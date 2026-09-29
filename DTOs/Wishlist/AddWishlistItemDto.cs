using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Wishlist
{
    public class AddWishlistItemDto
    {
        [Required(ErrorMessage = "ProductId là bắt buộc.")]
        public int ProductId { get; set; }
    }
}
