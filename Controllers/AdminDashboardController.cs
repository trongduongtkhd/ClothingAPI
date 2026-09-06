using ClothingAPI.DTOs.Dashboard;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = "Admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _adminDashboardService;

    public AdminDashboardController(IAdminDashboardService adminDashboardService)
    {
        _adminDashboardService = adminDashboardService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> Get([FromQuery] DashboardQueryDto query)

    {
        var result = await _adminDashboardService.GetAsync(query);

        return Ok(new ApiResponse<DashboardDto>(
            true,
            "Lấy dữ liệu dashboard thành công.",
            result
        ));
    }
}