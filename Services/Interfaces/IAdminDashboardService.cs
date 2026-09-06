using ClothingAPI.DTOs.Dashboard;

namespace ClothingAPI.Services.Interfaces
{
    public interface IAdminDashboardService
    {
        Task<DashboardDto> GetAsync(DashboardQueryDto query);
    }
}
