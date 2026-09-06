using ClothingAPI.DTOs.Brands;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers
{
    [Route("api/admin/brands")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminBrandsController : ControllerBase
    {
        private readonly IBrandService _brandService;

        public AdminBrandsController(IBrandService brandService)
        {
            _brandService = brandService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<BrandDto>>>> GetAll()
        {
            var result = await _brandService.GetAllAsync();

            return Ok(new ApiResponse<List<BrandDto>>(
                true,
                "Lấy danh sách thương hiệu thành công.",
                result
            ));
        }

        [HttpGet("{brandId:int}")]
        public async Task<ActionResult<ApiResponse<BrandDto>>> GetById(int brandId)
        {
            var result = await _brandService.GetByIdAsync(brandId);

            return Ok(new ApiResponse<BrandDto>(
                true,
                "Lấy chi tiết thương hiệu thành công.",
                result
            ));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<BrandDto>>> Create(
            [FromBody] UpsertBrandDto dto)
        {
            var result = await _brandService.CreateAsync(dto);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<BrandDto>(
                    true,
                    "Tạo thương hiệu thành công.",
                    result
                ));
        }

        [HttpPut("{brandId:int}")]
        public async Task<ActionResult<ApiResponse<BrandDto>>> Update(
            int brandId,
            [FromBody] UpsertBrandDto dto)
        {
            var result = await _brandService.UpdateAsync(brandId, dto);

            return Ok(new ApiResponse<BrandDto>(
                true,
                "Cập nhật thương hiệu thành công.",
                result
            ));
        }

        [HttpDelete("{brandId:int}")]
        public async Task<ActionResult<ApiResponse<object>>> Delete(int brandId)
        {
            await _brandService.DeleteAsync(brandId);

            return Ok(new ApiResponse<object>(
                true,
                "Đã ẩn thương hiệu thành công."
            ));
        }
    }
}
