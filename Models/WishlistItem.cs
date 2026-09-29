namespace ClothingAPI.Models
{
    public class WishlistItem
    {
        public int WishlistItemId { get; set; }

        public int UserId { get; set; }

        public int ProductId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;

        public Product Product { get; set; } = null!;
    }
}
