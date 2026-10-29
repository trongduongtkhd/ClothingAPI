using System.Net.Http.Json;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

public class OrderStockRestorationIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    [InlineData("return")]
    public async Task StatusSaveFails_RollsBackStockPaymentCouponAndHistory(string actor)
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "restore-rollback");
        Authorize(client, customer.AccessToken);
        var address = await CreateAddressAsync(client);
        var first = await CreateVariantAsync(factory, 10);
        var second = await CreateVariantAsync(factory, 10);
        var coupon = await CreateCouponAsync(factory, expired: false);
        await AddToCartAsync(client, first, 2);
        await AddToCartAsync(client, second, 3);
        var orderId = await CreateOrderAsync(client, address, coupon);

        using var adminClient = factory.CreateClient();
        var admin = await CreateAdminAndLoginAsync(factory, adminClient);
        Authorize(adminClient, admin.AccessToken);
        if (actor == "return")
        {
            foreach (var status in new[] { "Confirmed", "Shipping", "Completed" })
                (await adminClient.PutAsJsonAsync($"/api/admin/orders/{orderId}/status", new { status }))
                    .EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync($"/api/orders/{orderId}/return-request", new
            {
                reason = "Sản phẩm không phù hợp kích cỡ"
            })).EnsureSuccessStatusCode();
        }

        var histories = await WithDbAsync(factory, db => db.OrderStatusHistories.CountAsync(x => x.OrderId == orderId));
        await using (var command = factory.Connection.CreateCommand())
        {
            // Only integer fixture IDs are interpolated into this disposable SQLite DDL.
            command.CommandText = $"""
                CREATE TRIGGER L02_RejectStatusHistory
                BEFORE INSERT ON OrderStatusHistories
                WHEN NEW.OrderId = {orderId}
                BEGIN
                    SELECT CASE WHEN
                        (SELECT StockQuantity FROM ProductVariants WHERE VariantId = {first}) != 10
                        OR (SELECT StockQuantity FROM ProductVariants WHERE VariantId = {second}) != 10
                        THEN RAISE(ABORT, 'Stock restoration was not applied') END;
                    SELECT RAISE(ABORT, 'L02 test: status history rejected');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var error = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                if (actor == "customer")
                    await scope.ServiceProvider.GetRequiredService<IOrderService>()
                        .CancelAsync(customer.UserId, orderId, new CancelOrderDto { Reason = "Đổi ý" });
                else if (actor == "admin")
                    await scope.ServiceProvider.GetRequiredService<IAdminOrderService>()
                        .UpdateOrderStatusAsync(admin.UserId, orderId, new UpdateOrderStatusDto { Status = OrderStatus.Cancelled });
                else
                    await scope.ServiceProvider.GetRequiredService<IAdminOrderService>()
                        .ApproveReturnAsync(admin.UserId, orderId, new ProcessReturnRequestDto());
            });
            Assert.Contains("L02 test: status history rejected", error.InnerException?.Message);

            await WithDbAsync(factory, async db =>
            {
                Assert.Equal(new[] { 8, 7 }, await db.ProductVariants
                    .Where(x => x.VariantId == first || x.VariantId == second)
                    .OrderBy(x => x.VariantId).Select(x => x.StockQuantity).ToArrayAsync());
                var order = await db.Orders.Include(x => x.Payments).Include(x => x.ReturnRequest)
                    .SingleAsync(x => x.OrderId == orderId);
                Assert.Equal(actor == "return" ? OrderStatus.Completed : OrderStatus.Pending, order.OrderStatus);
                Assert.Equal(actor == "return" ? PaymentStatus.Paid : PaymentStatus.Unpaid, order.PaymentStatus);
                Assert.Equal(order.PaymentStatus, Assert.Single(order.Payments).PaymentStatus);
                if (actor == "return")
                    Assert.Equal(ReturnRequestStatus.Pending, order.ReturnRequest!.Status);
                Assert.Equal(1, await db.Coupons.Where(x => x.Code == coupon).Select(x => x.UsedCount).SingleAsync());
                Assert.Equal(1, await db.CouponUsages.CountAsync(x => x.OrderId == orderId));
                Assert.Equal(histories, await db.OrderStatusHistories.CountAsync(x => x.OrderId == orderId));
                return 0;
            });
        }
        finally
        {
            await using var command = factory.Connection.CreateCommand();
            command.CommandText = "DROP TRIGGER IF EXISTS L02_RejectStatusHistory;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
