using ClothingAPI.DTOs.Suppliers;

namespace ClothingAPI.Services.Interfaces
{
    public interface ISupplierService
    {
        Task<List<SupplierDto>> GetAllAsync();
        Task<SupplierDto> GetByIdAsync(int supplierId);
        Task<SupplierDto> CreateAsync(UpsertSupplierDto dto);
        Task<SupplierDto> UpdateAsync(int supplierId, UpsertSupplierDto dto);
        Task DeleteAsync(int supplierId);
    }
}
