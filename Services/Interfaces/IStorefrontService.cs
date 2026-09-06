using ClothingAPI.DTOs.Colors;
using ClothingAPI.DTOs.Sizes;
using ClothingAPI.DTOs.Storefront;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface IStorefrontService
    {
        Task<List<PublicCategoryDto>> GetCategoriesAsync();
        Task<List<PublicBrandDto>> GetBrandsAsync();
        Task<PagedResult<PublicProductListDto>> GetProductsAsync(PublicProductQueryDto query);
        Task<List<PublicProductListDto>> GetFeaturedProductsAsync(int limit);

        Task<List<PublicProductListDto>> GetNewArrivalProductsAsync(int limit);

        Task<PublicProductDetailDto> GetProductBySlugAsync(string slug);

        Task<List<PublicProductListDto>> GetRelatedProductsAsync(int productId, int limit);

        Task<PublicCategoryDto> GetCategoryBySlugAsync(string slug);

        Task<List<ColorDto>> GetColorsAsync();

        Task<List<SizeDto>> GetSizesAsync();



    }
}
