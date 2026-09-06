using ClothingAPI.DTOs.Storefront;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/brands")]
public class BrandsController : ControllerBase
{
    private readonly IStorefrontService _storefrontService;

    public BrandsController(IStorefrontService storefrontService)
    {
        _storefrontService = storefrontService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<PublicBrandDto>>>> GetAll()
    {
        var result = await _storefrontService.GetBrandsAsync();

        return Ok(new ApiResponse<List<PublicBrandDto>>(
            true,
            "Lấy danh sách thương hiệu thành công.",
            result
        ));
    }
}