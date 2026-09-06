namespace ClothingAPI.DTOs.Dashboard;

public class BestSellingProductDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int TotalQuantitySold { get; set; }

    public decimal TotalRevenue { get; set; }
}