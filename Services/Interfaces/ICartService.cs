using ClothingAPI.DTOs.Cart;

namespace ClothingAPI.Services.Interfaces
{
    public interface ICartService
    {

        Task<CartDto> GetMyCartAsync(int userId);
        Task<CartDto> AddItemAsync(int userId, AddCartItemDto dto);
        Task<CartDto> UpdateItemAsync(int userId, int cartItemId, UpdateCartItemDto dto);
        Task DeleteItemAsync(int userId, int cartItemId);
        Task ClearCartAsync(int userId);
    }
}
