using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Addresses
{
    public class UpsertAddressDto
    {
        [Required(ErrorMessage = "Tên người nhận là bắt buộc.")]
        [StringLength(150)]
        public string ReceiverName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số điện thoại người nhận là bắt buộc.")]
        [StringLength(20)]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Địa chỉ chi tiết là bắt buộc.")]
        [StringLength(500)]
        public string AddressDetail { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Ward { get; set; }

        [StringLength(100)]
        public string? District { get; set; }

        [Required(ErrorMessage = "Tỉnh/thành phố là bắt buộc.")]
        [StringLength(100)]
        public string Province { get; set; } = string.Empty;

        public bool IsDefault { get; set; }
    }
}
