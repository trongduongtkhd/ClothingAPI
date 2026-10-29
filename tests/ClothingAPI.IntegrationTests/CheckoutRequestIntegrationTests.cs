using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

public class CheckoutRequestIntegrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_ReturnsSameOrderAndDoesNotConsumeStockOrCouponAgain(bool buyNow)
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "checkout-replay");
        Authorize(client, customer.AccessToken);
        var addressId = await CreateAddressAsync(client);
        var variantId = await CreateVariantAsync(factory, 10);
        var couponCode = await CreateCouponAsync(factory, false);
        if (!buyNow) await AddToCartAsync(client, variantId, 2);
        var requestId = Guid.NewGuid();
        object? buyNowItem = buyNow ? new { variantId, quantity = 2 } : null;
        var payload = new { requestId, addressId, paymentMethod = "COD", couponCode, buyNowItem };
        var first = await client.PostAsJsonAsync("/api/orders", payload);
        var replay = await client.PostAsJsonAsync("/api/orders", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var id = (await ReadJsonAsync(first)).GetProperty("data").GetProperty("orderId").GetInt32();
        Assert.Equal(id, (await ReadJsonAsync(replay)).GetProperty("data").GetProperty("orderId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/orders/by-request/{requestId}")).StatusCode);
        await WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Orders.CountAsync(x => x.UserId == customer.UserId));
            Assert.Equal(8, await db.ProductVariants.Where(x => x.VariantId == variantId).Select(x => x.StockQuantity).SingleAsync());
            Assert.Equal(1, await db.CouponUsages.CountAsync(x => x.UserId == customer.UserId));
            Assert.Equal(1, await db.Coupons.Where(x => x.Code == couponCode).Select(x => x.UsedCount).SingleAsync());
            return 0;
        });
        var conflict = await client.PostAsJsonAsync("/api/orders", new
            { requestId, addressId, paymentMethod = "COD", couponCode, buyNowItem, note = "Changed payload" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task RequestId_IsScopedToCustomer_AndNewIntentCanBuySameItem()
    {
        using var a = factory.CreateClient(); using var b = factory.CreateClient();
        var ca = await RegisterAndLoginCustomerAsync(a, "checkout-user-a");
        var cb = await RegisterAndLoginCustomerAsync(b, "checkout-user-b");
        Authorize(a, ca.AccessToken); Authorize(b, cb.AccessToken);
        var aa = await CreateAddressAsync(a); var ab = await CreateAddressAsync(b);
        var variantId = await CreateVariantAsync(factory, 10); var requestId = Guid.NewGuid();
        (await a.PostAsJsonAsync("/api/orders", new { requestId, addressId=aa, paymentMethod="COD", buyNowItem=new { variantId, quantity=1 } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/orders/by-request/{requestId}")).StatusCode);
        (await b.PostAsJsonAsync("/api/orders", new { requestId, addressId=ab, paymentMethod="COD", buyNowItem=new { variantId, quantity=1 } })).EnsureSuccessStatusCode();
        (await a.PostAsJsonAsync("/api/orders", new { requestId=Guid.NewGuid(), addressId=aa, paymentMethod="COD", buyNowItem=new { variantId, quantity=1 } })).EnsureSuccessStatusCode();
        Assert.Equal(7, await WithDbAsync(factory, db=>db.ProductVariants.Where(x=>x.VariantId==variantId).Select(x=>x.StockQuantity).SingleAsync()));
    }

    [Fact]
    public async Task FailedRequest_RollsBackKeyAndCanBeRetried()
    {
        using var client = factory.CreateClient(); var customer=await RegisterAndLoginCustomerAsync(client,"checkout-retry-failure");
        Authorize(client,customer.AccessToken); var addressId=await CreateAddressAsync(client);
        var variantId=await CreateVariantAsync(factory,0);
        var payload=new { requestId=Guid.NewGuid(),addressId,paymentMethod="COD",buyNowItem=new {variantId,quantity=1} };
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/orders",payload)).StatusCode);
        await WithDbAsync(factory,async db=>{await db.ProductVariants.Where(x=>x.VariantId==variantId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.StockQuantity,1));return 0;});
        Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/orders",payload)).StatusCode);
        Assert.Equal(1,await WithDbAsync(factory,db=>db.Orders.CountAsync(x=>x.UserId==customer.UserId)));
    }

    [Fact]
    public async Task MissingOrEmptyRequestId_IsRejected()
    {
        using var client=factory.CreateClient();var customer=await RegisterAndLoginCustomerAsync(client,"checkout-invalid-key");
        Authorize(client,customer.AccessToken);var addressId=await CreateAddressAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/orders",new {addressId,paymentMethod="COD"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/orders",new {requestId=Guid.Empty,addressId,paymentMethod="COD"})).StatusCode);
        Assert.Equal(0,await WithDbAsync(factory,db=>db.Orders.CountAsync(x=>x.UserId==customer.UserId)));
    }
}
