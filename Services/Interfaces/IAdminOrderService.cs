using ClothingAPI.DTOs.Orders;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface IAdminOrderService
    {
        Task<PagedResult<AdminOrderSummaryDto>> GetAllAsync(AdminOrderQueryDto query);

        Task<OrderDetailDto> GetByIdAsync(int orderId);

        Task<OrderDetailDto> UpdateOrderStatusAsync(int adminUserId, int orderId, UpdateOrderStatusDto dto);

        Task<PaymentDto> UpdatePaymentStatusAsync(int adminUserId, int orderId, UpdatePaymentStatusDto dto);

    }
}
