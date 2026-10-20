using ClothingAPI.DTOs.Users;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

/// Quản lý 2FA của người dùng đang đăng nhập. Sai mã/mật khẩu trả 400 (không phải 401) để 401 ở đây
/// chỉ còn một nghĩa: access token hết hạn => frontend refresh bình thường.
/// Trạng thái bật/tắt nằm trong twoFactorEnabled của /api/auth/me và /api/users/profile.
[ApiController]
[Route("api/users/two-factor")]
[Authorize]
public class TwoFactorController : ControllerBase
{
    private readonly ITwoFactorService _twoFactorService;

    public TwoFactorController(ITwoFactorService twoFactorService)
    {
        _twoFactorService = twoFactorService;
    }

    // Phản hồi chứa secret => cấm cache. Chỉ dùng được khi 2FA CHƯA bật.
    [HttpPost("setup")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApiResponse<TwoFactorSetupDto>>> Setup()
    {
        var result = await _twoFactorService.GetSetupAsync(User.GetUserId());

        return Ok(new ApiResponse<TwoFactorSetupDto>(
            true,
            "Quét mã QR bằng ứng dụng xác thực rồi nhập mã 6 số để xác nhận.",
            result
        ));
    }

    // Phản hồi chứa mã khôi phục (chỉ hiện một lần) => cấm cache.
    [HttpPost("enable")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApiResponse<EnableTwoFactorResultDto>>> Enable([FromBody] EnableTwoFactorDto dto)
    {
        var result = await _twoFactorService.EnableAsync(User.GetUserId(), dto.Code, GetClientIp());

        return Ok(new ApiResponse<EnableTwoFactorResultDto>(
            true,
            "Đã bật xác thực hai lớp. Hãy lưu các mã khôi phục, chúng chỉ hiển thị một lần. Mọi phiên đăng nhập cũ đã bị đăng xuất.",
            result
        ));
    }

    [HttpPost("disable")]
    public async Task<ActionResult<ApiResponse<object>>> Disable([FromBody] DisableTwoFactorDto dto)
    {
        await _twoFactorService.DisableAsync(User.GetUserId(), dto, GetClientIp());

        return Ok(new ApiResponse<object>(
            true,
            "Đã tắt xác thực hai lớp."
        ));
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
