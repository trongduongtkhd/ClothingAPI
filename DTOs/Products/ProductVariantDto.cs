namespace ClothingAPI.DTOs.Products
{
    public class ProductVariantDto
    {
        public int VariantId { get; set; }

        public int ColorId { get; set; }

        public string ColorName { get; set; } = string.Empty;

        public string? ColorCode { get; set; }

        public int SizeId { get; set; }

        public string SizeName { get; set; } = string.Empty;

        public string SKU { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public decimal? SalePrice { get; set; }

        public int StockQuantity { get; set; }

        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; }
    }
}
