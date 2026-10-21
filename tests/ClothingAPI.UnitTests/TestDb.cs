using System.ComponentModel.DataAnnotations;
using ClothingAPI.Data;
using ClothingAPI.Enums;
using ClothingAPI.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// CSDL SQLite in-memory riêng cho MỖI test (mỗi instance = một kết nối = một CSDL mới).
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public int AdminId { get; }
    public int CustomerId { get; }
    public int CategoryAoId { get; }
    public int CategoryQuanId { get; }
    public int SupplierId { get; }

    private readonly int _colorId;
    private readonly int _sizeId;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        // AppDbContext dùng HasDefaultValueSql("GETUTCDATE()") của SQL Server.
        _connection.CreateFunction("GETUTCDATE", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF"));

        using var db = NewContext();
        db.Database.EnsureCreated(); // Roles Admin(1)/Customer(2) có sẵn từ HasData.

        var admin = NewUser("Quản trị viên", "admin@test.local", "Admin@1234", roleId: 1);
        var customer = NewUser("Khách hàng", "khach@test.local", "Khach@1234", roleId: 2);
        var ao = new Category { CategoryName = "Áo", Slug = "ao", CreatedAt = DateTime.UtcNow };
        var quan = new Category { CategoryName = "Quần", Slug = "quan", CreatedAt = DateTime.UtcNow };
        var color = new Color { ColorName = "Đen", ColorCode = "#000000" };
        var size = new Size { SizeName = "M", DisplayOrder = 1 };
        var supplier = new Supplier { SupplierName = "Nhà cung ứng A", Phone = "0900000000", CreatedAt = DateTime.UtcNow };

        db.AddRange(admin, customer, ao, quan, color, size, supplier);
        db.SaveChanges();

        AdminId = admin.UserId;
        CustomerId = customer.UserId;
        CategoryAoId = ao.CategoryId;
        CategoryQuanId = quan.CategoryId;
        SupplierId = supplier.SupplierId;
        _colorId = color.ColorId;
        _sizeId = size.SizeId;
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    public T Query<T>(Func<AppDbContext, T> query)
    {
        using var db = NewContext();
        return query(db);
    }

    public static User NewUser(string fullName, string email, string password, int roleId,
        int failedAttempts = 0, DateTime? lockoutEndUtc = null, bool isActive = true)
    {
        var user = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsActive = isActive,
            FailedLoginAttempts = failedAttempts,
            LockoutEndUtc = lockoutEndUtc,
            CreatedAt = DateTime.UtcNow
        };
        user.UserRoles.Add(new UserRole { RoleId = roleId });
        return user;
    }

    public int AddUser(string email, string password, int failedAttempts = 0, DateTime? lockoutEndUtc = null, bool isActive = true)
    {
        using var db = NewContext();
        var user = NewUser("Người dùng test", email, password, roleId: 2, failedAttempts, lockoutEndUtc, isActive);
        db.Users.Add(user);
        db.SaveChanges();
        return user.UserId;
    }

    /// Tạo 1 sản phẩm + 1 biến thể (biến thể không có giá riêng ⇒ bán theo giá sản phẩm).
    public (int VariantId, string Sku) AddVariant(
        decimal basePrice = 200000m,
        decimal? salePrice = null,
        int stock = 10,
        decimal? averageCost = null,
        bool isActive = true,
        int? categoryId = null)
    {
        using var db = NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = new Product
        {
            CategoryId = categoryId ?? CategoryAoId,
            ProductName = $"Sản phẩm {suffix}",
            Slug = $"san-pham-{suffix}",
            Description = "Sản phẩm unit test",
            Gender = Gender.Unisex,
            BasePrice = basePrice,
            SalePrice = salePrice,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var variant = new ProductVariant
        {
            ColorId = _colorId,
            SizeId = _sizeId,
            SKU = $"SKU-{suffix}".ToUpperInvariant(),
            StockQuantity = stock,
            AverageCostPrice = averageCost,
            IsActive = isActive
        };
        product.ProductVariants.Add(variant);
        db.Products.Add(product);
        db.SaveChanges();
        return (variant.VariantId, variant.SKU);
    }

    public int AddCartItem(int userId, int variantId, int quantity)
    {
        using var db = NewContext();
        var item = new CartItem { UserId = userId, VariantId = variantId, Quantity = quantity, CreatedAt = DateTime.UtcNow };
        db.CartItems.Add(item);
        db.SaveChanges();
        return item.CartItemId;
    }

    public int AddCoupon(
        string code,
        DiscountType type = DiscountType.Percentage,
        decimal value = 10,
        decimal? maxDiscount = null,
        decimal minOrder = 0,
        DateTime? start = null,
        DateTime? end = null,
        int? usageLimit = null,
        int? usageLimitPerUser = null,
        int usedCount = 0,
        bool isActive = true,
        int[]? categoryIds = null)
    {
        using var db = NewContext();
        var coupon = new Coupon
        {
            Code = code,
            Name = $"Mã {code}",
            DiscountType = type,
            DiscountValue = value,
            MaxDiscountAmount = maxDiscount,
            MinOrderAmount = minOrder,
            StartDate = start ?? DateTime.UtcNow.AddDays(-1),
            EndDate = end ?? DateTime.UtcNow.AddDays(30),
            UsageLimit = usageLimit,
            UsageLimitPerUser = usageLimitPerUser,
            UsedCount = usedCount,
            IsActive = isActive,
            CreatedByUserId = AdminId,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var categoryId in categoryIds ?? [])
        {
            coupon.CouponCategories.Add(new CouponCategory { CategoryId = categoryId });
        }
        db.Coupons.Add(coupon);
        db.SaveChanges();
        return coupon.CouponId;
    }

    public int AddAddress(int userId)
    {
        using var db = NewContext();
        var address = new Address
        {
            UserId = userId,
            ReceiverName = "Người nhận",
            ReceiverPhone = "0911111111",
            AddressDetail = "1 Đường A",
            Ward = "Phường 1",
            District = "Quận 1",
            Province = "TP. Hồ Chí Minh",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Addresses.Add(address);
        db.SaveChanges();
        return address.AddressId;
    }

    /// Đơn hàng có sẵn (không qua OrderService), kèm 1 Payment COD; nếu có couponId thì ghi CouponUsage.
    public (int OrderId, string OrderCode) AddOrder(
        int userId,
        OrderStatus status,
        PaymentStatus paymentStatus = PaymentStatus.Unpaid,
        (int VariantId, int Quantity)[]? items = null,
        int? couponId = null,
        decimal discount = 0,
        PaymentMethod paymentMethod = PaymentMethod.COD)
    {
        using var db = NewContext();
        var order = new Order
        {
            OrderCode = $"DH{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            UserId = userId,
            ReceiverName = "Người nhận",
            ReceiverPhone = "0911111111",
            ShippingAddress = "1 Đường A, TP. Hồ Chí Minh",
            OrderStatus = status,
            PaymentStatus = paymentStatus,
            CouponId = couponId,
            DiscountAmount = discount,
            CreatedAt = DateTime.UtcNow
        };

        decimal subtotal = 0;
        foreach (var (variantId, quantity) in items ?? [])
        {
            var variant = db.ProductVariants.Include(v => v.Product).Single(v => v.VariantId == variantId);
            var unitPrice = variant.Product.SalePrice ?? variant.Product.BasePrice;
            order.OrderItems.Add(new OrderItem
            {
                VariantId = variantId,
                ProductName = variant.Product.ProductName,
                SKU = variant.SKU,
                ColorName = "Đen",
                SizeName = "M",
                UnitPrice = unitPrice,
                Quantity = quantity,
                TotalPrice = unitPrice * quantity
            });
            subtotal += unitPrice * quantity;
        }

        order.Subtotal = subtotal;
        order.TotalAmount = subtotal - discount;
        order.Payments.Add(new Payment
        {
            PaymentMethod = paymentMethod,
            Amount = order.TotalAmount,
            PaymentStatus = paymentStatus,
            PaidAt = paymentStatus == PaymentStatus.Paid ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow
        });

        if (couponId.HasValue)
        {
            order.CouponUsages.Add(new CouponUsage
            {
                CouponId = couponId.Value,
                UserId = userId,
                DiscountAmount = discount,
                UsedAt = DateTime.UtcNow
            });
        }

        db.Orders.Add(order);
        db.SaveChanges();
        return (order.OrderId, order.OrderCode);
    }

    /// Phiếu nhập kho có sẵn (chỉ ghi phiếu, KHÔNG cộng tồn – tồn/giá vốn của biến thể được đặt sẵn theo kịch bản).
    public int AddPurchaseOrder(int variantId, int quantity, decimal unitCost, string status = PurchaseOrderStatuses.Completed)
    {
        using var db = NewContext();
        var variant = db.ProductVariants.Include(v => v.Product).Single(v => v.VariantId == variantId);
        var purchaseOrder = new PurchaseOrder
        {
            PurchaseOrderCode = $"PN{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            SupplierId = SupplierId,
            CreatedByUserId = AdminId,
            Status = status,
            TotalAmount = quantity * unitCost,
            CreatedAt = DateTime.UtcNow,
            CancelledAt = status == PurchaseOrderStatuses.Cancelled ? DateTime.UtcNow : null,
            CancelReason = status == PurchaseOrderStatuses.Cancelled ? "Đã huỷ trước đó" : null
        };
        purchaseOrder.Items.Add(new PurchaseOrderItem
        {
            VariantId = variantId,
            ProductName = variant.Product.ProductName,
            SKU = variant.SKU,
            ColorName = "Đen",
            SizeName = "M",
            UnitCost = unitCost,
            Quantity = quantity,
            LineTotal = quantity * unitCost
        });
        db.PurchaseOrders.Add(purchaseOrder);
        db.SaveChanges();
        return purchaseOrder.PurchaseOrderId;
    }

    public void Dispose() => _connection.Dispose();
}

/// Lớp cơ sở: mỗi test có CSDL riêng + ghi lại "bước 1" (dữ liệu dựng) và "kết quả thực tế" để xuất báo cáo.
public abstract class UnitTestBase : IDisposable
{
    protected readonly TestDb Db = new();
    private readonly ITestOutputHelper _output;

    protected UnitTestBase(ITestOutputHelper output) => _output = output;

    protected void Step1(string text) => _output.WriteLine($"STEP1|{text}");

    protected void Actual(string text) => _output.WriteLine($"ACTUAL|{text}");

    protected static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    protected static string Describe(Exception? ex) =>
        ex is null ? "không có lỗi (thành công)" : $"lỗi {ex.GetType().Name}: \"{ex.Message}\"";

    /// Chạy validation DataAnnotations của DTO giống [ApiController] của ASP.NET Core.
    protected static List<string> ValidateDto(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results.Select(r => r.ErrorMessage ?? string.Empty).ToList();
    }

    protected static string Money(decimal value) => value.ToString("#,##0.##", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));

    public void Dispose() => Db.Dispose();
}
