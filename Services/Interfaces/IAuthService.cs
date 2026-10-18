using ClothingAPI.DTOs.Auth;

namespace ClothingAPI.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto);
        Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
        Task<CurrentUserDto> GetCurrentUserAsync(int userId);
        // Cấp access token mới cho user đã được xác thực bằng refresh token (dùng ở /api/auth/refresh).
        Task<AuthResponseDto> CreateAuthResponseForUserAsync(int userId);
        Task ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto);
        Task ResetPasswordAsync(ResetPasswordDto resetPasswordDto);
    }
}
