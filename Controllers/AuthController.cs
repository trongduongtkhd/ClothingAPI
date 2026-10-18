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

        public AuthController(
            IAuthService authService,
            IRefreshTokenService refreshTokenService,
            IOptions<RefreshTokenSettings> refreshTokenSettings)
        {
            _authService = authService;
            _refreshTokenService = refreshTokenService;
            _refreshTokenSettings = refreshTokenSettings.Value;
        }

        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register([FromBody] RegisterDto registerDto)
        {
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

        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginDto loginDto)
        {
            var result = await _authService.LoginAsync(loginDto);

            // Trình duyệt đang giữ cookie của phiên trước (có thể của tài khoản khác) => thu hồi phiên đó
            // trước khi cấp phiên mới. Chỉ làm sau khi đăng nhập thành công.
            await _refreshTokenService.RevokeFamilyAsync(
                Request.Cookies[_refreshTokenSettings.CookieName],
                RefreshTokenRevokeReasons.Login);

            await IssueRefreshTokenCookieAsync(result.User.UserId);

            return Ok(new ApiResponse<AuthResponseDto>(
                true,
                "Đăng nhập thành công.",
                result
            ));
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
