namespace ClothingAPI.Models
{
    public class ProductImage
    {
        public int ImageId { get; set; }

        public int ProductId { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }

        public bool IsThumbnail { get; set; }

        public Product Product { get; set; } = null!;
    }
}
