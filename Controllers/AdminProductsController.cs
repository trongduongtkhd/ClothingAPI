using ClothingAPI.DTOs.Products;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = "Admin")]
public class AdminProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public AdminProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductListDto>>>> GetAll([FromQuery] ProductQueryDto query)
    {
        var result = await _productService.GetAllAsync(query);

        return Ok(new ApiResponse<PagedResult<ProductListDto>>(
            true,
            "Lấy danh sách sản phẩm thành công.",
            result
        ));
    }

    [HttpGet("{productId:int}")]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> GetById(int productId)
    {
        var result = await _productService.GetByIdAsync(productId);

        return Ok(new ApiResponse<ProductDetailDto>(
            true,
            "Lấy chi tiết sản phẩm thành công.",
            result
        ));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> Create([FromBody] UpsertProductDto dto)
    {
        var result = await _productService.CreateAsync(dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<ProductDetailDto>(
                true,
                "Tạo sản phẩm thành công.",
                result
            ));
    }

    [HttpPut("{productId:int}")]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> Update(int productId, [FromBody] UpsertProductDto dto)
    {
        var result = await _productService.UpdateAsync(productId, dto);

        return Ok(new ApiResponse<ProductDetailDto>(
            true,
            "Cập nhật sản phẩm thành công.",
            result
        ));
    }

    [HttpDelete("{productId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int productId)
    {
        await _productService.DeleteAsync(productId);

        return Ok(new ApiResponse<object>(
            true,
            "Đã ẩn sản phẩm và các biến thể của sản phẩm."
        ));
    }

    [HttpPost("{productId:int}/variants")]
    public async Task<ActionResult<ApiResponse<ProductVariantDto>>> CreateVariant(int productId, [FromBody] UpsertProductVariantDto dto)
    {
        var result = await _productService.CreateVariantAsync(productId, dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<ProductVariantDto>(
                true,
                "Tạo biến thể sản phẩm thành công.",
                result
            ));
    }

    [HttpPut("{productId:int}/variants/{variantId:int}")]
    public async Task<ActionResult<ApiResponse<ProductVariantDto>>> UpdateVariant(int productId, int variantId, [FromBody] UpsertProductVariantDto dto)
    {
        var result = await _productService.UpdateVariantAsync(productId, variantId, dto);

        return Ok(new ApiResponse<ProductVariantDto>(
            true,
            "Cập nhật biến thể sản phẩm thành công.",
            result
        ));
    }

    [HttpDelete("{productId:int}/variants/{variantId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteVariant(int productId, int variantId)
    {
        await _productService.DeleteVariantAsync(productId, variantId);

        return Ok(new ApiResponse<object>(
            true,
            "Đã ẩn biến thể sản phẩm."
        ));
    }

    [HttpPost("{productId:int}/images")]
    public async Task<ActionResult<ApiResponse<ProductImageDto>>> CreateImage(int productId, [FromBody] UpsertProductImageDto dto)
    {
        var result = await _productService.CreateImageAsync(productId, dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<ProductImageDto>(
                true,
                "Thêm ảnh sản phẩm thành công.",
                result
            ));
    }

    [HttpPut("{productId:int}/images/{imageId:int}")]
    public async Task<ActionResult<ApiResponse<ProductImageDto>>> UpdateImage(int productId, int imageId, [FromBody] UpsertProductImageDto dto)

    {
        var result = await _productService.UpdateImageAsync(productId, imageId, dto);

        return Ok(new ApiResponse<ProductImageDto>(
            true,
            "Cập nhật ảnh sản phẩm thành công.",
            result
        ));
    }

    [HttpDelete("{productId:int}/images/{imageId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteImage(int productId, int imageId)
    {
        await _productService.DeleteImageAsync(productId, imageId);

        return Ok(new ApiResponse<object>(
            true,
            "Xóa ảnh sản phẩm thành công."
        ));
    }
}