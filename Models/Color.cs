namespace ClothingAPI.Models
{
    public class Color
    {
        public int ColorId { get; set; }

        public string ColorName { get; set; } = string.Empty;

        public string? ColorCode { get; set; }
        public ICollection<ProductVariant> ProductVariants { get; set; } = new List<ProductVariant>();

    }
}
