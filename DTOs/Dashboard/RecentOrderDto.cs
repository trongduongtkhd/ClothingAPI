using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Dashboard;

public class RecentOrderDto
{
    public int OrderId { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public OrderStatus OrderStatus { get; set; }

    public DateTime CreatedAt { get; set; }
}