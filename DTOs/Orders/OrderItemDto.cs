namespace ClothingAPI.DTOs.Orders;

public class OrderItemDto
{
    public int OrderItemId { get; set; }

    public int VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string SKU { get; set; } = string.Empty;

    public string ColorName { get; set; } = string.Empty;

    public string SizeName { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal TotalPrice { get; set; }
}