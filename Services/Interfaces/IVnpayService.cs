using ClothingAPI.DTOs.Orders;

namespace ClothingAPI.Services.Interfaces;

public interface IVnpayService
{
    Task<VnpayPaymentUrlDto> CreatePaymentUrlAsync(int userId, int orderId, string ipAddress, string? frontendOrigin = null);
    Task<VnpayStatusDto> GetStatusAsync(int userId, int orderId);
    Task<VnpayStatusDto> ReconcileAsync(int userId, int orderId);
    Task<VnpayIpnResult> HandleIpnAsync(IReadOnlyDictionary<string, string> fields);
    Task<string> HandleReturnAsync(IReadOnlyDictionary<string, string> fields);
    Task ProcessExpiredOrdersAsync();
}
