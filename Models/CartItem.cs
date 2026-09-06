namespace ClothingAPI.Models
{
    public class CartItem
    {
        public int CartItemId { get; set; }

        public int UserId { get; set; }

        public int VariantId { get; set; }

        public int Quantity { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public User User { get; set; } = null!;

        public ProductVariant Variant { get; set; } = null!;
    }
}
