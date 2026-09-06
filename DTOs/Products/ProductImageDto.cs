namespace ClothingAPI.DTOs.Products
{
    public class ProductImageDto
    {
        public int ImageId { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }

        public bool IsThumbnail { get; set; }
    }
}
