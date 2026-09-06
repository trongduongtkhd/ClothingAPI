using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders;

public class OrderStatusHistoryDto
{
    public int OrderStatusHistoryId { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    public string? ChangedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}