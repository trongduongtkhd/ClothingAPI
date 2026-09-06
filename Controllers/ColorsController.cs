using ClothingAPI.DTOs.Colors;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/colors")]
public class ColorsController : ControllerBase
{
    private readonly IStorefrontService _storefrontService;

    public ColorsController(IStorefrontService storefrontService)
    {
        _storefrontService = storefrontService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ColorDto>>>> GetAll()
    {
        var result = await _storefrontService.GetColorsAsync();

        return Ok(new ApiResponse<List<ColorDto>>(
            true,
            "Lấy danh sách màu sắc thành công.",
            result
        ));
    }
}