using ClothingAPI.DTOs.Sizes;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers
{
    [Route("api/admin/sizes")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminSizesController : ControllerBase
    {
        private readonly ISizeService _sizeService;

        public AdminSizesController(ISizeService sizeService)
        {
            _sizeService = sizeService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<SizeDto>>>> GetAll()
        {
            var result = await _sizeService.GetAllAsync();

            return Ok(new ApiResponse<List<SizeDto>>(
                true,
                "Lấy danh sách size thành công.",
                result
            ));
        }

        [HttpGet("{sizeId:int}")]
        public async Task<ActionResult<ApiResponse<SizeDto>>> GetById(int sizeId)
        {
            var result = await _sizeService.GetByIdAsync(sizeId);

            return Ok(new ApiResponse<SizeDto>(
                true,
                "Lấy chi tiết size thành công.",
                result
            ));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<SizeDto>>> Create([FromBody] UpsertSizeDto dto)
        {
            var result = await _sizeService.CreateAsync(dto);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<SizeDto>(
                    true,
                    "Tạo size thành công.",
                    result
                ));
        }

        [HttpPut("{sizeId:int}")]
        public async Task<ActionResult<ApiResponse<SizeDto>>> Update(int sizeId, [FromBody] UpsertSizeDto dto)
        {
            var result = await _sizeService.UpdateAsync(sizeId, dto);

            return Ok(new ApiResponse<SizeDto>(
                true,
                "Cập nhật size thành công.",
                result
            ));
        }

        [HttpDelete("{sizeId:int}")]
        public async Task<ActionResult<ApiResponse<object>>> Delete(int sizeId)
        {
            await _sizeService.DeleteAsync(sizeId);

            return Ok(new ApiResponse<object>(
                true,
                "Xóa size thành công."
            ));
        }
    }
}
