using ClothingAPI.Models;
using ClothingAPI.Enums;
using Microsoft.EntityFrameworkCore;
namespace ClothingAPI.Data
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Brand> Brands { get; set; }
        public DbSet<Color> Colors { get; set; }
        public DbSet<Size> Sizes { get; set; }

        public DbSet<Product> Products { get; set; }

        public DbSet<ProductVariant> ProductVariants { get; set; }

        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<CartItem> CartItems { get; set; }

        public DbSet<Coupon> Coupons { get; set; }

        public DbSet<CouponCategory> CouponCategories { get; set; }

        public DbSet<CouponProduct> CouponProducts { get; set; }

        public DbSet<Order> Orders { get; set; }

        public DbSet<OrderItem> OrderItems { get; set; }

        public DbSet<Payment> Payments { get; set; }

        public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

        public DbSet<CouponUsage> CouponUsages { get; set; }
        public DbSet<Review> Reviews { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureUser(modelBuilder);
            ConfigureRole(modelBuilder);
            ConfigureUserRole(modelBuilder);

            ConfigureCategory(modelBuilder);
            ConfigureBrand(modelBuilder);
            ConfigureColor(modelBuilder);
            ConfigureSize(modelBuilder);

            ConfigureProduct(modelBuilder);
            ConfigureProductVariant(modelBuilder);
            ConfigureProductImage(modelBuilder);

            ConfigureAddress(modelBuilder);
            ConfigureCartItem(modelBuilder);

            ConfigureCoupon(modelBuilder);
            ConfigureCouponCategory(modelBuilder);
            ConfigureCouponProduct(modelBuilder);

            ConfigureOrder(modelBuilder);
            ConfigureOrderItem(modelBuilder);
            ConfigurePayment(modelBuilder);
            ConfigureOrderStatusHistory(modelBuilder);
            ConfigureCouponUsage(modelBuilder);

            ConfigureReview(modelBuilder);

            SeedRoles(modelBuilder);
        }

        private static void ConfigureUser(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");

                entity.HasKey(x => x.UserId);

                entity.Property(x => x.FullName)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(x => x.Email)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.HasIndex(x => x.Email)
                    .IsUnique();

                entity.Property(x => x.PasswordHash)
                    .IsRequired();

                entity.Property(x => x.PhoneNumber)
                    .HasMaxLength(20);

                entity.Property(x => x.AvatarUrl)
                    .HasMaxLength(500);

                entity.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");
            });
        }

        private static void ConfigureRole(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Role>(entity =>
            {
                entity.ToTable("Roles");

                entity.HasKey(x => x.RoleId);

                entity.Property(x => x.RoleName)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasIndex(x => x.RoleName)
                    .IsUnique();

                entity.Property(x => x.Description)
                    .HasMaxLength(255);
            });
        }

        private static void ConfigureUserRole(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.ToTable("UserRoles");

                entity.HasKey(x => new { x.UserId, x.RoleId });

                entity.HasOne(x => x.User)
                    .WithMany(x => x.UserRoles)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Role)
                    .WithMany(x => x.UserRoles)
                    .HasForeignKey(x => x.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureCategory(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("Categories");

                entity.HasKey(x => x.CategoryId);

                entity.Property(x => x.CategoryName)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(x => x.Slug)
                    .HasMaxLength(180)
                    .IsRequired();

                entity.HasIndex(x => x.Slug)
                    .IsUnique();

                entity.Property(x => x.Description)
                    .HasMaxLength(500);

                entity.Property(x => x.ImageUrl)
                    .HasMaxLength(500);

                entity.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.ParentCategory)
                    .WithMany(x => x.ChildCategories)
                    .HasForeignKey(x => x.ParentCategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureBrand(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Brand>(entity =>
            {
                entity.ToTable("Brands");

                entity.HasKey(x => x.BrandId);

                entity.Property(x => x.BrandName)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.HasIndex(x => x.BrandName)
                    .IsUnique();

                entity.Property(x => x.Description)
                    .HasMaxLength(500);

                entity.Property(x => x.LogoUrl)
                    .HasMaxLength(500);

                entity.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");
            });
        }

        private static void ConfigureColor(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Color>(entity =>
            {
                entity.ToTable("Colors");

                entity.HasKey(x => x.ColorId);

                entity.Property(x => x.ColorName)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasIndex(x => x.ColorName)
                    .IsUnique();

                entity.Property(x => x.ColorCode)
                    .HasMaxLength(20);
            });
        }

        private static void ConfigureSize(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Size>(entity =>
            {
                entity.ToTable("Sizes");

                entity.HasKey(x => x.SizeId);

                entity.Property(x => x.SizeName)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.HasIndex(x => x.SizeName)
                    .IsUnique();

                entity.Property(x => x.DisplayOrder)
                    .IsRequired();
            });
        }
        private static void ConfigureProduct(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("Products");

                entity.HasKey(x => x.ProductId);

                entity.Property(x => x.ProductName)
                    .HasMaxLength(250)
                    .IsRequired();

                entity.Property(x => x.Slug)
                    .HasMaxLength(300)
                    .IsRequired();

                entity.HasIndex(x => x.Slug)
                    .IsUnique();

                entity.Property(x => x.ShortDescription)
                    .HasMaxLength(500);

                entity.Property(x => x.Description)
                    .IsRequired();

                entity.Property(x => x.Material)
                    .HasMaxLength(150);

                entity.Property(x => x.Gender)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.BasePrice)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(x => x.SalePrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.IsFeatured)
                    .HasDefaultValue(false);

                entity.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.Category)
                    .WithMany(x => x.Products)
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Brand)
                    .WithMany(x => x.Products)
                    .HasForeignKey(x => x.BrandId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }

        private static void ConfigureProductVariant(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProductVariant>(entity =>
            {
                entity.ToTable("ProductVariants");

                entity.HasKey(x => x.VariantId);

                entity.Property(x => x.SKU)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.HasIndex(x => x.SKU)
                    .IsUnique();

                entity.HasIndex(x => new
                {
                    x.ProductId,
                    x.ColorId,
                    x.SizeId
                }).IsUnique();

                entity.Property(x => x.Price)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(x => x.SalePrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.StockQuantity)
                    .IsRequired();

                entity.Property(x => x.ImageUrl)
                    .HasMaxLength(500);

                entity.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                entity.HasOne(x => x.Product)
                    .WithMany(x => x.ProductVariants)
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Color)
                    .WithMany(x => x.ProductVariants)
                    .HasForeignKey(x => x.ColorId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Size)
                    .WithMany(x => x.ProductVariants)
                    .HasForeignKey(x => x.SizeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureProductImage(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProductImage>(entity =>
            {
                entity.ToTable("ProductImages");

                entity.HasKey(x => x.ImageId);

                entity.Property(x => x.ImageUrl)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(x => x.DisplayOrder)
                    .IsRequired();

                entity.Property(x => x.IsThumbnail)
                    .HasDefaultValue(false);

                entity.HasOne(x => x.Product)
                    .WithMany(x => x.ProductImages)
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private static void ConfigureAddress(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Address>(entity =>
            {
                entity.ToTable("Addresses");

                entity.HasKey(x => x.AddressId);

                entity.Property(x => x.ReceiverName)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(x => x.ReceiverPhone)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.AddressDetail)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(x => x.Ward)
                    .HasMaxLength(100);

                entity.Property(x => x.District)
                    .HasMaxLength(100);

                entity.Property(x => x.Province)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.IsDefault)
                    .HasDefaultValue(false);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.User)
                    .WithMany(x => x.Addresses)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Mỗi User chỉ có tối đa một địa chỉ mặc định.
                entity.HasIndex(x => x.UserId)
                    .IsUnique()
                    .HasFilter("[IsDefault] = 1");
            });
        }
        private static void ConfigureCartItem(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CartItem>(entity =>
            {
                entity.ToTable("CartItems");

                entity.HasKey(x => x.CartItemId);

                entity.Property(x => x.Quantity)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                // Một User chỉ có một dòng giỏ hàng cho cùng một Variant.
                entity.HasIndex(x => new { x.UserId, x.VariantId })
                    .IsUnique();

                entity.HasOne(x => x.User)
                    .WithMany(x => x.CartItems)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Variant)
                    .WithMany(x => x.CartItems)
                    .HasForeignKey(x => x.VariantId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureCoupon(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Coupon>(entity =>
            {
                entity.ToTable("Coupons");

                entity.HasKey(x => x.CouponId);

                entity.Property(x => x.Code)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasIndex(x => x.Code)
                    .IsUnique();

                entity.Property(x => x.Name)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(x => x.Description)
                    .HasMaxLength(500);

                entity.Property(x => x.DiscountType)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.DiscountValue)
                    .HasPrecision(18, 2)
                    .IsRequired();

                entity.Property(x => x.MaxDiscountAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.MinOrderAmount)
                    .HasPrecision(18, 2)
                    .HasDefaultValue(0);

                entity.Property(x => x.UsedCount)
                    .HasDefaultValue(0);

                entity.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureCouponCategory(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CouponCategory>(entity =>
            {
                entity.ToTable("CouponCategories");

                entity.HasKey(x => new { x.CouponId, x.CategoryId });

                entity.HasOne(x => x.Coupon)
                    .WithMany(x => x.CouponCategories)
                    .HasForeignKey(x => x.CouponId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Category)
                    .WithMany()
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureCouponProduct(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CouponProduct>(entity =>
            {
                entity.ToTable("CouponProducts");

                entity.HasKey(x => new { x.CouponId, x.ProductId });

                entity.HasOne(x => x.Coupon)
                    .WithMany(x => x.CouponProducts)
                    .HasForeignKey(x => x.CouponId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureOrder(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("Orders");

                entity.HasKey(x => x.OrderId);

                entity.Property(x => x.OrderCode)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.HasIndex(x => x.OrderCode)
                    .IsUnique();

                entity.Property(x => x.ReceiverName)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(x => x.ReceiverPhone)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.ShippingAddress)
                    .HasMaxLength(700)
                    .IsRequired();

                entity.Property(x => x.Note)
                    .HasMaxLength(500);

                entity.Property(x => x.Subtotal)
                    .HasPrecision(18, 2);

                entity.Property(x => x.ShippingFee)
                    .HasPrecision(18, 2);

                entity.Property(x => x.DiscountAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.TotalAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.CouponCode)
                    .HasMaxLength(50);

                entity.Property(x => x.OrderStatus)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.PaymentStatus)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.User)
                    .WithMany(x => x.Orders)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Coupon)
                    .WithMany()
                    .HasForeignKey(x => x.CouponId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }

        private static void ConfigureOrderItem(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("OrderItems");

                entity.HasKey(x => x.OrderItemId);

                entity.Property(x => x.ProductName)
                    .HasMaxLength(250)
                    .IsRequired();

                entity.Property(x => x.SKU)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.ColorName)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(x => x.SizeName)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.UnitPrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.TotalPrice)
                    .HasPrecision(18, 2);

                entity.HasOne(x => x.Order)
                    .WithMany(x => x.OrderItems)
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Variant)
                    .WithMany(x => x.OrderItems)
                    .HasForeignKey(x => x.VariantId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigurePayment(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("Payments");

                entity.HasKey(x => x.PaymentId);

                entity.Property(x => x.PaymentMethod)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.PaymentStatus)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.Amount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.TransactionCode)
                    .HasMaxLength(150);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.Order)
                    .WithMany(x => x.Payments)
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private static void ConfigureOrderStatusHistory(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderStatusHistory>(entity =>
            {
                entity.ToTable("OrderStatusHistories");

                entity.HasKey(x => x.OrderStatusHistoryId);

                entity.Property(x => x.Status)
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.Note)
                    .HasMaxLength(500);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.Order)
                    .WithMany(x => x.OrderStatusHistories)
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.ChangedByUser)
                    .WithMany(x => x.ChangedOrderStatusHistories)
                    .HasForeignKey(x => x.ChangedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        private static void ConfigureCouponUsage(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CouponUsage>(entity =>
            {
                entity.ToTable("CouponUsages");

                entity.HasKey(x => x.CouponUsageId);

                entity.Property(x => x.DiscountAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.UsedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                // Mỗi Order chỉ dùng tối đa một coupon.
                entity.HasIndex(x => x.OrderId)
                    .IsUnique();

                entity.HasOne(x => x.Coupon)
                    .WithMany(x => x.CouponUsages)
                    .HasForeignKey(x => x.CouponId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.User)
                    .WithMany(x => x.CouponUsages)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Order)
                    .WithMany(x => x.CouponUsages)
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private static void ConfigureReview(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Review>(entity =>
            {
                entity.ToTable("Reviews");

                entity.HasKey(x => x.ReviewId);

                entity.Property(x => x.Rating)
                    .IsRequired();

                entity.Property(x => x.Comment)
                    .HasMaxLength(1000);

                entity.Property(x => x.IsApproved)
                    .HasDefaultValue(false);

                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(x => x.Product)
                    .WithMany(x => x.Reviews)
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.User)
                    .WithMany(x => x.Reviews)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.OrderItem)
                    .WithMany(x => x.Reviews)
                    .HasForeignKey(x => x.OrderItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Một OrderItem chỉ được đánh giá một lần.
                entity.HasIndex(x => x.OrderItemId)
                    .IsUnique()
                    .HasFilter("[OrderItemId] IS NOT NULL");
            });
        }
        private static void SeedRoles(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Role>().HasData(
                new Role
                {
                    RoleId = 1,
                    RoleName = "Admin",
                    Description = "Quản trị viên hệ thống"
                },
                new Role
                {
                    RoleId = 2,
                    RoleName = "Customer",
                    Description = "Khách hàng"
                }
            );
        }


    }
}
