using ClothingAPI.Enums;

namespace ClothingAPI.Models;

public class OrderStatusHistory
{
    public int OrderStatusHistoryId { get; set; }
    public int OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    public int? ChangedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Order Order { get; set; } = null!;
    public User? ChangedByUser { get; set; }
}