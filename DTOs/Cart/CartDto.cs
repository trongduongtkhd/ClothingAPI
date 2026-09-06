namespace ClothingAPI.DTOs.Cart
{
    public class CartDto
    {
        public List<CartItemDto> Items { get; set; } = [];

        public decimal Subtotal { get; set; }

        public int TotalQuantity { get; set; }
    }
}
