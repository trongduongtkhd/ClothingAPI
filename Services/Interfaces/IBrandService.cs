using ClothingAPI.DTOs.Brands;

namespace ClothingAPI.Services.Interfaces
{
    public interface IBrandService
    {
        Task<List<BrandDto>> GetAllAsync();

        Task<BrandDto> GetByIdAsync(int brandId);

        Task<BrandDto> CreateAsync(UpsertBrandDto dto);

        Task<BrandDto> UpdateAsync(int brandId, UpsertBrandDto dto);

        Task DeleteAsync(int brandId);
    }
}
