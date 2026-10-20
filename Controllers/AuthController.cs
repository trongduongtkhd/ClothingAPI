using ClothingAPI.DTOs.Auth;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace ClothingAPI.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly RefreshTokenSettings _refreshTokenSettings;
        private readonly ITwoFactorService _twoFactorService;
        private readonly ITurnstileService _turnstileService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthService authService,
            IRefreshTokenService refreshTokenService,
            IOptions<RefreshTokenSettings> refreshTokenSettings,
            ITwoFactorService twoFactorService,
            ITurnstileService turnstileService,
            ILogger<AuthController> logger)
        {
            _authService = authService;
            _refreshTokenService = refreshTokenService;
            _refreshTokenSettings = refreshTokenSettings.Value;
            _twoFactorService = twoFactorService;
            _turnstileService = turnstileService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register(
            [FromBody] RegisterDto registerDto,
            CancellationToken cancellationToken)
        {
            // Không gọi được Cloudflare => CHẶN (fail-closed): ngưng đăng ký vài phút không gây thiệt hại,
            // còn cho qua thì bot đăng ký hàng loạt để lấy coupon khách mới.
            var captchaRejection = await CheckCaptchaAsync(registerDto.CaptchaToken, "register", failOpen: false, cancellationToken);
            if (captchaRejection is not null)
            {
                return captchaRejection;
            }

            var result = await _authService.RegisterAsync(registerDto);

            await IssueRefreshTokenCookieAsync(result.User.UserId);

            return StatusCode(
                StatusCodes.Status201Created,
                new ApiResponse<AuthResponseDto>(
                    true,
                    "Đăng ký tài khoản thành công.",
                    result
                )
            );
        }

        // Trả về AuthResponseDto (đăng nhập xong) hoặc TwoFactorChallengeDto (requiresTwoFactor = true, cần bước 2).
        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<object>>> Login(
            [FromBody] LoginDto loginDto,
            CancellationToken cancellationToken)
        {
            // Không gọi được Cloudflare => CHO QUA (fail-open): chặn đăng nhập là tự gây sự cố,
            // và login vẫn còn khóa tài khoản + 2FA bảo vệ.
            var captchaRejection = await CheckCaptchaAsync(loginDto.CaptchaToken, "login", failOpen: true, cancellationToken);
            if (captchaRejection is not null)
            {
                return captchaRejection;
            }

            var result = await _authService.LoginAsync(loginDto, GetClientIp());

            if (result.Challenge is not null)
            {
                // Mới qua lớp 1: KHÔNG cấp refresh cookie và KHÔNG thu hồi phiên cũ trong cookie
                // (người chỉ biết mật khẩu không được phép đá phiên đang có của trình duyệt này).
                return Ok(new ApiResponse<object>(
                    true,
                    "Vui lòng nhập mã xác thực hai lớp.",
                    result.Challenge
                ));
            }

            return Ok(new ApiResponse<object>(
                true,
                "Đăng nhập thành công.",
                await CompleteSignInAsync(result.Auth!)
            ));
        }

        // Đăng nhập bước 2 bằng mã TOTP 6 số. Lỗi dự kiến trả 401 trực tiếp (không throw) để không ghi LogError.
        [AllowAnonymous]
        [HttpPost("two-factor/verify")]
        public async Task<ActionResult<ApiResponse<AuthResponseDto>>> VerifyTwoFactor([FromBody] TwoFactorVerifyDto dto)
        {
            var result = await _twoFactorService.VerifyLoginCodeAsync(dto.TwoFactorToken, dto.Code, GetClientIp());

            return await TwoFactorSignInResultAsync(result);
        }

        // Đăng nhập bước 2 bằng mã khôi phục (dùng một lần).
        [AllowAnonymous]
        [HttpPost("two-factor/recovery")]
        public async Task<ActionResult<ApiResponse<AuthResponseDto>>> VerifyRecoveryCode([FromBody] TwoFactorRecoveryDto dto)
        {
            var result = await _twoFactorService.VerifyLoginRecoveryCodeAsync(dto.TwoFactorToken, dto.RecoveryCode, GetClientIp());

            return await TwoFactorSignInResultAsync(result);
        }

        private async Task<ActionResult<ApiResponse<AuthResponseDto>>> TwoFactorSignInResultAsync(TwoFactorLoginResult result)
        {
            if (!result.Succeeded)
            {
                var message = result.Status switch
                {
                    TwoFactorLoginStatus.InvalidCode => "Mã xác thực không đúng.",
                    TwoFactorLoginStatus.Replayed => "Mã này đã được sử dụng. Vui lòng đợi mã mới trên ứng dụng xác thực.",
                    TwoFactorLoginStatus.LockedOut =>
                        $"Tài khoản tạm khóa do nhập sai quá nhiều lần. Vui lòng thử lại sau {result.RemainingLockoutMinutes} phút.",
                    _ => "Phiên xác thực đã hết hạn. Vui lòng đăng nhập lại."
                };

                // "reason" để frontend biết khi nào phải quay về bước nhập mật khẩu (ChallengeExpired, LockedOut).
                return Unauthorized(new ApiResponse<object>(false, message, new { reason = result.Status.ToString() }));
            }

            var auth = await _authService.CreateAuthResponseForUserAsync(result.UserId);

            var successMessage = result.RemainingRecoveryCodes is int remaining
                ? $"Đăng nhập thành công bằng mã khôi phục. Bạn còn {remaining} mã khôi phục chưa dùng."
                : "Đăng nhập thành công.";

            return Ok(new ApiResponse<AuthResponseDto>(true, successMessage, await CompleteSignInAsync(auth)));
        }

        // Cổng ĐẦU TIÊN của login/register: chạy trước khi tra DB và verify mật khẩu, và KHÔNG tính vào
        // khóa tài khoản (nếu tính, chỉ cần gửi token rác kèm email nạn nhân là khóa được tài khoản người khác).
        // Trả lỗi trực tiếp (không throw) để ExceptionMiddleware không ghi LogError cho lỗi xảy ra thường xuyên.
        // Trả null = cho đi tiếp. TUYỆT ĐỐI không ghi token ra log.
        private async Task<ActionResult?> CheckCaptchaAsync(string? token, string action, bool failOpen, CancellationToken cancellationToken)
        {
            var result = await _turnstileService.VerifyAsync(token, cancellationToken);

            switch (result.Status)
            {
                case TurnstileVerifyStatus.Valid:
                    return null;

                case TurnstileVerifyStatus.Unavailable when failOpen:
                    _logger.LogWarning(
                        "Không gọi được Cloudflare Turnstile ({Detail}) khi {Action} từ IP {Ip}: ĐÃ CHO QUA (fail-open).",
                        result.FailureDetail, action, GetClientIp());
                    return null;

                case TurnstileVerifyStatus.Unavailable:
                    _logger.LogWarning(
                        "Không gọi được Cloudflare Turnstile ({Detail}) khi {Action} từ IP {Ip}: ĐÃ CHẶN (fail-closed).",
                        result.FailureDetail, action, GetClientIp());
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        new ApiResponse<object>(false, "Hệ thống xác minh đang bận, vui lòng thử lại sau ít phút."));

                default:
                    _logger.LogWarning(
                        "CAPTCHA không hợp lệ khi {Action} từ IP {Ip}, error-codes: {ErrorCodes}.",
                        action, GetClientIp(), string.Join(", ", result.ErrorCodes));
                    return BadRequest(new ApiResponse<object>(false, result.Message));
            }
        }

        // Chỉ gọi khi đã xác thực ĐỦ (mật khẩu, và mã 2FA nếu có bật).
        private async Task<AuthResponseDto> CompleteSignInAsync(AuthResponseDto auth)
        {
            // Trình duyệt đang giữ cookie của phiên trước (có thể của tài khoản khác) => thu hồi phiên đó
            // trước khi cấp phiên mới. Chỉ làm sau khi đăng nhập thành công.
            await _refreshTokenService.RevokeFamilyAsync(
                Request.Cookies[_refreshTokenSettings.CookieName],
                RefreshTokenRevokeReasons.Login);

            await IssueRefreshTokenCookieAsync(auth.User.UserId);

            return auth;
        }

        // Không cần header Authorization (access token cũ có thể đã hết hạn): người dùng được xác định
        // hoàn toàn bằng refresh token trong cookie HttpOnly.
        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Refresh()
        {
            var rotation = await _refreshTokenService.RotateAsync(
                Request.Cookies[_refreshTokenSettings.CookieName],
                GetClientIp(),
                Request.Headers.UserAgent.ToString());

            if (!rotation.Succeeded)
            {
                // ConcurrentReuse: request "thắng" đã set cookie mới. Nếu phản hồi 401 của request "thua"
                // đến sau mà lại xoá cookie thì trình duyệt mất luôn cookie mới => KHÔNG xoá trong trường hợp này.
                if (rotation.Status != RefreshRotationStatus.ConcurrentReuse)
                {
                    DeleteRefreshTokenCookie();
                }

                // Trả 401 trực tiếp (không throw) để ExceptionMiddleware không ghi log Error mỗi lần mở trang.
                return Unauthorized(new ApiResponse<object>(
                    false,
                    "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."
                ));
            }

            var result = await _authService.CreateAuthResponseForUserAsync(rotation.UserId);

            SetRefreshTokenCookie(rotation.NewToken!);

            return Ok(new ApiResponse<AuthResponseDto>(
                true,
                "Làm mới phiên đăng nhập thành công.",
                result
            ));
        }

        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<ActionResult<ApiResponse<object>>> Logout()
        {
            await _refreshTokenService.RevokeFamilyAsync(
                Request.Cookies[_refreshTokenSettings.CookieName],
                RefreshTokenRevokeReasons.Logout);

            DeleteRefreshTokenCookie();

            return Ok(new ApiResponse<object>(
                true,
                "Đăng xuất thành công."
            ));
        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult<ApiResponse<object>>> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            await _authService.ForgotPasswordAsync(forgotPasswordDto);

            return Ok(new ApiResponse<object>(
                true,
                "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi hướng dẫn đặt lại mật khẩu."
            ));
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult<ApiResponse<object>>> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
            await _authService.ResetPasswordAsync(resetPasswordDto);

            return Ok(new ApiResponse<object>(
                true,
                "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại."
            ));
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<ApiResponse<CurrentUserDto>>> GetCurrentUser()
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdText, out var userId))
            {
                return Unauthorized(new ApiResponse<object>(
                    false,
                    "Token không hợp lệ."
                ));
            }

            var result = await _authService.GetCurrentUserAsync(userId);

            return Ok(new ApiResponse<CurrentUserDto>(
                true,
                "Lấy thông tin tài khoản thành công.",
                result
            ));
        }

        private async Task IssueRefreshTokenCookieAsync(int userId)
        {
            var refreshToken = await _refreshTokenService.IssueAsync(
                userId,
                GetClientIp(),
                Request.Headers.UserAgent.ToString());

            SetRefreshTokenCookie(refreshToken);
        }

        // Refresh token chỉ đi qua cookie HttpOnly: JavaScript (kể cả mã XSS) không đọc được,
        // và không bao giờ xuất hiện trong body JSON.
        private void SetRefreshTokenCookie(IssuedRefreshToken refreshToken)
        {
            var options = CreateCookieOptions();
            options.Expires = refreshToken.ExpiresAt;

            Response.Cookies.Append(_refreshTokenSettings.CookieName, refreshToken.RawToken, options);
        }

        // Phải dùng cùng Path/SameSite/Secure với lúc set thì trình duyệt mới xoá đúng cookie.
        private void DeleteRefreshTokenCookie()
        {
            Response.Cookies.Delete(_refreshTokenSettings.CookieName, CreateCookieOptions());
        }

        private CookieOptions CreateCookieOptions() => new()
        {
            HttpOnly = true,
            Secure = _refreshTokenSettings.CookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = _refreshTokenSettings.CookiePath,
            IsEssential = true
        };

        private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
