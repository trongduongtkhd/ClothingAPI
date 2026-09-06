namespace ClothingAPI.Models
{
    public class ProductVariant
    {
        public int VariantId { get; set; }

        public int ProductId { get; set; }

        public int ColorId { get; set; }

        public int SizeId { get; set; }

        public string SKU { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public decimal? SalePrice { get; set; }

        public int StockQuantity { get; set; }

        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public Product Product { get; set; } = null!;

        public Color Color { get; set; } = null!;

        public Size Size { get; set; } = null!;
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();


    }
}
