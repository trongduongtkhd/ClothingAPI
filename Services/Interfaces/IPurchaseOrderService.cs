using ClothingAPI.DTOs.PurchaseOrders;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface IPurchaseOrderService
    {
        Task<PagedResult<PurchaseOrderListDto>> GetAllAsync(int? supplierId, int page, int pageSize);
        Task<PurchaseOrderDetailDto> GetByIdAsync(int purchaseOrderId);
        Task<PurchaseOrderDetailDto> CreateAsync(int createdByUserId, CreatePurchaseOrderDto dto);
        Task<PurchaseOrderDetailDto> UpdateAsync(int purchaseOrderId, CreatePurchaseOrderDto dto);
        Task<PurchaseOrderDetailDto> CancelAsync(int purchaseOrderId, CancelPurchaseOrderDto dto);
    }
}
