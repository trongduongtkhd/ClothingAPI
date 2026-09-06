namespace ClothingAPI.DTOs.Storefront
{
    public class PublicBrandDto
    {
        public int BrandId { get; set; }

        public string BrandName { get; set; } = string.Empty;

        public string? LogoUrl { get; set; }
    }
}
