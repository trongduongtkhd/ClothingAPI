using ClothingAPI.DTOs.Auth;
using ClothingAPI.DTOs.Users;

namespace ClothingAPI.Services.Interfaces
{
    public interface IUserProfileService
    {
        Task<CurrentUserDto> GetProfileAsync(int userId);
        Task<CurrentUserDto> UpdateProfileAsync(int userId, UpdateProfileDto dto);
        Task ChangePasswordAsync(int userId, ChangePasswordDto dto);
    }
}
