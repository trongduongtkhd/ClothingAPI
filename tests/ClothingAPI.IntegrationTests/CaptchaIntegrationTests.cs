using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

/// CAPTCHA (Turnstile) qua HTTP thật, Cloudflare được thay bằng FakeTurnstileService (kết quả theo giá trị token).
/// Trọng tâm: CAPTCHA sai bị chặn TRƯỚC logic xác thực nên không bao giờ làm tăng FailedLoginAttempts.
public class CaptchaIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string WrongPassword = "Sai@00000";

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public CaptchaIntegrationTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private void Log(string message) => _output.WriteLine(message);

    private Task<int> FailedAttemptsAsync(string email) =>
        WithDbAsync(_factory, db => db.Users.AsNoTracking().Where(u => u.Email == email).Select(u => u.FailedLoginAttempts).SingleAsync());

    private async Task<string> RegisterUserAsync(HttpClient client, string prefix)
    {
        var email = UniqueEmail(prefix);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(client, email, DefaultPassword)).StatusCode);
        return email;
    }

    [Fact]
    public async Task IT022_Login_ThieuCaptchaToken_400_KhongTinhLanSai()
    {
        var client = _factory.CreateClient();
        var email = await RegisterUserAsync(client, "it022");

        var login = await LoginAsync(client, email, WrongPassword, captchaToken: null);
        var attempts = await FailedAttemptsAsync(email);
        Log($"POST /api/auth/login (không có captchaToken, sai mật khẩu) -> {(int)login.StatusCode}; FailedLoginAttempts = {attempts}");

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task IT023_Login_CaptchaSai_400_KhongTinhLanSai()
    {
        var client = _factory.CreateClient();
        var email = await RegisterUserAsync(client, "it023");

        // Kẻ tấn công gửi token rác kèm email nạn nhân: nếu được tính vào lockout thì khóa được tài khoản người khác.
        for (var i = 0; i < 6; i++)
        {
            var login = await LoginAsync(client, email, WrongPassword, FakeTurnstileService.InvalidToken);
            var body = await ReadJsonAsync(login);
            Log($"Lần {i + 1}: POST /api/auth/login (CAPTCHA sai, sai mật khẩu) -> {(int)login.StatusCode} {body.GetProperty("message").GetString()}");
            Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        }

        var attempts = await FailedAttemptsAsync(email);
        Log($"FailedLoginAttempts sau 6 lần = {attempts}");
        Assert.Equal(0, attempts);

        // Chủ tài khoản vẫn đăng nhập bình thường.
        var owner = await LoginAsync(client, email, DefaultPassword);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task IT024_Register_KhongGoiDuocCloudflare_503_KhongTaoUser()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail("it024");

        var register = await RegisterAsync(client, email, DefaultPassword, captchaToken: FakeTurnstileService.UnavailableToken);
        var body = await ReadJsonAsync(register);
        var exists = await WithDbAsync(_factory, db => db.Users.AnyAsync(u => u.Email == email));
        Log($"POST /api/auth/register (Cloudflare không phản hồi) -> {(int)register.StatusCode} {body.GetProperty("message").GetString()}; user được tạo: {exists}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, register.StatusCode);
        Assert.False(exists);
    }

    [Fact]
    public async Task IT025_Login_KhongGoiDuocCloudflare_ChoQua()
    {
        var client = _factory.CreateClient();
        var email = await RegisterUserAsync(client, "it025");

        var login = await LoginAsync(client, email, DefaultPassword, FakeTurnstileService.UnavailableToken);
        Log($"POST /api/auth/login (Cloudflare không phản hồi, đúng mật khẩu) -> {(int)login.StatusCode}");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
