using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Cart
{
    public class AddCartItemDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Biến thể sản phẩm không hợp lệ.")]
        public int VariantId { get; set; }

        [Range(1, 100, ErrorMessage = "Số lượng phải từ 1 đến 100.")]
        public int Quantity { get; set; }
    }
}
