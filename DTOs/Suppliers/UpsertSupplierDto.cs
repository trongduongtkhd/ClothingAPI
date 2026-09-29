using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Suppliers
{
    public class UpsertSupplierDto
    {
        [Required(ErrorMessage = "Tên nhà cung ứng là bắt buộc.")]
        [StringLength(150, ErrorMessage = "Tên nhà cung ứng không được vượt quá 150 ký tự.")]
        public string SupplierName { get; set; } = string.Empty;

        [StringLength(150)]
        public string? ContactName { get; set; }

        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(150)]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        public string? Email { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(50)]
        public string? TaxCode { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
