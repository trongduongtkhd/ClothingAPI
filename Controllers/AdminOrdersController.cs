using ClothingAPI.DTOs.Orders;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrdersController : ControllerBase
{
    private readonly IAdminOrderService _adminOrderService;

    public AdminOrdersController(IAdminOrderService adminOrderService)
    {
        _adminOrderService = adminOrderService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminOrderSummaryDto>>>> GetAll([FromQuery] AdminOrderQueryDto query)

    {
        var result = await _adminOrderService.GetAllAsync(query);

        return Ok(new ApiResponse<PagedResult<AdminOrderSummaryDto>>(
            true,
            "Lấy danh sách đơn hàng thành công.",
            result
        ));
    }

    [HttpGet("{orderId:int}")]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> GetById(int orderId)
    {
        var result = await _adminOrderService.GetByIdAsync(orderId);

        return Ok(new ApiResponse<OrderDetailDto>(
            true,
            "Lấy chi tiết đơn hàng thành công.",
            result
        ));
    }

    [HttpPut("{orderId:int}/status")]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> UpdateStatus(int orderId, [FromBody] UpdateOrderStatusDto dto)

    {
        var adminUserId = User.GetUserId();

        var result = await _adminOrderService.UpdateOrderStatusAsync(adminUserId, orderId, dto);

        return Ok(new ApiResponse<OrderDetailDto>(
            true,
            "Cập nhật trạng thái đơn hàng thành công.",
            result
        ));
    }

    [HttpPut("{orderId:int}/payment-status")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> UpdatePaymentStatus(int orderId, [FromBody] UpdatePaymentStatusDto dto)

    {
        var adminUserId = User.GetUserId();

        var result = await _adminOrderService.UpdatePaymentStatusAsync(adminUserId, orderId, dto);

        return Ok(new ApiResponse<PaymentDto>(
            true,
            "Cập nhật trạng thái thanh toán thành công.",
            result
        ));
    }
}