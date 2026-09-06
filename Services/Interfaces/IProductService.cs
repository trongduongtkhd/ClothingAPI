using ClothingAPI.DTOs.Products;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface IProductService
    {
        Task<PagedResult<ProductListDto>> GetAllAsync(ProductQueryDto query);
        Task<ProductDetailDto> GetByIdAsync(int productId);
        Task<ProductDetailDto> CreateAsync(UpsertProductDto dto);
        Task<ProductDetailDto> UpdateAsync(int productId, UpsertProductDto dto);
        Task DeleteAsync(int productId);
        Task<ProductVariantDto> CreateVariantAsync(int productId, UpsertProductVariantDto dto);
        Task<ProductVariantDto> UpdateVariantAsync(int productId, int variantId, UpsertProductVariantDto dto);
        Task DeleteVariantAsync(int productId, int variantId);

        Task<ProductImageDto> CreateImageAsync(int productId, UpsertProductImageDto dto);
        Task<ProductImageDto> UpdateImageAsync(int productId, int imageId, UpsertProductImageDto dto);
        Task DeleteImageAsync(int productId, int imageId);
    }
}
