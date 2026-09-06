namespace ClothingAPI.Models
{
    public class Size
    {
        public int SizeId { get; set; }
        public string SizeName { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public ICollection<ProductVariant> ProductVariants { get; set; } = new List<ProductVariant>();

    }
}
