using ClothingAPI.DTOs.Auth;

namespace ClothingAPI.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto);
        Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
        Task<CurrentUserDto> GetCurrentUserAsync(int userId);
    }
}
