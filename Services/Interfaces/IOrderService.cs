using ClothingAPI.DTOs.Orders;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface IOrderService
    {
        Task<OrderDetailDto> CreateAsync(int userId, CreateOrderDto dto);
        Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(int userId, OrderQueryDto query);
        Task<OrderDetailDto> GetMyOrderByIdAsync(int userId, int orderId);
        Task CancelAsync(int userId, int orderId, CancelOrderDto dto);
        Task<MockQrDto> GetMockQrAsync(int userId, int orderId);

        Task<PaymentDto> ConfirmMockQrAsync(int userId, int orderId);
    }
}
