using ClothingAPI.DTOs.Colors;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers
{
    [Route("api/admin/colors")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminColorsController : ControllerBase
    {
        private readonly IColorService _colorService;

        public AdminColorsController(IColorService colorService)
        {
            _colorService = colorService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<ColorDto>>>> GetAll()
        {
            var result = await _colorService.GetAllAsync();

            return Ok(new ApiResponse<List<ColorDto>>(
                true,
                "Lấy danh sách màu thành công.",
                result
            ));
        }

        [HttpGet("{colorId:int}")]
        public async Task<ActionResult<ApiResponse<ColorDto>>> GetById(int colorId)
        {
            var result = await _colorService.GetByIdAsync(colorId);

            return Ok(new ApiResponse<ColorDto>(
                true,
                "Lấy chi tiết màu thành công.",
                result
            ));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<ColorDto>>> Create([FromBody] UpsertColorDto dto)
        {
            var result = await _colorService.CreateAsync(dto);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<ColorDto>(
                    true,
                    "Tạo màu thành công.",
                    result
                ));
        }

        [HttpPut("{colorId:int}")]
        public async Task<ActionResult<ApiResponse<ColorDto>>> Update(int colorId, [FromBody] UpsertColorDto dto)
        {
            var result = await _colorService.UpdateAsync(colorId, dto);

            return Ok(new ApiResponse<ColorDto>(
                true,
                "Cập nhật màu thành công.",
                result
            ));
        }

        [HttpDelete("{colorId:int}")]
        public async Task<ActionResult<ApiResponse<object>>> Delete(int colorId)
        {
            await _colorService.DeleteAsync(colorId);

            return Ok(new ApiResponse<object>(
                true,
                "Xóa màu thành công."
            ));
        }
    }
}
