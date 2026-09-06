using ClothingAPI.DTOs.Reviews;
using ClothingAPI.DTOs.Storefront;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IStorefrontService _storefrontService;
    private readonly IReviewService _reviewService;

    public ProductsController(IStorefrontService storefrontService, IReviewService reviewService)
    {
        _storefrontService = storefrontService;
        _reviewService = reviewService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PublicProductListDto>>>> GetAll([FromQuery] PublicProductQueryDto query)

    {
        var result = await _storefrontService.GetProductsAsync(query);

        return Ok(new ApiResponse<PagedResult<PublicProductListDto>>(
            true,
            "Lấy danh sách sản phẩm thành công.",
            result
        ));
    }

    [HttpGet("featured")]
    public async Task<ActionResult<ApiResponse<List<PublicProductListDto>>>> GetFeatured(
        [FromQuery] int limit = 8)
    {
        var result = await _storefrontService.GetFeaturedProductsAsync(limit);

        return Ok(new ApiResponse<List<PublicProductListDto>>(
            true,
            "Lấy sản phẩm nổi bật thành công.",
            result
        ));
    }

    [HttpGet("new-arrivals")]
    public async Task<ActionResult<ApiResponse<List<PublicProductListDto>>>> GetNewArrivals(
        [FromQuery] int limit = 8)
    {
        var result = await _storefrontService.GetNewArrivalProductsAsync(limit);

        return Ok(new ApiResponse<List<PublicProductListDto>>(
            true,
            "Lấy sản phẩm mới thành công.",
            result
        ));
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<ApiResponse<PublicProductDetailDto>>> GetBySlug(
        string slug)
    {
        var result = await _storefrontService.GetProductBySlugAsync(slug);

        return Ok(new ApiResponse<PublicProductDetailDto>(
            true,
            "Lấy chi tiết sản phẩm thành công.",
            result
        ));
    }

    [HttpGet("{productId:int}/related")]
    public async Task<ActionResult<ApiResponse<List<PublicProductListDto>>>> GetRelated(
        int productId,
        [FromQuery] int limit = 4)
    {
        var result = await _storefrontService.GetRelatedProductsAsync(
            productId,
            limit
        );

        return Ok(new ApiResponse<List<PublicProductListDto>>(
            true,
            "Lấy sản phẩm liên quan thành công.",
            result
        ));
    }

    [HttpGet("{productId:int}/reviews")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<PublicReviewDto>>>> GetReviews(int productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)

    {
        var result = await _reviewService.GetProductReviewsAsync(productId, page, pageSize);

        return Ok(new ApiResponse<PagedResult<PublicReviewDto>>(
            true,
            "Lấy danh sách đánh giá thành công.",
            result
        ));
    }



}