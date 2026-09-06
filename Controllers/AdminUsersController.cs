using ClothingAPI.DTOs.Users;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _adminUserService;

    public AdminUsersController(IAdminUserService adminUserService)
    {
        _adminUserService = adminUserService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminUserDto>>>> GetAll([FromQuery] AdminUserQueryDto query)

    {
        var result = await _adminUserService.GetAllAsync(query);

        return Ok(new ApiResponse<PagedResult<AdminUserDto>>(
            true,
            "Lấy danh sách người dùng thành công.",
            result
        ));
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> GetById(int userId)
    {
        var result = await _adminUserService.GetByIdAsync(userId);

        return Ok(new ApiResponse<AdminUserDto>(
            true,
            "Lấy chi tiết người dùng thành công.",
            result
        ));
    }

    [HttpPut("{userId:int}/status")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateStatus(int userId, [FromBody] UpdateUserStatusDto dto)

    {
        var currentAdminUserId = User.GetUserId();

        await _adminUserService.UpdateStatusAsync(currentAdminUserId, userId, dto.IsActive);

        return Ok(new ApiResponse<object>(
            true,
            dto.IsActive ? "Mở khóa tài khoản thành công." : "Khóa tài khoản thành công."
        ));
    }
}