using ClothingAPI.DTOs.Wishlist;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/wishlist")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlistService;

    public WishlistController(IWishlistService wishlistService)
    {
        _wishlistService = wishlistService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<WishlistItemDto>>>> GetMyWishlist()
    {
        var userId = User.GetUserId();

        var result = await _wishlistService.GetMyWishlistAsync(userId);

        return Ok(new ApiResponse<List<WishlistItemDto>>(
            true,
            "Lấy danh sách yêu thích thành công.",
            result
        ));
    }

    [HttpPost("items")]
    public async Task<ActionResult<ApiResponse<WishlistItemDto>>> AddItem([FromBody] AddWishlistItemDto dto)
    {
        var userId = User.GetUserId();

        var result = await _wishlistService.AddItemAsync(userId, dto.ProductId);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<WishlistItemDto>(
                true,
                "Đã thêm vào danh sách yêu thích.",
                result
            ));
    }

    [HttpDelete("items/{productId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> RemoveItem(int productId)
    {
        var userId = User.GetUserId();

        await _wishlistService.RemoveItemAsync(userId, productId);

        return Ok(new ApiResponse<object>(
            true,
            "Đã xóa khỏi danh sách yêu thích."
        ));
    }
}
