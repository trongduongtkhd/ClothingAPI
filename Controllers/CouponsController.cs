using ClothingAPI.DTOs.Coupons;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/coupons")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly ICouponService _couponService;

    public CouponsController(ICouponService couponService)
    {
        _couponService = couponService;
    }

    [HttpPost("validate")]
    public async Task<ActionResult<ApiResponse<CouponValidationDto>>>
        Validate([FromBody] ValidateCouponDto dto)
    {
        var userId = User.GetUserId();

        var result = await _couponService.ValidateForCartAsync(userId, dto);

        return Ok(new ApiResponse<CouponValidationDto>(
            true,
            "Áp dụng mã giảm giá thành công.",
            result
        ));
    }
}