using ClothingAPI.DTOs.Sizes;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/sizes")]
public class SizesController : ControllerBase
{
    private readonly IStorefrontService _storefrontService;

    public SizesController(IStorefrontService storefrontService)
    {
        _storefrontService = storefrontService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SizeDto>>>> GetAll()
    {
        var result = await _storefrontService.GetSizesAsync();

        return Ok(new ApiResponse<List<SizeDto>>(
            true,
            "Lấy danh sách size thành công.",
            result
        ));
    }
}