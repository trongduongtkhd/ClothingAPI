using System.ComponentModel.DataAnnotations;

using ClothingAPI.Helpers;

namespace ClothingAPI.DTOs.Users
{
    public class UpdateProfileDto
    {
        private string _fullName = string.Empty;
        private string? _phoneNumber;

        [Required(ErrorMessage = "Họ tên là bắt buộc.")]
        [StringLength(150)]
        [PersonName]
        public string FullName { get => _fullName; set => _fullName = UserInputRules.NormalizeFullName(value); }

        [VietnameseMobilePhone]
        public string? PhoneNumber { get => _phoneNumber; set => _phoneNumber = UserInputRules.NormalizePhone(value); }

        [StringLength(500)]
        public string? AvatarUrl { get; set; }
    }
}
