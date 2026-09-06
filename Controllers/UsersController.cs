using ClothingAPI.DTOs.Auth;
using ClothingAPI.DTOs.Users;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserProfileService _userProfileService;

    public UsersController(IUserProfileService userProfileService)
    {
        _userProfileService = userProfileService;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<CurrentUserDto>>> GetProfile()
    {
        var userId = User.GetUserId();

        var result = await _userProfileService.GetProfileAsync(userId);

        return Ok(new ApiResponse<CurrentUserDto>(
            true,
            "Lấy hồ sơ thành công.",
            result
        ));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<CurrentUserDto>>> UpdateProfile(
        [FromBody] UpdateProfileDto dto)
    {
        var userId = User.GetUserId();

        var result = await _userProfileService.UpdateProfileAsync(userId, dto);

        return Ok(new ApiResponse<CurrentUserDto>(
            true,
            "Cập nhật hồ sơ thành công.",
            result
        ));
    }

    [HttpPut("change-password")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(
        [FromBody] ChangePasswordDto dto)
    {
        var userId = User.GetUserId();

        await _userProfileService.ChangePasswordAsync(userId, dto);

        return Ok(new ApiResponse<object>(
            true,
            "Đổi mật khẩu thành công."
        ));
    }
}