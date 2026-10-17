using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClothingAPI.Data;
using ClothingAPI.Enums;
using ClothingAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.IntegrationTests;

public record TestUser(int UserId, string Email, string Password, string AccessToken);

public static class TestHelpers
{
    public const string DefaultPassword = "Test@1234";

    public static string UniqueEmail(string prefix) =>
        $"{prefix}.{Guid.NewGuid():N}@test.local";

    public static string UniqueCode(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    public static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password, string fullName = "Khách Test") =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName,
            email,
            password,
            confirmPassword = password,
            phoneNumber = "0900000000"
        });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static async Task<string> LoginAndGetTokenAsync(HttpClient client, string email, string password)
    {
        var response = await LoginAsync(client, email, password);
        var body = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Đăng nhập thất bại: {(int)response.StatusCode} {body}");
        return body.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    /// Đăng ký khách hàng mới qua API rồi đăng nhập lấy token.
    public static async Task<TestUser> RegisterAndLoginCustomerAsync(HttpClient client, string prefix = "customer")
    {
        var email = UniqueEmail(prefix);
        var register = await RegisterAsync(client, email, DefaultPassword);
        var registerBody = await ReadJsonAsync(register);
        Assert.True(register.StatusCode == HttpStatusCode.Created, $"Đăng ký thất bại: {(int)register.StatusCode} {registerBody}");

        var token = await LoginAndGetTokenAsync(client, email, DefaultPassword);
        var userId = registerBody.GetProperty("data").GetProperty("user").GetProperty("userId").GetInt32();
        return new TestUser(userId, email, DefaultPassword, token);
    }

    /// Tạo tài khoản quản trị (role Admin) trực tiếp trong CSDL rồi đăng nhập qua API.
    public static async Task<TestUser> CreateAdminAndLoginAsync(CustomWebApplicationFactory factory, HttpClient client)
    {
        var email = UniqueEmail("admin");
        int userId;

        await using (var db = factory.CreateDbContext())
        {
            var adminRole = await db.Roles.SingleAsync(r => r.RoleName == "Admin");
            var admin = new User
            {
                FullName = "Quản trị viên Test",
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            admin.UserRoles.Add(new UserRole { RoleId = adminRole.RoleId });
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            userId = admin.UserId;
        }

        var token = await LoginAndGetTokenAsync(client, email, DefaultPassword);
        return new TestUser(userId, email, DefaultPassword, token);
    }

    public static async Task<int> CreateAddressAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/addresses", new
        {
            receiverName = "Người nhận Test",
            receiverPhone = "0911111111",
            addressDetail = "123 Đường Test",
            ward = "Phường 1",
            district = "Quận 1",
            province = "TP. Hồ Chí Minh",
            isDefault = true
        });
        var body = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Tạo địa chỉ thất bại: {(int)response.StatusCode} {body}");
        return body.GetProperty("data").GetProperty("addressId").GetInt32();
    }

    public static async Task AddToCartAsync(HttpClient client, int variantId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/cart/items", new { variantId, quantity });
        var body = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Thêm vào giỏ thất bại: {(int)response.StatusCode} {body}");
    }

    public static Task<HttpResponseMessage> PostOrderAsync(HttpClient client, int addressId, string? couponCode, string note = "Integration test") =>
        client.PostAsJsonAsync("/api/orders", new
        {
            addressId,
            paymentMethod = "COD",
            note,
            couponCode
        });

    /// Tạo đơn qua API và trả về OrderId (yêu cầu tạo đơn phải thành công).
    public static async Task<int> CreateOrderAsync(HttpClient client, int addressId, string? couponCode = null)
    {
        var response = await PostOrderAsync(client, addressId, couponCode);
        var body = await ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Tạo đơn thất bại: {(int)response.StatusCode} {body}");
        return body.GetProperty("data").GetProperty("orderId").GetInt32();
    }

    /// Tạo một sản phẩm + một biến thể riêng cho từng test (SKU/slug duy nhất) với tồn kho cho trước.
    public static async Task<int> CreateVariantAsync(CustomWebApplicationFactory factory, int stockQuantity, decimal basePrice = 200000m)
    {
        await using var db = factory.CreateDbContext();

        var category = await db.Categories.FirstAsync();
        var color = await db.Colors.FirstAsync();
        var size = await db.Sizes.FirstAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var product = new Product
        {
            CategoryId = category.CategoryId,
            ProductName = $"Áo test {suffix}",
            Slug = $"ao-test-{suffix}",
            Description = "Sản phẩm cho integration test",
            Gender = Gender.Unisex,
            BasePrice = basePrice,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var variant = new ProductVariant
        {
            ColorId = color.ColorId,
            SizeId = size.SizeId,
            SKU = $"SKU-{suffix}",
            StockQuantity = stockQuantity,
            IsActive = true
        };
        product.ProductVariants.Add(variant);

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return variant.VariantId;
    }

    /// Tạo mã giảm giá riêng cho từng test (10%, không giới hạn), còn hạn hoặc đã hết hạn.
    public static async Task<string> CreateCouponAsync(CustomWebApplicationFactory factory, bool expired)
    {
        await using var db = factory.CreateDbContext();

        var adminId = await db.Users
            .Where(u => u.UserRoles.Any(ur => ur.Role.RoleName == "Admin"))
            .Select(u => u.UserId)
            .FirstAsync();

        var code = UniqueCode(expired ? "EXP" : "VAL");
        db.Coupons.Add(new Coupon
        {
            Code = code,
            Name = expired ? "Mã test hết hạn" : "Mã test còn hạn",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10,
            MinOrderAmount = 0,
            StartDate = DateTime.UtcNow.AddDays(expired ? -30 : -1),
            EndDate = expired ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(30),
            IsActive = true,
            UsedCount = 0,
            CreatedByUserId = adminId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return code;
    }

    public static async Task<T> WithDbAsync<T>(CustomWebApplicationFactory factory, Func<AppDbContext, Task<T>> action)
    {
        await using var db = factory.CreateDbContext();
        return await action(db);
    }
}
