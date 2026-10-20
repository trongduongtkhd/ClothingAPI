using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Xunit.Abstractions;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

/// Đăng nhập hai bước qua HTTP thật. Mã TOTP được tính từ secret trả về lúc thiết lập (không cần điện thoại).
/// Lúc bật 2FA đã "dùng" bước thời gian hiện tại, nên khi đăng nhập test dùng mã của bước KẾ TIẾP
/// (nằm trong cửa sổ +1) để không phải chờ 30 giây.
public class TwoFactorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string CookieName = "refreshToken";

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public TwoFactorIntegrationTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private record TwoFactorUser(string Email, string Secret, List<string> RecoveryCodes);

    private void Log(string message) => _output.WriteLine(message);

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string? GetSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith($"{CookieName}=", StringComparison.Ordinal))
            : null;

    private static string NextStepCode(string secret) =>
        new Totp(Base32Encoding.ToBytes(secret.Replace(" ", ""))).ComputeTotp(DateTime.UtcNow.AddSeconds(30));

    private static string? DataString(JsonElement body, string name) =>
        body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// Đăng ký tài khoản mới, thiết lập và bật 2FA qua API.
    private async Task<TwoFactorUser> RegisterWithTwoFactorAsync(HttpClient client, string prefix)
    {
        var email = UniqueEmail(prefix);
        var register = await RegisterAsync(client, email, DefaultPassword);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var accessToken = (await ReadJsonAsync(register)).GetProperty("data").GetProperty("accessToken").GetString()!;

        Authorize(client, accessToken);

        var setup = await client.PostAsync("/api/users/two-factor/setup", null);
        var setupBody = await ReadJsonAsync(setup);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Contains("no-store", setup.Headers.CacheControl?.ToString() ?? "");
        var secret = DataString(setupBody, "secret")!;

        var code = new Totp(Base32Encoding.ToBytes(secret.Replace(" ", ""))).ComputeTotp(DateTime.UtcNow);
        var enable = await client.PostAsJsonAsync("/api/users/two-factor/enable", new { code });
        var enableBody = await ReadJsonAsync(enable);
        Assert.True(enable.StatusCode == HttpStatusCode.OK, $"Bật 2FA thất bại: {(int)enable.StatusCode} {enableBody}");
        Assert.Contains("no-store", enable.Headers.CacheControl?.ToString() ?? "");

        var recoveryCodes = enableBody.GetProperty("data").GetProperty("recoveryCodes")
            .EnumerateArray().Select(x => x.GetString()!).ToList();

        client.DefaultRequestHeaders.Authorization = null;
        return new TwoFactorUser(email, secret, recoveryCodes);
    }

    private async Task<string> LoginStepOneAsync(HttpClient client, string email)
    {
        var login = await LoginAsync(client, email, DefaultPassword);
        var body = await ReadJsonAsync(login);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(body.GetProperty("data").GetProperty("requiresTwoFactor").GetBoolean());
        return DataString(body, "twoFactorToken")!;
    }

    [Fact]
    public async Task IT017_Login_With2FA_ReturnsChallenge_NoAccessToken_NoRefreshCookie()
    {
        var client = NewClient();
        var user = await RegisterWithTwoFactorAsync(client, "it017");

        var login = await LoginAsync(client, user.Email, DefaultPassword);
        var body = await ReadJsonAsync(login);
        var data = body.GetProperty("data");
        Log($"POST /api/auth/login (tài khoản bật 2FA) -> {(int)login.StatusCode}; data có accessToken: {data.TryGetProperty("accessToken", out _)}; Set-Cookie refreshToken: {GetSetCookie(login) ?? "(không có)"}");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(data.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.False(data.TryGetProperty("accessToken", out _));
        Assert.False(data.TryGetProperty("user", out _));
        Assert.Null(GetSetCookie(login));
        Assert.Matches("^[0-9A-F]{64}$", data.GetProperty("twoFactorToken").GetString());

        // Sau khi bật 2FA thì không endpoint nào trả lại secret.
        var regetAccess = await VerifyAndGetAccessTokenAsync(client, data.GetProperty("twoFactorToken").GetString()!, user.Secret);
        Authorize(client, regetAccess);
        var setupAgain = await client.PostAsync("/api/users/two-factor/setup", null);
        Log($"POST /api/users/two-factor/setup khi đã bật -> {(int)setupAgain.StatusCode}");
        Assert.Equal(HttpStatusCode.BadRequest, setupAgain.StatusCode);
        Assert.DoesNotContain(user.Secret.Replace(" ", ""), (await setupAgain.Content.ReadAsStringAsync()).Replace(" ", ""));
    }

    private async Task<string> VerifyAndGetAccessTokenAsync(HttpClient client, string challenge, string secret)
    {
        var verify = await client.PostAsJsonAsync("/api/auth/two-factor/verify", new { twoFactorToken = challenge, code = NextStepCode(secret) });
        var body = await ReadJsonAsync(verify);
        Assert.True(verify.StatusCode == HttpStatusCode.OK, $"Xác thực 2FA thất bại: {(int)verify.StatusCode} {body}");
        return DataString(body, "accessToken")!;
    }

    [Fact]
    public async Task IT018_ChallengeTokenAsBearer_OnMe_Returns401()
    {
        var client = NewClient();
        var user = await RegisterWithTwoFactorAsync(client, "it018");
        var challenge = await LoginStepOneAsync(client, user.Email);

        Authorize(client, challenge);
        var me = await client.GetAsync("/api/auth/me");
        Log($"GET /api/auth/me với Authorization: Bearer <challenge> -> {(int)me.StatusCode}");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task IT019_TwoStepLogin_FullFlow_IssuesTokenAndCookie_WrongCodeIs401()
    {
        var client = NewClient();
        var user = await RegisterWithTwoFactorAsync(client, "it019");
        var challenge = await LoginStepOneAsync(client, user.Email);

        var correct = NextStepCode(user.Secret);
        var wrong = await client.PostAsJsonAsync("/api/auth/two-factor/verify",
            new { twoFactorToken = challenge, code = correct == "000000" ? "111111" : "000000" });
        var wrongBody = await ReadJsonAsync(wrong);
        Log($"POST /api/auth/two-factor/verify (mã sai) -> {(int)wrong.StatusCode}, reason = {wrongBody.GetProperty("data").GetProperty("reason").GetString()}");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("InvalidCode", wrongBody.GetProperty("data").GetProperty("reason").GetString());
        Assert.Null(GetSetCookie(wrong));

        var verify = await client.PostAsJsonAsync("/api/auth/two-factor/verify", new { twoFactorToken = challenge, code = correct });
        var verifyBody = await ReadJsonAsync(verify);
        var setCookie = GetSetCookie(verify);
        Log($"POST /api/auth/two-factor/verify (mã đúng) -> {(int)verify.StatusCode}; có accessToken: {DataString(verifyBody, "accessToken") is not null}; Set-Cookie refreshToken: {setCookie is not null}");

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.NotNull(setCookie);
        Assert.Contains("httponly", setCookie!.ToLowerInvariant());

        Authorize(client, DataString(verifyBody, "accessToken")!);
        var me = await client.GetAsync("/api/auth/me");
        var meBody = await ReadJsonAsync(me);
        Log($"GET /api/auth/me -> {(int)me.StatusCode}, twoFactorEnabled = {meBody.GetProperty("data").GetProperty("twoFactorEnabled").GetBoolean()}");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(meBody.GetProperty("data").GetProperty("twoFactorEnabled").GetBoolean());

        // Refresh không đòi 2FA lại.
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", setCookie.Split(';')[0]);
        var refresh = await client.SendAsync(refreshRequest);
        Log($"POST /api/auth/refresh bằng cookie vừa nhận -> {(int)refresh.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        // Challenge đã dùng thì không dùng lại được.
        var reuse = await client.PostAsJsonAsync("/api/auth/two-factor/verify", new { twoFactorToken = challenge, code = correct });
        Log($"Dùng lại challenge đã tiêu thụ -> {(int)reuse.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var wrongCount = await WithDbAsync(_factory, db => db.Users.Where(u => u.Email == user.Email).Select(u => u.FailedLoginAttempts).SingleAsync());
        Assert.Equal(0, wrongCount); // đăng nhập đủ 2 lớp thành công => reset bộ đếm
    }

    [Fact]
    public async Task IT020_RecoveryCode_WorksOnlyOnce()
    {
        var client = NewClient();
        var user = await RegisterWithTwoFactorAsync(client, "it020");
        var code = user.RecoveryCodes[0];

        var first = await client.PostAsJsonAsync("/api/auth/two-factor/recovery",
            new { twoFactorToken = await LoginStepOneAsync(client, user.Email), recoveryCode = code });
        var firstBody = await ReadJsonAsync(first);
        Log($"Mã khôi phục lần 1 -> {(int)first.StatusCode}: {firstBody.GetProperty("message").GetString()}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(GetSetCookie(first));

        var second = await client.PostAsJsonAsync("/api/auth/two-factor/recovery",
            new { twoFactorToken = await LoginStepOneAsync(client, user.Email), recoveryCode = code });
        Log($"Mã khôi phục lần 2 (cùng mã) -> {(int)second.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Null(GetSetCookie(second));

        var rawStored = await WithDbAsync(_factory, db => db.TwoFactorRecoveryCodes
            .AnyAsync(r => r.CodeHash.Contains(code.Replace("-", ""))));
        Assert.False(rawStored);
    }

    [Fact]
    public async Task IT021_Disable_RequiresPasswordAndCode_ThenLoginNeedsOnlyPassword()
    {
        var client = NewClient();
        var user = await RegisterWithTwoFactorAsync(client, "it021");
        var accessToken = await VerifyAndGetAccessTokenAsync(client, await LoginStepOneAsync(client, user.Email), user.Secret);
        Authorize(client, accessToken);

        var noCode = await client.PostAsJsonAsync("/api/users/two-factor/disable", new { password = DefaultPassword });
        var wrongPassword = await client.PostAsJsonAsync("/api/users/two-factor/disable",
            new { password = "Sai@00000", recoveryCode = user.RecoveryCodes[1] });
        Log($"Tắt 2FA thiếu mã -> {(int)noCode.StatusCode}; sai mật khẩu -> {(int)wrongPassword.StatusCode}");
        Assert.Equal(HttpStatusCode.BadRequest, noCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);

        // Bước TOTP kế tiếp đã dùng để đăng nhập => tắt bằng mã khôi phục (không phải chờ 30 giây).
        var disable = await client.PostAsJsonAsync("/api/users/two-factor/disable",
            new { password = DefaultPassword, recoveryCode = user.RecoveryCodes[1] });
        Log($"Tắt 2FA với mật khẩu + mã khôi phục -> {(int)disable.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var login = await LoginAsync(client, user.Email, DefaultPassword);
        var body = await ReadJsonAsync(login);
        Log($"Đăng nhập lại sau khi tắt -> {(int)login.StatusCode}; có accessToken: {DataString(body, "accessToken") is not null}; requiresTwoFactor = {body.GetProperty("data").GetProperty("requiresTwoFactor").GetBoolean()}");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotNull(DataString(body, "accessToken"));
        Assert.False(body.GetProperty("data").GetProperty("requiresTwoFactor").GetBoolean());
        Assert.NotNull(GetSetCookie(login));
    }
}
