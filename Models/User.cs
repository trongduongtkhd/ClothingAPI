namespace ClothingAPI.Models
{
    public class User
    {
        public int UserId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        public string? AvatarUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public string? ResetPasswordToken { get; set; }

        public DateTime? ResetPasswordTokenExpiry { get; set; }

        // Chống brute-force: đếm số lần đăng nhập sai liên tiếp, khoá tạm khi vượt ngưỡng.
        public int FailedLoginAttempts { get; set; }

        public DateTime? LockoutEndUtc { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public ICollection<Address> Addresses { get; set; } = new List<Address>();
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<OrderStatusHistory> ChangedOrderStatusHistories { get; set; } = new List<OrderStatusHistory>();
        public ICollection<CouponUsage> CouponUsages { get; set; } = new List<CouponUsage>();
        public ICollection<Review> Reviews { get; set; } = new List<Review>();
        public ICollection<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();
        public ICollection<PurchaseOrder> CreatedPurchaseOrders { get; set; } = new List<PurchaseOrder>();

    }
}
