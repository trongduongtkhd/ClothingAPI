using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Products
{
    public class UpsertProductVariantDto
    {
        [Range(1, int.MaxValue)]
        public int ColorId { get; set; }

        [Range(1, int.MaxValue)]
        public int SizeId { get; set; }

        [Required(ErrorMessage = "SKU là bắt buộc.")]
        [StringLength(100)]
        public string SKU { get; set; } = string.Empty;

        [Range(typeof(decimal), "0.01", "9999999999999999")]
        public decimal Price { get; set; }

        [Range(typeof(decimal), "0.01", "9999999999999999")]
        public decimal? SalePrice { get; set; }

        [Range(0, int.MaxValue)]
        public int StockQuantity { get; set; }

        [StringLength(500)]
        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
