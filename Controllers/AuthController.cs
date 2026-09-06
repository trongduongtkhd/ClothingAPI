using ClothingAPI.DTOs.Auth;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ClothingAPI.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register([FromBody] RegisterDto registerDto)
        {
            var result = await _authService.RegisterAsync(registerDto);

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

            return Ok(new ApiResponse<AuthResponseDto>(
                true,
                "Đăng nhập thành công.",
                result
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
    }
}
