namespace ClothingAPI.Models
{
    public class Address
    {
        public int AddressId { get; set; }
        public int UserId { get; set; }
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string AddressDetail { get; set; } = string.Empty;
        public string? Ward { get; set; }
        public string? District { get; set; }
        public string Province { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public User User { get; set; } = null!;
    }
}
