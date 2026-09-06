namespace ClothingAPI.Models;

public class OrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public int VariantId { get; set; }
    // Snapshot tại thời điểm đặt hàng.
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string ColorName { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
    public Order Order { get; set; } = null!;
    public ProductVariant Variant { get; set; } = null!;
    public ICollection<Review> Reviews { get; set; } = new List<Review>();

}