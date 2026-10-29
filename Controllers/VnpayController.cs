using ClothingAPI.DTOs.Orders;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/payments/vnpay")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class VnpayController(IVnpayService service, IOptions<VnpaySettings> settings, ILogger<VnpayController> logger) : ControllerBase
{
    [HttpGet("configuration")]
    [AllowAnonymous]
    public object Configuration() => new { enabled = settings.Value.IsConfigured };

    [HttpPost("/api/orders/{orderId:int}/vnpay/payment-url")]
    [Authorize]
    public async Task<object> Create(int orderId)
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        var result = await service.CreatePaymentUrlAsync(User.GetUserId(), orderId, ip?.MapToIPv4().ToString() ?? "127.0.0.1",
            Request.Headers.Origin.ToString());
        return new ApiResponse<VnpayPaymentUrlDto>(true, "Đã tạo liên kết thanh toán VNPAY.", result);
    }

    [HttpGet("/api/orders/{orderId:int}/vnpay/status")]
    [Authorize]
    public async Task<object> Status(int orderId) => new ApiResponse<VnpayStatusDto>(true, "Đã lấy kết quả thanh toán.",
        await service.GetStatusAsync(User.GetUserId(), orderId));

    [HttpPost("/api/orders/{orderId:int}/vnpay/reconcile")]
    [Authorize]
    public async Task<object> Reconcile(int orderId) => new ApiResponse<VnpayStatusDto>(true, "Đã kiểm tra kết quả thanh toán.",
        await service.ReconcileAsync(User.GetUserId(), orderId));

    [HttpGet("ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> Ipn()
    {
        var fields = ReadFields();
        if (fields is null) return Ok(new VnpayIpnResult("97", "Invalid parameters"));
        try { return Ok(await service.HandleIpnAsync(fields)); }
        catch (Exception ex)
        {
            logger.LogError(ex, "VNPAY IPN processing failed");
            return Ok(new VnpayIpnResult("99", "Please retry"));
        }
    }

    [HttpGet("return")]
    [AllowAnonymous]
    public async Task<IActionResult> Return()
    {
        var fields = ReadFields();
        var redirect = fields is null ? settings.Value.ResultUrl + "?invalid=true" : await service.HandleReturnAsync(fields);
        return Redirect(redirect);
    }

    private Dictionary<string, string>? ReadFields()
    {
        if (Request.Query.Any(x => x.Value.Count != 1 || x.Value.ToString().Length > 2048)) return null;
        return Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.Ordinal);
    }
}
