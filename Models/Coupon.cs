using ClothingAPI.Enums;

namespace ClothingAPI.Models;

public class Coupon
{
    public int CouponId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DiscountType DiscountType { get; set; }

    public decimal DiscountValue { get; set; } // gia tri giam

    public decimal? MaxDiscountAmount { get; set; } // so tien giam toi da

    public decimal MinOrderAmount { get; set; } // Gia tri don hang toi thieu de duoc dung ma

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public int? UsageLimit { get; set; } // tong so luot dung toi da cua bo ma

    public int? UsageLimitPerUser { get; set; } // So lan toi da 1 user duoc dung ma

    public int UsedCount { get; set; }  // Tong so luot dung hien tai

    public bool IsActive { get; set; } = true;

    public int CreatedByUserId { get; set; } // admin tao ma

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public ICollection<CouponCategory> CouponCategories { get; set; } = new List<CouponCategory>();
    public ICollection<CouponUsage> CouponUsages { get; set; } = new List<CouponUsage>();
    public ICollection<CouponProduct> CouponProducts { get; set; } = new List<CouponProduct>();

}