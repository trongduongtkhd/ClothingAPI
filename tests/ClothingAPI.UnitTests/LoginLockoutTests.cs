using ClothingAPI.DTOs.Auth;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// AuthService.LoginAsync: đếm số lần sai, khoá tạm 15 phút sau 5 lần sai.
/// ITokenService / IEmailService được mock; "thời gian" được giả lập bằng mốc LockoutEndUtc đặt sẵn trong dữ liệu.
public class LoginLockoutTests : UnitTestBase
{
    private const string Email = "dangnhap@test.local";
    private const string CorrectPassword = "Dung@1234";
    private const string WrongPassword = "Sai@0000";

    private readonly Mock<ITokenService> _tokenService = new();

    public LoginLockoutTests(ITestOutputHelper output) : base(output)
    {
        _tokenService.Setup(x => x.CreateToken(It.IsAny<User>(), It.IsAny<IEnumerable<string>>())).Returns(("fake-token", DateTime.UtcNow.AddMinutes(15)));
    }

    private async Task<(AuthResponseDto? Result, Exception? Error)> LoginAsync(string password)
    {
        await using var context = Db.NewContext();
        var service = new AuthService(
            context,
            _tokenService.Object,
            Mock.Of<IEmailService>(),
            Options.Create(new FrontendSettings { BaseUrl = "http://localhost:4200" }),
            NullLogger<AuthService>.Instance,
            Mock.Of<IRefreshTokenService>(),
            Mock.Of<ITwoFactorService>()); // User trong các test này không bật 2FA.

        AuthResponseDto? result = null;
        var error = await CaptureAsync(async () => result = (await service.LoginAsync(new LoginDto { Email = Email, Password = password })).Auth);
        return (result, error);
    }

    private User LoadUser() => Db.Query(db => db.Users.Single(u => u.Email == Email));

    private void Report(AuthResponseDto? result, Exception? error)
    {
        var user = LoadUser();
        var lockout = user.LockoutEndUtc.HasValue
            ? $"khoá đến {user.LockoutEndUtc.Value:HH:mm:ss} UTC (≈ hiện tại + {(user.LockoutEndUtc.Value - DateTime.UtcNow).TotalMinutes:0.0} phút)"
            : "không bị khoá (LockoutEndUtc = null)";
        var outcome = error is null ? $"đăng nhập thành công, accessToken = \"{result!.AccessToken}\"" : Describe(error);
        Actual($"{outcome}; FailedLoginAttempts = {user.FailedLoginAttempts}; {lockout}");
    }

    [Fact]
    public async Task UT065_DangNhap_Sai4Lan_ChuaKhoa()
    {
        Db.AddUser(Email, CorrectPassword, failedAttempts: 3);
        Step1("Tài khoản đang hoạt động, FailedLoginAttempts = 3, chưa khoá; đăng nhập với mật khẩu sai (lần thứ 4)");

        var (result, error) = await LoginAsync(WrongPassword);
        Report(result, error);

        var user = LoadUser();
        Assert.IsType<UnauthorizedException>(error);
        Assert.Equal("Email hoặc mật khẩu không đúng.", error!.Message);
        Assert.Equal(4, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndUtc);
    }

    [Fact]
    public async Task UT066_DangNhap_Sai5Lan_KhoaTam15Phut()
    {
        Db.AddUser(Email, CorrectPassword, failedAttempts: 4);
        Step1("Tài khoản đang hoạt động, FailedLoginAttempts = 4, chưa khoá; đăng nhập với mật khẩu sai (lần thứ 5)");

        var before = DateTime.UtcNow;
        var (result, error) = await LoginAsync(WrongPassword);
        var after = DateTime.UtcNow;
        Report(result, error);

        var user = LoadUser();
        Assert.IsType<UnauthorizedException>(error);
        Assert.NotNull(user.LockoutEndUtc);
        Assert.InRange(user.LockoutEndUtc!.Value, before.AddMinutes(15), after.AddMinutes(15));
    }

    [Fact]
    public async Task UT067_DangNhap_DungTrongThoiGianKhoa_BiTuChoi()
    {
        Db.AddUser(Email, CorrectPassword, lockoutEndUtc: DateTime.UtcNow.AddMinutes(10));
        Step1("Tài khoản đang bị khoá (LockoutEndUtc = hiện tại + 10 phút); đăng nhập với mật khẩu ĐÚNG");

        var (result, error) = await LoginAsync(CorrectPassword);
        Report(result, error);

        Assert.IsType<UnauthorizedException>(error);
        Assert.StartsWith("Tài khoản tạm khóa do đăng nhập sai quá nhiều lần", error!.Message);
    }

    [Fact]
    public async Task UT068_DangNhap_HetThoiGianKhoa_ThanhCong()
    {
        // Giả lập thời gian: bị khoá từ 16 phút trước ⇒ hết khoá (15 phút) cách đây 1 phút.
        Db.AddUser(Email, CorrectPassword, lockoutEndUtc: DateTime.UtcNow.AddMinutes(-1));
        Step1("Tài khoản bị khoá từ 16 phút trước (LockoutEndUtc = hiện tại − 1 phút, đã hết 15 phút khoá); đăng nhập với mật khẩu đúng");

        var (result, error) = await LoginAsync(CorrectPassword);
        Report(result, error);

        Assert.Null(error);
        Assert.Equal("fake-token", result!.AccessToken);
        Assert.Null(LoadUser().LockoutEndUtc);
    }

    [Fact]
    public async Task UT069_DangNhap_ThanhCong_DatLaiBoDem()
    {
        Db.AddUser(Email, CorrectPassword, failedAttempts: 3);
        Step1("Tài khoản đang hoạt động, FailedLoginAttempts = 3; đăng nhập với mật khẩu đúng");

        var (result, error) = await LoginAsync(CorrectPassword);
        Report(result, error);

        Assert.Null(error);
        Assert.False(string.IsNullOrEmpty(result!.AccessToken));
        Assert.Equal(0, LoadUser().FailedLoginAttempts);
    }

    [Fact]
    public async Task UT070_DangNhap_TaiKhoanVoHieuHoa_BiTuChoi()
    {
        Db.AddUser(Email, CorrectPassword, isActive: false);
        Step1("Tài khoản bị vô hiệu hoá (IsActive = false); đăng nhập với mật khẩu đúng");

        var (result, error) = await LoginAsync(CorrectPassword);
        Report(result, error);

        Assert.IsType<UnauthorizedException>(error);
        Assert.Equal("Tài khoản của bạn đã bị khóa.", error!.Message);
    }
}
