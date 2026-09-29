using ClothingAPI.DTOs.Suppliers;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers
{
    [Route("api/admin/suppliers")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminSuppliersController : ControllerBase
    {
        private readonly ISupplierService _supplierService;

        public AdminSuppliersController(ISupplierService supplierService)
        {
            _supplierService = supplierService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<SupplierDto>>>> GetAll()
        {
            var result = await _supplierService.GetAllAsync();

            return Ok(new ApiResponse<List<SupplierDto>>(
                true,
                "Lấy danh sách nhà cung ứng thành công.",
                result
            ));
        }

        [HttpGet("{supplierId:int}")]
        public async Task<ActionResult<ApiResponse<SupplierDto>>> GetById(int supplierId)
        {
            var result = await _supplierService.GetByIdAsync(supplierId);

            return Ok(new ApiResponse<SupplierDto>(
                true,
                "Lấy chi tiết nhà cung ứng thành công.",
                result
            ));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<SupplierDto>>> Create(
            [FromBody] UpsertSupplierDto dto)
        {
            var result = await _supplierService.CreateAsync(dto);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<SupplierDto>(
                    true,
                    "Tạo nhà cung ứng thành công.",
                    result
                ));
        }

        [HttpPut("{supplierId:int}")]
        public async Task<ActionResult<ApiResponse<SupplierDto>>> Update(
            int supplierId,
            [FromBody] UpsertSupplierDto dto)
        {
            var result = await _supplierService.UpdateAsync(supplierId, dto);

            return Ok(new ApiResponse<SupplierDto>(
                true,
                "Cập nhật nhà cung ứng thành công.",
                result
            ));
        }

        [HttpDelete("{supplierId:int}")]
        public async Task<ActionResult<ApiResponse<object>>> Delete(int supplierId)
        {
            await _supplierService.DeleteAsync(supplierId);

            return Ok(new ApiResponse<object>(
                true,
                "Đã ẩn nhà cung ứng thành công."
            ));
        }
    }
}
