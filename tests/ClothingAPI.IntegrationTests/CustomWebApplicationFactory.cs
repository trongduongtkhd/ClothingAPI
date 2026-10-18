using ClothingAPI.Data;
using ClothingAPI.Enums;
using ClothingAPI.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ClothingAPI.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin.seed@test.local";
    public const string AdminPassword = "Admin@1234";
    public const string ValidCouponCode = "VALID10";
    public const string ExpiredCouponCode = "EXPIRED10";

    public const string JwtSecretKey = "integration-test-secret-key-0123456789-abcdefgh";

    private readonly SqliteConnection _connection;

    public CustomWebApplicationFactory()
    {
        // Một kết nối in-memory mở suốt vòng đời factory: đóng kết nối là mất CSDL.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // AppDbContext dùng HasDefaultValueSql("GETUTCDATE()") (hàm SQL Server) – đăng ký hàm tương đương cho SQLite.
        _connection.CreateFunction("GETUTCDATE", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF"));

        // Program đọc JwtSettings ngay trước builder.Build(), nên đặt qua biến môi trường cho chắc chắn.
        Environment.SetEnvironmentVariable("JwtSettings__SecretKey", JwtSecretKey);
    }

    public SqliteConnection Connection => _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" để không nạp User Secrets của môi trường Development.
        builder.UseEnvironment("Testing");
        builder.UseSetting("JwtSettings:SecretKey", JwtSecretKey);
        builder.UseSetting("JwtSettings:Issuer", "ClothingStoreAPI");
        builder.UseSetting("JwtSettings:Audience", "ClothingStoreAngular");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=:memory:");
        builder.UseSetting("AdminSeedSettings:Email", "");
        builder.UseSetting("AdminSeedSettings:Password", "");
        // Test client chạy http://localhost: cookie Secure sẽ không được gửi lại.
        builder.UseSetting("RefreshTokenSettings:CookieSecure", "false");
        builder.UseSetting("RefreshTokenSettings:ReuseGraceSeconds", "0");

        builder.ConfigureTestServices(services =>
        {
            var descriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options
                .UseSqlite(_connection)
                .ReplaceService<IMigrator, NoOpMigrator>());
        });

        CreateSchemaAndSeed();
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options);
    }

    private void CreateSchemaAndSeed()
    {
        using var context = CreateDbContext();

        // Roles Admin(1) / Customer(2) được tạo bởi HasData trong AppDbContext.
        context.Database.EnsureCreated();

        if (context.Users.Any())
        {
            return;
        }

        var admin = new User
        {
            FullName = "Seed Administrator",
            Email = AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(AdminPassword),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        admin.UserRoles.Add(new UserRole { RoleId = 1 });
        context.Users.Add(admin);

        var category = new Category { CategoryName = "Áo thun", Slug = "ao-thun", IsActive = true, CreatedAt = DateTime.UtcNow };
        var brand = new Brand { BrandName = "Test Brand", IsActive = true, CreatedAt = DateTime.UtcNow };
        var red = new Color { ColorName = "Đỏ", ColorCode = "#FF0000" };
        var blue = new Color { ColorName = "Xanh", ColorCode = "#0000FF" };
        var sizeM = new Size { SizeName = "M", DisplayOrder = 1 };
        var sizeL = new Size { SizeName = "L", DisplayOrder = 2 };

        context.AddRange(category, brand, red, blue, sizeM, sizeL);

        var product = new Product
        {
            Category = category,
            Brand = brand,
            ProductName = "Áo thun mẫu",
            Slug = "ao-thun-mau",
            Description = "Sản phẩm seed cho integration test",
            Gender = Gender.Unisex,
            BasePrice = 200000m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        product.ProductVariants.Add(new ProductVariant { Color = red, Size = sizeM, SKU = "SEED-RED-M", StockQuantity = 50, IsActive = true });
        product.ProductVariants.Add(new ProductVariant { Color = blue, Size = sizeL, SKU = "SEED-BLUE-L", StockQuantity = 50, IsActive = true });
        context.Products.Add(product);

        context.SaveChanges();

        context.Coupons.AddRange(
            new Coupon
            {
                Code = ValidCouponCode,
                Name = "Giảm 10% còn hạn",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10,
                MinOrderAmount = 0,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(30),
                IsActive = true,
                CreatedByUserId = admin.UserId,
                CreatedAt = DateTime.UtcNow
            },
            new Coupon
            {
                Code = ExpiredCouponCode,
                Name = "Giảm 10% đã hết hạn",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10,
                MinOrderAmount = 0,
                StartDate = DateTime.UtcNow.AddDays(-30),
                EndDate = DateTime.UtcNow.AddDays(-1),
                IsActive = true,
                CreatedByUserId = admin.UserId,
                CreatedAt = DateTime.UtcNow
            });

        context.SaveChanges();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
