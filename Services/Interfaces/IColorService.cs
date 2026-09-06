using ClothingAPI.DTOs.Colors;

namespace ClothingAPI.Services.Interfaces
{
    public interface IColorService
    {
        Task<List<ColorDto>> GetAllAsync();

        Task<ColorDto> GetByIdAsync(int colorId);

        Task<ColorDto> CreateAsync(UpsertColorDto dto);

        Task<ColorDto> UpdateAsync(int colorId, UpsertColorDto dto);

        Task DeleteAsync(int colorId);
    }
}
