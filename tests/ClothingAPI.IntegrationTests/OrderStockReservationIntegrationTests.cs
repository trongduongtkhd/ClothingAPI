using System.Net;
using System.Net.Http.Json;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

// These tests verify transaction rollback. Real concurrent-writer behavior is
// tested separately on SQL Server by order-stock-audit.cjs --l01-only.
public class OrderStockReservationIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task LaterReservationAffectsZeroRows_RollsBackEarlierReservation()
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "stock-reservation-later-line");
        Authorize(client, customer.AccessToken);
        var address = await CreateAddressAsync(client);
        var first = await CreateVariantAsync(factory, 5);
        var second = await CreateVariantAsync(factory, 5);
        // Insert in reverse order; reservations must still use variant ID order.
        await AddToCartAsync(client, second, 2);
        await AddToCartAsync(client, first, 2);

        // Both snapshots pass the initial stock check. Simulate losing the later
        // conditional update, after the earlier variant has already been reserved.
        await ExecuteTestSqlAsync($"""
                CREATE TRIGGER L01_RejectLaterReservation
                BEFORE UPDATE OF StockQuantity ON ProductVariants
                WHEN OLD.VariantId = {second}
                BEGIN
                    SELECT CASE WHEN
                        (SELECT StockQuantity FROM ProductVariants WHERE VariantId = {first}) != 3
                        THEN RAISE(ABORT, 'Earlier reservation was not applied') END;
                    SELECT RAISE(IGNORE);
                END;
                """);
        try
        {
            var response = await client.PostAsJsonAsync("/api/orders", new
            {
                requestId = Guid.NewGuid(),
                addressId = address,
                paymentMethod = "COD"
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await ReadJsonAsync(response);
            Assert.Contains("không đủ tồn kho", body.GetProperty("message").GetString());
            await AssertNoOrderOrStockChanges(customer.UserId, first, second);
        }
        finally
        {
            await ExecuteTestSqlAsync("DROP TRIGGER IF EXISTS L01_RejectLaterReservation;");
        }
    }

    [Fact]
    public async Task OrderSaveFails_RollsBackReservationsCouponAndCart()
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "stock-reservation-save-failure");
        Authorize(client, customer.AccessToken);
        var address = await CreateAddressAsync(client);
        var first = await CreateVariantAsync(factory, 5);
        var second = await CreateVariantAsync(factory, 5);
        var coupon = await CreateCouponAsync(factory, expired: false);
        await AddToCartAsync(client, first, 2);
        await AddToCartAsync(client, second, 2);

        await ExecuteTestSqlAsync($"""
                CREATE TRIGGER L01_RejectOrderSave
                BEFORE INSERT ON Orders
                WHEN NEW.UserId = {customer.UserId}
                BEGIN
                    SELECT CASE WHEN
                        (SELECT StockQuantity FROM ProductVariants WHERE VariantId = {first}) != 3
                        OR (SELECT StockQuantity FROM ProductVariants WHERE VariantId = {second}) != 3
                        THEN RAISE(ABORT, 'Reservations were not applied') END;
                    SELECT RAISE(ABORT, 'L01 test: order insert rejected');
                END;
                """);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(customer.UserId, new CreateOrderDto
            {
                RequestId = Guid.NewGuid(),
                AddressId = address,
                PaymentMethod = PaymentMethod.COD,
                CouponCode = coupon
            }));

            Assert.Contains("L01 test: order insert rejected", error.InnerException?.Message);
            await AssertNoOrderOrStockChanges(customer.UserId, first, second);
            Assert.Equal(0, await WithDbAsync(factory, db => db.Coupons
                .Where(x => x.Code == coupon).Select(x => x.UsedCount).SingleAsync()));
            Assert.Equal(0, await WithDbAsync(factory, db => db.CouponUsages
                .CountAsync(x => x.UserId == customer.UserId)));
        }
        finally
        {
            await ExecuteTestSqlAsync("DROP TRIGGER IF EXISTS L01_RejectOrderSave;");
        }
    }

    private async Task AssertNoOrderOrStockChanges(int userId, int first, int second)
    {
        await WithDbAsync(factory, async db =>
        {
            Assert.Equal(new[] { 5, 5 }, await db.ProductVariants
                .Where(x => x.VariantId == first || x.VariantId == second)
                .OrderBy(x => x.VariantId).Select(x => x.StockQuantity).ToArrayAsync());
            Assert.Equal(0, await db.Orders.CountAsync(x => x.UserId == userId));
            Assert.Equal(new[] { 2, 2 }, await db.CartItems.Where(x => x.UserId == userId)
                .OrderBy(x => x.VariantId).Select(x => x.Quantity).ToArrayAsync());
            return 0;
        });
    }

    [Fact]
    public async Task CostChangesAtReservation_OrderUsesCurrentCostWithoutChangingSalePrice()
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "reserve-current-cost");
        Authorize(client, customer.AccessToken);
        var address = await CreateAddressAsync(client);
        var variant = await CreateVariantAsync(factory, 10, 300000m);
        await WithDbAsync(factory, async db =>
        {
            await db.ProductVariants.Where(x => x.VariantId == variant)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AverageCostPrice, 100000m));
            return 0;
        });
        // Simulate a receipt changing cost after the initial read, before reservation.
        await ExecuteTestSqlAsync($"""
            CREATE TRIGGER L03_ChangeCostAtReservation BEFORE UPDATE OF StockQuantity ON ProductVariants
            WHEN OLD.VariantId = {variant}
            BEGIN UPDATE ProductVariants SET AverageCostPrice = 200000 WHERE VariantId = {variant}; END;
            """);
        try
        {
            var response = await client.PostAsJsonAsync("/api/orders", new
            {
                requestId = Guid.NewGuid(),
                addressId = address,
                paymentMethod = "COD",
                buyNowItem = new { variantId = variant, quantity = 1 }
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            await WithDbAsync(factory, async db =>
            {
                var line = await db.OrderItems.SingleAsync(x => x.Order.UserId == customer.UserId);
                Assert.Equal(200000m, line.UnitCost);
                Assert.Equal(300000m, line.UnitPrice);
                Assert.Equal(9, await db.ProductVariants.Where(x => x.VariantId == variant).Select(x => x.StockQuantity).SingleAsync());
                Assert.Equal(200000m, await db.ProductVariants.Where(x => x.VariantId == variant).Select(x => x.AverageCostPrice).SingleAsync());
                return 0;
            });
        }
        finally { await ExecuteTestSqlAsync("DROP TRIGGER IF EXISTS L03_ChangeCostAtReservation;"); }
    }

    private async Task ExecuteTestSqlAsync(string sql)
    {
        // Trigger definitions cannot use SQL parameters; interpolated values above
        // are integer fixture IDs. This connection is the disposable test database.
        await using var command = factory.Connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
