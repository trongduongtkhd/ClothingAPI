using ClothingAPI.DTOs.Sizes;

namespace ClothingAPI.Services.Interfaces
{
    public interface ISizeService
    {
        Task<List<SizeDto>> GetAllAsync();

        Task<SizeDto> GetByIdAsync(int sizeId);

        Task<SizeDto> CreateAsync(UpsertSizeDto dto);

        Task<SizeDto> UpdateAsync(int sizeId, UpsertSizeDto dto);

        Task DeleteAsync(int sizeId);
    }
}
