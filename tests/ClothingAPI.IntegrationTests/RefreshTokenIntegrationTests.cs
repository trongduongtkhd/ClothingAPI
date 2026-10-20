using System.Net;
using ClothingAPI.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

/// Luồng refresh token qua HTTP thật: cookie HttpOnly, xoay vòng, phát hiện dùng lại, đăng xuất.
/// Client tắt HandleCookies để tự điều khiển cookie gửi đi (giống việc copy cookie sang Postman).
public class RefreshTokenIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string CookieName = "refreshToken";

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public RefreshTokenIntegrationTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private void Log(string message) => _output.WriteLine(message);

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string? GetSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith($"{CookieName}=", StringComparison.Ordinal))
            : null;

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';')[0][(CookieName.Length + 1)..];

    private static Task<HttpResponseMessage> PostWithCookieAsync(HttpClient client, string url, string? refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (refreshToken is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={refreshToken}");
        }
        return client.SendAsync(request);
    }

    private async Task<(string RefreshToken, string SetCookie, string Body)> RegisterAndLoginAsync(HttpClient client, string prefix)
    {
        var email = UniqueEmail(prefix);
        var register = await RegisterAsync(client, email, DefaultPassword);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await LoginAsync(client, email, DefaultPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var setCookie = GetSetCookie(login);
        Assert.NotNull(setCookie);
        return (CookieValue(setCookie!), setCookie!, await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IT011_Login_SetsHttpOnlyCookie_BodyHasNoRefreshToken()
    {
        var client = NewClient();
        var (refreshToken, setCookie, body) = await RegisterAndLoginAsync(client, "it011");
        Log($"POST /api/auth/login -> Set-Cookie: {setCookie.Replace(refreshToken, refreshToken[..8] + "...")}");

        var lower = setCookie.ToLowerInvariant();
        Assert.Contains("httponly", lower);
        Assert.Contains("samesite=strict", lower);
        Assert.Contains("path=/api/auth", lower);
        Assert.Contains("expires=", lower);
        Assert.DoesNotContain(refreshToken, body);
        Log($"Body đăng nhập có chứa refresh token: {body.Contains(refreshToken)}");

        var hash = ClothingAPI.Services.Implementations.RefreshTokenService.HashToken(refreshToken);
        var saved = await WithDbAsync(_factory, db => db.RefreshTokens.AnyAsync(t => t.TokenHash == hash));
        var rawSaved = await WithDbAsync(_factory, db => db.RefreshTokens.AnyAsync(t => t.TokenHash == refreshToken));
        Log($"DB có dòng hash: {saved}; DB có lưu bản gốc: {rawSaved}");
        Assert.True(saved);
        Assert.False(rawSaved);
    }

    [Fact]
    public async Task IT012_Refresh_Rotates_ThenReuseOfOldToken_RevokesFamily()
    {
        var client = NewClient();
        var (oldToken, _, _) = await RegisterAndLoginAsync(client, "it012");

        var refresh = await PostWithCookieAsync(client, "/api/auth/refresh", oldToken);
        var refreshBody = await ReadJsonAsync(refresh);
        var newSetCookie = GetSetCookie(refresh);
        Log($"POST /api/auth/refresh (cookie hợp lệ, không có Authorization) -> {(int)refresh.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.False(string.IsNullOrEmpty(refreshBody.GetProperty("data").GetProperty("accessToken").GetString()));
        Assert.NotNull(newSetCookie);
        var newToken = CookieValue(newSetCookie!);
        Assert.NotEqual(oldToken, newToken);

        // Access token mới dùng được.
        Authorize(client, refreshBody.GetProperty("data").GetProperty("accessToken").GetString()!);
        var profile = await client.GetAsync("/api/users/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

        // Kẻ tấn công gửi lại token cũ (grace = 0 trong môi trường test).
        var reuse = await PostWithCookieAsync(client, "/api/auth/refresh", oldToken);
        Log($"POST /api/auth/refresh bằng token CŨ -> {(int)reuse.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // Cả family bị thu hồi: token mới của người dùng hợp lệ cũng hết dùng được.
        var legit = await PostWithCookieAsync(client, "/api/auth/refresh", newToken);
        Log($"POST /api/auth/refresh bằng token MỚI sau khi bị dùng lại -> {(int)legit.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, legit.StatusCode);

        var newHash = ClothingAPI.Services.Implementations.RefreshTokenService.HashToken(newToken);
        var newRow = await WithDbAsync(_factory, db => db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == newHash));
        Log($"Token mới trong DB: RevokedReason = {newRow.RevokedReason}");
        Assert.Equal("ReuseDetected", newRow.RevokedReason);
    }

    [Fact]
    public async Task IT013_Logout_ThenRefresh_IsRejected()
    {
        var client = NewClient();
        var (refreshToken, _, _) = await RegisterAndLoginAsync(client, "it013");

        var logout = await PostWithCookieAsync(client, "/api/auth/logout", refreshToken);
        var deleteCookie = GetSetCookie(logout);
        Log($"POST /api/auth/logout -> {(int)logout.StatusCode}, Set-Cookie: {deleteCookie}");
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.NotNull(deleteCookie);
        Assert.Contains("expires=thu, 01 jan 1970", deleteCookie!.ToLowerInvariant());

        var refresh = await PostWithCookieAsync(client, "/api/auth/refresh", refreshToken);
        Log($"POST /api/auth/refresh sau đăng xuất -> {(int)refresh.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task IT015_ConcurrentReuse_DoesNotDeleteCookie()
    {
        // Bật grace 10 giây (factory gốc đặt 0); factory con dùng chung CSDL SQLite với factory gốc.
        var graceFactory = _factory.WithWebHostBuilder(b => b.UseSetting("RefreshTokenSettings:ReuseGraceSeconds", "10"));
        var client = graceFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (oldToken, _, _) = await RegisterAndLoginAsync(client, "it015");

        var winner = await PostWithCookieAsync(client, "/api/auth/refresh", oldToken);
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        var newToken = CookieValue(GetSetCookie(winner)!);

        // Request "thua" (gửi token cũ ngay sau khi đã xoay): 401 nhưng KHÔNG được xoá cookie mới.
        var loser = await PostWithCookieAsync(client, "/api/auth/refresh", oldToken);
        Log($"POST /api/auth/refresh bằng token vừa xoay (trong grace) -> {(int)loser.StatusCode}, Set-Cookie: {GetSetCookie(loser) ?? "(không có)"}");
        Assert.Equal(HttpStatusCode.Unauthorized, loser.StatusCode);
        Assert.Null(GetSetCookie(loser));

        // Token mới vẫn dùng được.
        var stillValid = await PostWithCookieAsync(client, "/api/auth/refresh", newToken);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);

        // Đối chứng: token không hợp lệ thì có Set-Cookie xoá cookie.
        var invalid = await PostWithCookieAsync(client, "/api/auth/refresh", new string('A', 64));
        var deleteCookie = GetSetCookie(invalid);
        Log($"POST /api/auth/refresh bằng token bịa -> {(int)invalid.StatusCode}, Set-Cookie: {deleteCookie}");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.NotNull(deleteCookie);
        Assert.Contains("expires=thu, 01 jan 1970", deleteCookie!.ToLowerInvariant());
    }

    [Fact]
    public async Task IT016_Login_RevokesPreviousSessionFromCookie()
    {
        var client = NewClient();
        var (tokenA, _, _) = await RegisterAndLoginAsync(client, "it016a");

        var emailB = UniqueEmail("it016b");
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(client, emailB, DefaultPassword)).StatusCode);

        // Trình duyệt vẫn đang giữ cookie của A khi đăng nhập tài khoản B.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { email = emailB, password = DefaultPassword, captchaToken = FakeTurnstileService.PassToken })
        };
        request.Headers.Add("Cookie", $"{CookieName}={tokenA}");
        var loginB = await client.SendAsync(request);
        Log($"POST /api/auth/login (B, kèm cookie của A) -> {(int)loginB.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, loginB.StatusCode);
        Assert.NotNull(GetSetCookie(loginB));

        var hashA = ClothingAPI.Services.Implementations.RefreshTokenService.HashToken(tokenA);
        var rowA = await WithDbAsync(_factory, db => db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hashA));
        Log($"Token của A trong DB: RevokedReason = {rowA.RevokedReason}");
        Assert.Equal("Login", rowA.RevokedReason);

        var refreshA = await PostWithCookieAsync(client, "/api/auth/refresh", tokenA);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshA.StatusCode);
    }

    [Fact]
    public async Task IT014_Refresh_WithoutCookie_Returns401()
    {
        var client = NewClient();

        var refresh = await PostWithCookieAsync(client, "/api/auth/refresh", null);
        Log($"POST /api/auth/refresh (không cookie) -> {(int)refresh.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
