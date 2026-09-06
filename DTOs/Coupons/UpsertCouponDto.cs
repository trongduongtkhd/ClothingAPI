using System.ComponentModel.DataAnnotations;
using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Coupons;

public class UpsertCouponDto
{
    [Required(ErrorMessage = "Mã giảm giá là bắt buộc.")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên chương trình là bắt buộc.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public DiscountType DiscountType { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal DiscountValue { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal? MaxDiscountAmount { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal MinOrderAmount { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [Range(1, int.MaxValue)]
    public int? UsageLimit { get; set; }

    [Range(1, int.MaxValue)]
    public int? UsageLimitPerUser { get; set; }

    public bool IsActive { get; set; } = true;

    public List<int> CategoryIds { get; set; } = [];

    public List<int> ProductIds { get; set; } = [];
}