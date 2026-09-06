using ClothingAPI.DTOs.Cart;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<CartDto>>> GetMyCart()
    {
        var userId = User.GetUserId();

        var result = await _cartService.GetMyCartAsync(userId);

        return Ok(new ApiResponse<CartDto>(
            true,
            "Lấy giỏ hàng thành công.",
            result
        ));
    }

    [HttpPost("items")]
    public async Task<ActionResult<ApiResponse<CartDto>>> AddItem([FromBody] AddCartItemDto dto)
    {
        var userId = User.GetUserId();

        var result = await _cartService.AddItemAsync(userId, dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<CartDto>(
                true,
                "Thêm sản phẩm vào giỏ hàng thành công.",
                result
            ));
    }

    [HttpPut("items/{cartItemId:int}")]
    public async Task<ActionResult<ApiResponse<CartDto>>> UpdateItem(int cartItemId, [FromBody] UpdateCartItemDto dto)
    {
        var userId = User.GetUserId();

        var result = await _cartService.UpdateItemAsync(userId, cartItemId, dto);

        return Ok(new ApiResponse<CartDto>(
            true,
            "Cập nhật giỏ hàng thành công.",
            result
        ));
    }

    [HttpDelete("items/{cartItemId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteItem(int cartItemId)
    {
        var userId = User.GetUserId();

        await _cartService.DeleteItemAsync(userId, cartItemId);

        return Ok(new ApiResponse<object>(
            true,
            "Xóa sản phẩm khỏi giỏ hàng thành công."
        ));
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse<object>>> ClearCart()
    {
        var userId = User.GetUserId();

        await _cartService.ClearCartAsync(userId);

        return Ok(new ApiResponse<object>(
            true,
            "Đã xóa toàn bộ giỏ hàng."
        ));
    }
}