namespace ClothingAPI.DTOs.Cart
{
    public class CartItemDto
    {
        public int CartItemId { get; set; }

        public int VariantId { get; set; }

        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string? ThumbnailUrl { get; set; }

        public string ColorName { get; set; } = string.Empty;

        public string SizeName { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }

        public int StockQuantity { get; set; }

        public int Quantity { get; set; }

        public decimal TotalPrice { get; set; }

        public bool IsAvailable { get; set; }
    }
}
