namespace ClothingAPI.DTOs.Addresses
{
    public class AddressDto
    {
        public int AddressId { get; set; }

        public string ReceiverName { get; set; } = string.Empty;

        public string ReceiverPhone { get; set; } = string.Empty;

        public string AddressDetail { get; set; } = string.Empty;

        public string? Ward { get; set; }

        public string? District { get; set; }

        public string Province { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
