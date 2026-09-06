using ClothingAPI.DTOs.Storefront;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly IStorefrontService _storefrontService;
    public CategoriesController(IStorefrontService storefrontService)
    {
        _storefrontService = storefrontService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<PublicCategoryDto>>>> GetAll()
    {
        var result = await _storefrontService.GetCategoriesAsync();

        return Ok(new ApiResponse<List<PublicCategoryDto>>(
            true,
            "Lấy danh sách danh mục thành công.",
            result
        ));
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<ApiResponse<PublicCategoryDto>>> GetBySlug(
       string slug)
    {
        var result = await _storefrontService.GetCategoryBySlugAsync(slug);

        return Ok(new ApiResponse<PublicCategoryDto>(
            true,
            "Lấy chi tiết danh mục thành công.",
            result
        ));
    }
}