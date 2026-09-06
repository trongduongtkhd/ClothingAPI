namespace ClothingAPI.Models;

public class Review
{
    public int ReviewId { get; set; }

    public int ProductId { get; set; }

    public int UserId { get; set; }

    public int? OrderItemId { get; set; }

    public byte Rating { get; set; }

    public string? Comment { get; set; }

    public bool IsApproved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;

    public User User { get; set; } = null!;

    public OrderItem? OrderItem { get; set; }
}