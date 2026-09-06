using ClothingAPI.DTOs.Categories;

namespace ClothingAPI.Services.Interfaces
{
    public interface ICategoryService
    {
        Task<List<CategoryDto>> GetAllAsync();

        Task<CategoryDto> GetByIdAsync(int categoryId);

        Task<CategoryDto> CreateAsync(UpsertCategoryDto dto);

        Task<CategoryDto> UpdateAsync(int categoryId, UpsertCategoryDto dto);

        Task DeleteAsync(int categoryId);
    }
}
