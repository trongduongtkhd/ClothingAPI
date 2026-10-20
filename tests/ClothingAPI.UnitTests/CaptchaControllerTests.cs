using ClothingAPI.Controllers;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// AuthController: CAPTCHA là cổng đầu tiên của login/register.
/// IAuthService là mock Strict: nếu controller lỡ gọi tới (tức là chạm tới DB / verify mật khẩu) thì test đỏ ngay.
public class CaptchaControllerTests : UnitTestBase
{
    private const string Token = "token-captcha-khong-duoc-xuat-hien-trong-log";

    private readonly Mock<IAuthService> _authService = new(MockBehavior.Strict);
    private readonly Mock<ITurnstileService> _turnstile = new();
    private readonly ListLogger<AuthController> _logger = new();

    public CaptchaControllerTests(ITestOutputHelper output) : base(output)
    {
    }

    private AuthController NewController()
    {
        var refreshTokens = new Mock<IRefreshTokenService>();
        refreshTokens
            .Setup(x => x.IssueAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new IssuedRefreshToken("refresh-gia-lap", DateTime.UtcNow.AddDays(7)));

        return new AuthController(
            _authService.Object,
            refreshTokens.Object,
            Options.Create(new RefreshTokenSettings()),
            Mock.Of<ITwoFactorService>(),
            _turnstile.Object,
            _logger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private void TurnstileReturns(TurnstileVerifyStatus status, params string[] errorCodes) =>
        _turnstile
            .Setup(x => x.VerifyAsync(Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TurnstileVerifyResult(status, errorCodes, $"Thông báo cho {status}", status == TurnstileVerifyStatus.Unavailable ? "Timeout" : null));

    private static LoginDto Login() => new() { Email = "khach@test.local", Password = "Sai@0000", CaptchaToken = Token };

    private static RegisterDto Register() => new()
    {
        FullName = "Bot đăng ký",
        Email = "bot@test.local",
        Password = "Bot@12345",
        ConfirmPassword = "Bot@12345",
        CaptchaToken = Token
    };

    private void Report(IActionResult? result)
    {
        var status = result is ObjectResult objectResult ? objectResult.StatusCode?.ToString() ?? "200" : result?.GetType().Name ?? "(null)";
        Actual($"HTTP {status}; IAuthService bị gọi {_authService.Invocations.Count} lần; log: " +
               string.Join(" || ", _logger.Entries.Select(e => $"[{e.Level}] {e.Message}")));
    }

    private void AssertWarningWithoutToken(string expectedFragment)
    {
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(expectedFragment));
        Assert.All(_logger.Entries, e => Assert.DoesNotContain(Token, e.Message));
    }

    [Fact]
    public async Task UT133_Login_CaptchaSai_400_KhongGoiAuthService()
    {
        TurnstileReturns(TurnstileVerifyStatus.Invalid, "invalid-input-response");
        Step1("POST /auth/login kèm token CAPTCHA mà Cloudflare báo không hợp lệ");

        var response = await NewController().Login(Login(), CancellationToken.None);
        Report(response.Result);

        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Empty(_authService.Invocations); // không tra DB, không verify mật khẩu, không đụng FailedLoginAttempts
        AssertWarningWithoutToken("invalid-input-response");
    }

    [Fact]
    public async Task UT134_Login_KhongGoiDuocCloudflare_ChoQua()
    {
        TurnstileReturns(TurnstileVerifyStatus.Unavailable);
        _authService
            .Setup(x => x.LoginAsync(It.IsAny<LoginDto>(), It.IsAny<string?>()))
            .ReturnsAsync(new LoginResult(new AuthResponseDto { AccessToken = "access-gia-lap", User = new CurrentUserDto { UserId = 1 } }, null));
        Step1("POST /auth/login khi không gọi được Cloudflare (timeout)");

        var response = await NewController().Login(Login(), CancellationToken.None);
        Report(response.Result);

        Assert.IsType<OkObjectResult>(response.Result);
        _authService.Verify(x => x.LoginAsync(It.IsAny<LoginDto>(), It.IsAny<string?>()), Times.Once);
        AssertWarningWithoutToken("ĐÃ CHO QUA");
    }

    [Fact]
    public async Task UT135_Register_KhongGoiDuocCloudflare_Chan503()
    {
        TurnstileReturns(TurnstileVerifyStatus.Unavailable);
        Step1("POST /auth/register khi không gọi được Cloudflare (timeout)");

        var response = await NewController().Register(Register(), CancellationToken.None);
        Report(response.Result);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("Hệ thống xác minh đang bận, vui lòng thử lại sau ít phút.", Assert.IsType<ApiResponse<object>>(result.Value).Message);
        Assert.Empty(_authService.Invocations);
        AssertWarningWithoutToken("ĐÃ CHẶN");
    }

    [Fact]
    public async Task UT136_Register_CaptchaSai_400_KhongGoiAuthService()
    {
        TurnstileReturns(TurnstileVerifyStatus.Invalid, "timeout-or-duplicate");
        Step1("POST /auth/register kèm token CAPTCHA đã dùng (timeout-or-duplicate)");

        var response = await NewController().Register(Register(), CancellationToken.None);
        Report(response.Result);

        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Empty(_authService.Invocations);
        AssertWarningWithoutToken("timeout-or-duplicate");
    }
}
