using ClothingAPI.DTOs.Categories;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers
{
    [Route("api/admin/categories")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminCategoriesController : ControllerBase
    {
        private readonly ICategoryService _categoryService;

        public AdminCategoriesController(ICategoryService categoryService)
        {
            _categoryService = categoryService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<CategoryDto>>>> GetAll()
        {
            var result = await _categoryService.GetAllAsync();

            return Ok(new ApiResponse<List<CategoryDto>>(
                true,
                "Lấy danh sách danh mục thành công.",
                result
            ));
        }

        [HttpGet("{categoryId:int}")]
        public async Task<ActionResult<ApiResponse<CategoryDto>>> GetById(int categoryId)
        {
            var result = await _categoryService.GetByIdAsync(categoryId);

            return Ok(new ApiResponse<CategoryDto>(
                true,
                "Lấy chi tiết danh mục thành công.",
                result
            ));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<CategoryDto>>> Create([FromBody] UpsertCategoryDto dto)
        {
            var result = await _categoryService.CreateAsync(dto);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<CategoryDto>(
                    true,
                    "Tạo danh mục thành công.",
                    result
                ));
        }

        [HttpPut("{categoryId:int}")]
        public async Task<ActionResult<ApiResponse<CategoryDto>>> Update(int categoryId, [FromBody] UpsertCategoryDto dto)
        {
            var result = await _categoryService.UpdateAsync(categoryId, dto);

            return Ok(new ApiResponse<CategoryDto>(
                true,
                "Cập nhật danh mục thành công.",
                result
            ));
        }

        [HttpDelete("{categoryId:int}")]
        public async Task<ActionResult<ApiResponse<object>>> Delete(int categoryId)
        {
            await _categoryService.DeleteAsync(categoryId);

            return Ok(new ApiResponse<object>(
                true,
                "Đã ẩn danh mục thành công."
            ));
        }
    }
}
