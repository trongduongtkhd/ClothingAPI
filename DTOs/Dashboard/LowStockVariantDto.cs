namespace ClothingAPI.DTOs.Dashboard;

public class LowStockVariantDto
{
    public int VariantId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string SKU { get; set; } = string.Empty;

    public string ColorName { get; set; } = string.Empty;

    public string SizeName { get; set; } = string.Empty;

    public int StockQuantity { get; set; }
}