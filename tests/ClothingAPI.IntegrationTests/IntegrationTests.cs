using System.Net;
using System.Net.Http.Json;
using ClothingAPI.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static ClothingAPI.IntegrationTests.TestHelpers;

// Mọi test dùng chung một kết nối SQLite in-memory nên chạy tuần tự.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ClothingAPI.IntegrationTests;

public class IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public IntegrationTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private void Log(string message) => _output.WriteLine(message);

    [Fact]
    public async Task IT001_GetCart_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/cart");

        Log($"GET /api/cart (không token) -> {(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IT002_AdminUsers_WithCustomerToken_Returns403()
    {
        var client = _factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "it002");
        Log($"Tạo khách hàng {customer.Email}, lấy token thành công");

        Authorize(client, customer.AccessToken);
        var response = await client.GetAsync("/api/admin/users");

        Log($"GET /api/admin/users (token Customer) -> {(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IT003_AdminUsers_WithAdminToken_Returns200WithUsers()
    {
        var client = _factory.CreateClient();
        var admin = await CreateAdminAndLoginAsync(_factory, client);
        Log($"Tạo quản trị viên {admin.Email}, lấy token thành công");

        Authorize(client, admin.AccessToken);
        var response = await client.GetAsync("/api/admin/users");
        var body = await ReadJsonAsync(response);

        Log($"GET /api/admin/users (token Admin) -> {(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("success").GetBoolean());

        var items = body.GetProperty("data").GetProperty("items");
        var totalItems = body.GetProperty("data").GetProperty("totalItems").GetInt32();
        Log($"Danh sách trả về {items.GetArrayLength()} người dùng (totalItems = {totalItems})");
        Assert.True(items.GetArrayLength() > 0);
    }

    [Fact]
    public async Task IT004_CustomerA_GetOrderOfCustomerB_IsDenied()
    {
        var variantId = await CreateVariantAsync(_factory, stockQuantity: 10);

        var clientB = _factory.CreateClient();
        var customerB = await RegisterAndLoginCustomerAsync(clientB, "it004b");
        Authorize(clientB, customerB.AccessToken);
        var addressB = await CreateAddressAsync(clientB);
        await AddToCartAsync(clientB, variantId, 1);
        var orderIdB = await CreateOrderAsync(clientB, addressB);
        Log($"Tạo đơn #{orderIdB} cho khách B ({customerB.Email}) -> 201");

        var clientA = _factory.CreateClient();
        var customerA = await RegisterAndLoginCustomerAsync(clientA, "it004a");
        Authorize(clientA, customerA.AccessToken);

        var response = await clientA.GetAsync($"/api/orders/{orderIdB}");
        var body = await ReadJsonAsync(response);

        Log($"GET /api/orders/{orderIdB} bằng token khách A -> {(int)response.StatusCode} {response.StatusCode}, message = \"{body.GetProperty("message").GetString()}\"");
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Mong đợi 403/404, thực tế {(int)response.StatusCode}");

        var hasData = body.TryGetProperty("data", out var data) && data.ValueKind != System.Text.Json.JsonValueKind.Null;
        Log($"Response có dữ liệu đơn: {hasData}");
        Assert.False(hasData);
    }

    [Fact]
    public async Task IT005_Register_Login_GetProfile_Succeeds()
    {
        var client = _factory.CreateClient();
        var email = UniqueEmail("it005");

        var register = await RegisterAsync(client, email, DefaultPassword, "Khách IT005");
        Log($"POST /api/auth/register ({email}) -> {(int)register.StatusCode} {register.StatusCode}");
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await LoginAsync(client, email, DefaultPassword);
        var loginBody = await ReadJsonAsync(login);
        var token = loginBody.GetProperty("data").GetProperty("accessToken").GetString();
        Log($"POST /api/auth/login -> {(int)login.StatusCode} {login.StatusCode}, có accessToken: {!string.IsNullOrEmpty(token)}");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(loginBody.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(token));

        Authorize(client, token!);
        var profile = await client.GetAsync("/api/users/profile");
        var profileBody = await ReadJsonAsync(profile);
        var profileEmail = profileBody.GetProperty("data").GetProperty("email").GetString();
        Log($"GET /api/users/profile -> {(int)profile.StatusCode} {profile.StatusCode}, email = {profileEmail}");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(email, profileEmail);
    }

    [Fact]
    public async Task IT006_CreateOrder_Success_UpdatesStockCartCoupon()
    {
        var variantId = await CreateVariantAsync(_factory, stockQuantity: 10);
        var couponCode = await CreateCouponAsync(_factory, expired: false);

        var client = _factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "it006");
        Authorize(client, customer.AccessToken);
        var addressId = await CreateAddressAsync(client);
        await AddToCartAsync(client, variantId, 2);

        var usedBefore = await WithDbAsync(_factory, db => db.Coupons.Where(c => c.Code == couponCode).Select(c => c.UsedCount).SingleAsync());
        Log($"Chuẩn bị: biến thể #{variantId} tồn 10, giỏ 2 sản phẩm, địa chỉ #{addressId}, mã {couponCode} (UsedCount = {usedBefore})");

        var response = await PostOrderAsync(client, addressId, couponCode);
        var body = await ReadJsonAsync(response);
        Log($"POST /api/orders -> {(int)response.StatusCode} {response.StatusCode}, message = \"{body.GetProperty("message").GetString()}\"");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var orderId = body.GetProperty("data").GetProperty("orderId").GetInt32();
        var orderStatus = await WithDbAsync(_factory, db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.OrderStatus).SingleAsync());
        var stockAfter = await WithDbAsync(_factory, db => db.ProductVariants.Where(v => v.VariantId == variantId).Select(v => v.StockQuantity).SingleAsync());
        var cartCount = await WithDbAsync(_factory, db => db.CartItems.CountAsync(c => c.UserId == customer.UserId));
        var usedAfter = await WithDbAsync(_factory, db => db.Coupons.Where(c => c.Code == couponCode).Select(c => c.UsedCount).SingleAsync());
        var discount = body.GetProperty("data").GetProperty("discountAmount").GetDecimal();
        var total = body.GetProperty("data").GetProperty("totalAmount").GetDecimal();

        Log($"Đơn #{orderId}: trạng thái {orderStatus}, giảm {discount:N0}, tổng {total:N0}; tồn 10 -> {stockAfter}; giỏ còn {cartCount} dòng; UsedCount {usedBefore} -> {usedAfter}");
        Assert.Equal(OrderStatus.Pending, orderStatus);
        Assert.Equal(8, stockAfter);
        Assert.Equal(0, cartCount);
        Assert.Equal(usedBefore + 1, usedAfter);
    }

    [Fact]
    public async Task IT007_CreateOrder_InsufficientStock_IsRejected()
    {
        var variantId = await CreateVariantAsync(_factory, stockQuantity: 1);

        var client = _factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "it007");
        Authorize(client, customer.AccessToken);
        var addressId = await CreateAddressAsync(client);

        // API giỏ hàng chặn ngay khi thêm số lượng vượt tồn kho, nên đưa dòng giỏ (số lượng 2) vào trực tiếp qua DbContext
        // để tái hiện đúng kịch bản "tồn 1, giỏ 2" (vd: tồn kho giảm sau khi khách đã bỏ vào giỏ).
        var addViaApi = await client.PostAsJsonAsync("/api/cart/items", new { variantId, quantity = 2 });
        var addViaApiBody = await ReadJsonAsync(addViaApi);
        Log($"(Ghi chú) POST /api/cart/items số lượng 2 khi tồn 1 -> {(int)addViaApi.StatusCode}, \"{addViaApiBody.GetProperty("message").GetString()}\" => chèn giỏ trực tiếp qua DbContext");

        await using (var db = _factory.CreateDbContext())
        {
            db.CartItems.Add(new ClothingAPI.Models.CartItem
            {
                UserId = customer.UserId,
                VariantId = variantId,
                Quantity = 2,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var ordersBefore = await WithDbAsync(_factory, db => db.Orders.CountAsync());
        Log($"Chuẩn bị: biến thể #{variantId} tồn 1, giỏ 2; tổng số đơn trước = {ordersBefore}");

        var response = await PostOrderAsync(client, addressId, null);
        var body = await ReadJsonAsync(response);
        var message = body.GetProperty("message").GetString();
        Log($"POST /api/orders -> {(int)response.StatusCode} {response.StatusCode}, message = \"{message}\"");

        var ordersAfter = await WithDbAsync(_factory, db => db.Orders.CountAsync());
        var stockAfter = await WithDbAsync(_factory, db => db.ProductVariants.Where(v => v.VariantId == variantId).Select(v => v.StockQuantity).SingleAsync());
        var cartQuantity = await WithDbAsync(_factory, db => db.CartItems.Where(c => c.UserId == customer.UserId).SumAsync(c => c.Quantity));
        Log($"Sau: số đơn {ordersBefore} -> {ordersAfter}; tồn {stockAfter}; giỏ còn số lượng {cartQuantity}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("không đủ tồn kho", message);
        Assert.Equal(ordersBefore, ordersAfter);
        Assert.Equal(1, stockAfter);
        Assert.Equal(2, cartQuantity);
    }

    [Fact]
    public async Task IT008_CreateOrder_ExpiredCoupon_IsRejected()
    {
        var variantId = await CreateVariantAsync(_factory, stockQuantity: 10);
        var couponCode = await CreateCouponAsync(_factory, expired: true);

        var client = _factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "it008");
        Authorize(client, customer.AccessToken);
        var addressId = await CreateAddressAsync(client);
        await AddToCartAsync(client, variantId, 1);

        var ordersBefore = await WithDbAsync(_factory, db => db.Orders.CountAsync());
        Log($"Chuẩn bị: mã {couponCode} hết hạn hôm qua; giỏ 1 sản phẩm; tổng số đơn trước = {ordersBefore}");

        var response = await PostOrderAsync(client, addressId, couponCode);
        var body = await ReadJsonAsync(response);
        var message = body.GetProperty("message").GetString();
        Log($"POST /api/orders kèm mã hết hạn -> {(int)response.StatusCode} {response.StatusCode}, message = \"{message}\"");

        var ordersAfter = await WithDbAsync(_factory, db => db.Orders.CountAsync());
        var stockAfter = await WithDbAsync(_factory, db => db.ProductVariants.Where(v => v.VariantId == variantId).Select(v => v.StockQuantity).SingleAsync());
        Log($"Sau: số đơn {ordersBefore} -> {ordersAfter}; tồn {stockAfter}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Mã giảm giá đã hết hạn.", message);
        Assert.Equal(ordersBefore, ordersAfter);
    }

    [Fact]
    public async Task IT009_CustomerCancelsPendingOrder_RestoresStockAndCoupon()
    {
        var variantId = await CreateVariantAsync(_factory, stockQuantity: 10);
        var couponCode = await CreateCouponAsync(_factory, expired: false);

        var client = _factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "it009");
        Authorize(client, customer.AccessToken);
        var addressId = await CreateAddressAsync(client);
        await AddToCartAsync(client, variantId, 2);

        var orderId = await CreateOrderAsync(client, addressId, couponCode);
        var stockAfterOrder = await WithDbAsync(_factory, db => db.ProductVariants.Where(v => v.VariantId == variantId).Select(v => v.StockQuantity).SingleAsync());
        var usedAfterOrder = await WithDbAsync(_factory, db => db.Coupons.Where(c => c.Code == couponCode).Select(c => c.UsedCount).SingleAsync());
        Log($"Tạo đơn #{orderId} dùng mã {couponCode}: tồn 10 -> {stockAfterOrder}, UsedCount = {usedAfterOrder}");

        // Endpoint thật của ứng dụng là PUT /api/orders/{id}/cancel (không phải POST).
        var response = await client.PutAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "Đổi ý" });
        var body = await ReadJsonAsync(response);
        Log($"PUT /api/orders/{orderId}/cancel -> {(int)response.StatusCode} {response.StatusCode}, message = \"{body.GetProperty("message").GetString()}\"");

        var status = await WithDbAsync(_factory, db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.OrderStatus).SingleAsync());
        var stockAfterCancel = await WithDbAsync(_factory, db => db.ProductVariants.Where(v => v.VariantId == variantId).Select(v => v.StockQuantity).SingleAsync());
        var usedAfterCancel = await WithDbAsync(_factory, db => db.Coupons.Where(c => c.Code == couponCode).Select(c => c.UsedCount).SingleAsync());
        Log($"Sau huỷ: trạng thái {status}; tồn {stockAfterOrder} -> {stockAfterCancel}; UsedCount {usedAfterOrder} -> {usedAfterCancel}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, status);
        Assert.Equal(10, stockAfterCancel);
        Assert.Equal(usedAfterOrder - 1, usedAfterCancel);
    }

    [Fact]
    public async Task IT010_AdminSkipsStatus_PendingToCompleted_Returns400()
    {
        var variantId = await CreateVariantAsync(_factory, stockQuantity: 10);

        var customerClient = _factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(customerClient, "it010");
        Authorize(customerClient, customer.AccessToken);
        var addressId = await CreateAddressAsync(customerClient);
        await AddToCartAsync(customerClient, variantId, 1);
        var orderId = await CreateOrderAsync(customerClient, addressId);
        var statusBefore = await WithDbAsync(_factory, db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.OrderStatus).SingleAsync());
        Log($"Đơn #{orderId} trạng thái {statusBefore}");

        var adminClient = _factory.CreateClient();
        var admin = await CreateAdminAndLoginAsync(_factory, adminClient);
        Authorize(adminClient, admin.AccessToken);

        // Endpoint thật: PUT /api/admin/orders/{id}/status
        var response = await adminClient.PutAsJsonAsync($"/api/admin/orders/{orderId}/status", new { status = "Completed", note = "Nhảy bước" });
        var body = await ReadJsonAsync(response);
        Log($"PUT /api/admin/orders/{orderId}/status (Completed) -> {(int)response.StatusCode} {response.StatusCode}, message = \"{body.GetProperty("message").GetString()}\"");

        var statusAfter = await WithDbAsync(_factory, db => db.Orders.Where(o => o.OrderId == orderId).Select(o => o.OrderStatus).SingleAsync());
        Log($"Trạng thái sau: {statusAfter}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OrderStatus.Pending, statusAfter);
    }
}
