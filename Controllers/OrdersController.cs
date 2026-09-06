using ClothingAPI.DTOs.Orders;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> Create([FromBody] CreateOrderDto dto)
    {
        var userId = User.GetUserId();

        var result = await _orderService.CreateAsync(userId, dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<OrderDetailDto>(
                true,
                "Đặt hàng thành công.",
                result
            ));
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderSummaryDto>>>> GetMyOrders([FromQuery] OrderQueryDto query)

    {
        var userId = User.GetUserId();

        var result = await _orderService.GetMyOrdersAsync(userId, query);

        return Ok(new ApiResponse<PagedResult<OrderSummaryDto>>(
            true,
            "Lấy danh sách đơn hàng thành công.",
            result
        ));
    }

    [HttpGet("{orderId:int}")]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> GetById(int orderId)
    {
        var userId = User.GetUserId();

        var result = await _orderService.GetMyOrderByIdAsync(userId, orderId);

        return Ok(new ApiResponse<OrderDetailDto>(
            true,
            "Lấy chi tiết đơn hàng thành công.",
            result
        ));
    }

    [HttpPut("{orderId:int}/cancel")]
    public async Task<ActionResult<ApiResponse<object>>> Cancel(int orderId, [FromBody] CancelOrderDto dto)
    {
        var userId = User.GetUserId();

        await _orderService.CancelAsync(userId, orderId, dto);

        return Ok(new ApiResponse<object>(
            true,
            "Hủy đơn hàng thành công."
        ));
    }

    [HttpGet("{orderId:int}/mock-qr")]
    public async Task<ActionResult<ApiResponse<MockQrDto>>> GetMockQr(int orderId)
    {
        var userId = User.GetUserId();

        var result = await _orderService.GetMockQrAsync(userId, orderId);

        return Ok(new ApiResponse<MockQrDto>(
            true,
            "Lấy thông tin QR thanh toán thành công.",
            result
        ));
    }

    [HttpPost("{orderId:int}/mock-qr/confirm")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> ConfirmMockQr(int orderId)
    {
        var userId = User.GetUserId();

        var result = await _orderService.ConfirmMockQrAsync(userId, orderId);

        return Ok(new ApiResponse<PaymentDto>(
            true,
            "Thanh toán QR mô phỏng thành công.",
            result
        ));
    }
}