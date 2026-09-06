namespace ClothingAPI.DTOs.Brands
{
    public class BrandDto
    {
        public int BrandId { get; set; }

        public string BrandName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? LogoUrl { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
