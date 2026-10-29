using ClothingAPI.Data;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/orders/{orderId:int}/vnpay")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminVnpayController(AppDbContext db, IVnpayService service) : ControllerBase
{
    private async Task<int> Owner(int orderId) => await db.Orders.Where(x => x.OrderId == orderId)
        .Select(x => (int?)x.UserId).SingleOrDefaultAsync() ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

    [HttpGet("status")]
    public async Task<object> Status(int orderId) => new ApiResponse<VnpayStatusDto>(true, "Đã lấy kết quả thanh toán.",
        await service.GetStatusAsync(await Owner(orderId), orderId));

    [HttpPost("reconcile")]
    public async Task<object> Reconcile(int orderId) => new ApiResponse<VnpayStatusDto>(true, "Đã kiểm tra kết quả thanh toán.",
        await service.ReconcileAsync(await Owner(orderId), orderId));
}
