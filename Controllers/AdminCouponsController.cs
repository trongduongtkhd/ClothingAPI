using ClothingAPI.DTOs.Coupons;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/admin/coupons")]
[Authorize(Roles = "Admin")]
public class AdminCouponsController : ControllerBase
{
    private readonly ICouponService _couponService;

    public AdminCouponsController(ICouponService couponService)
    {
        _couponService = couponService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CouponDto>>>> GetAll()
    {
        var result = await _couponService.GetAllAsync();

        return Ok(new ApiResponse<List<CouponDto>>(
            true,
            "Lấy danh sách mã giảm giá thành công.",
            result
        ));
    }

    [HttpGet("{couponId:int}")]
    public async Task<ActionResult<ApiResponse<CouponDto>>> GetById(int couponId)
    {
        var result = await _couponService.GetByIdAsync(couponId);

        return Ok(new ApiResponse<CouponDto>(
            true,
            "Lấy chi tiết mã giảm giá thành công.",
            result
        ));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CouponDto>>> Create([FromBody] UpsertCouponDto dto)

    {
        var adminUserId = User.GetUserId();

        var result = await _couponService.CreateAsync(adminUserId, dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<CouponDto>(
                true,
                "Tạo mã giảm giá thành công.",
                result
            ));
    }

    [HttpPut("{couponId:int}")]
    public async Task<ActionResult<ApiResponse<CouponDto>>> Update(int couponId, [FromBody] UpsertCouponDto dto)
    {
        var result = await _couponService.UpdateAsync(couponId, dto);

        return Ok(new ApiResponse<CouponDto>(
            true,
            "Cập nhật mã giảm giá thành công.",
            result
        ));
    }

    [HttpPut("{couponId:int}/status")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateStatus(int couponId, [FromBody] UpdateCouponStatusDto dto)

    {
        await _couponService.UpdateStatusAsync(couponId, dto.IsActive);

        return Ok(new ApiResponse<object>(
            true,
            "Cập nhật trạng thái mã giảm giá thành công."
        ));
    }

    [HttpGet("{couponId:int}/usages")]
    public async Task<ActionResult<ApiResponse<PagedResult<CouponUsageDto>>>> GetUsages(int couponId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _couponService.GetUsagesAsync(couponId, page, pageSize);

        return Ok(new ApiResponse<PagedResult<CouponUsageDto>>(
            true,
            "Lấy lịch sử sử dụng mã giảm giá thành công.",
            result
        ));
    }
}