using ClothingAPI.DTOs.Users;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface IAdminUserService
    {
        Task<PagedResult<AdminUserDto>> GetAllAsync(AdminUserQueryDto query);
        Task<AdminUserDto> GetByIdAsync(int userId);
        Task UpdateStatusAsync(int currentAdminUserId, int targetUserId, bool isActive);

    }
}
