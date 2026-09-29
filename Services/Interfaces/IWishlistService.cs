using ClothingAPI.DTOs.Wishlist;

namespace ClothingAPI.Services.Interfaces
{
    public interface IWishlistService
    {
        Task<List<WishlistItemDto>> GetMyWishlistAsync(int userId);
        Task<WishlistItemDto> AddItemAsync(int userId, int productId);
        Task RemoveItemAsync(int userId, int productId);
    }
}
