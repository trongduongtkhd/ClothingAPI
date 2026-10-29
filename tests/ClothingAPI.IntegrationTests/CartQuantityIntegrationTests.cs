using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

public class CartQuantityIntegrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(100, false)]
    public async Task AddExistingVariant_EnforcesLimitOnAccumulatedQuantity(int added, bool accepted)
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "cart-quantity-limit");
        Authorize(client, customer.AccessToken);
        var variant = await CreateVariantAsync(factory, 200, 100000m);
        await AddToCartAsync(client, variant, 99);

        var response = await client.PostAsJsonAsync("/api/cart/items", new { variantId = variant, quantity = added });
        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        if (!accepted)
            Assert.Equal("Số lượng phải từ 1 đến 100.", body.GetProperty("message").GetString());

        var expected = accepted ? 100 : 99;
        var cart = (await ReadJsonAsync(await client.GetAsync("/api/cart"))).GetProperty("data");
        Assert.Equal(1, cart.GetProperty("items").GetArrayLength());
        Assert.Equal(expected, cart.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(expected * 100000m, cart.GetProperty("subtotal").GetDecimal());
        Assert.Equal(expected, await WithDbAsync(factory, db => db.CartItems
            .Where(x => x.UserId == customer.UserId).Select(x => x.Quantity).SingleAsync()));
        Assert.Equal(200, await WithDbAsync(factory, db => db.ProductVariants
            .Where(x => x.VariantId == variant).Select(x => x.StockQuantity).SingleAsync()));
    }

    [Fact]
    public async Task LimitIsPerVariant_NotTheWholeCart()
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "cart-quantity-lines");
        Authorize(client, customer.AccessToken);
        await AddToCartAsync(client, await CreateVariantAsync(factory, 200), 100);
        await AddToCartAsync(client, await CreateVariantAsync(factory, 200), 100);
        var cart = (await ReadJsonAsync(await client.GetAsync("/api/cart"))).GetProperty("data");
        Assert.Equal(2, cart.GetProperty("items").GetArrayLength());
        Assert.Equal(200, cart.GetProperty("totalQuantity").GetInt32());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StockReducedBelowCartQuantity_CanReduceToAvailableStock(int stock)
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "cart-stock-reduced");
        Authorize(client, customer.AccessToken);
        var variant = await CreateVariantAsync(factory, 10, 100000m);
        await AddToCartAsync(client, variant, 5);
        await using (var db = factory.CreateDbContext())
        {
            (await db.ProductVariants.SingleAsync(x => x.VariantId == variant)).StockQuantity = stock;
            await db.SaveChangesAsync();
        }

        var cart = (await ReadJsonAsync(await client.GetAsync("/api/cart"))).GetProperty("data");
        var item = cart.GetProperty("items")[0];
        var id = item.GetProperty("cartItemId").GetInt32();
        Assert.False(item.GetProperty("isAvailable").GetBoolean());
        Assert.True(item.GetProperty("isForSale").GetBoolean());
        Assert.Equal(stock, item.GetProperty("stockQuantity").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/cart/items/{id}", new { quantity = stock + 1 })).StatusCode);

        var response = await client.PutAsJsonAsync($"/api/cart/items/{id}", new { quantity = stock });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        cart = (await ReadJsonAsync(response)).GetProperty("data");
        Assert.True(cart.GetProperty("items")[0].GetProperty("isAvailable").GetBoolean());
        Assert.Equal(stock, cart.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(stock * 100000m, cart.GetProperty("subtotal").GetDecimal());
        Assert.Equal(stock, await WithDbAsync(factory, db => db.CartItems
            .Where(x => x.UserId == customer.UserId).Select(x => x.Quantity).SingleAsync()));
    }

    [Theory]
    [InlineData("variant")]
    [InlineData("product")]
    [InlineData("stock")]
    public async Task UnavailableItem_CannotBeUpdatedToAFalseValidQuantity(string reason)
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "cart-unavailable");
        Authorize(client, customer.AccessToken);
        var variantId = await CreateVariantAsync(factory, 10);
        await AddToCartAsync(client, variantId, 5);
        await using (var db = factory.CreateDbContext())
        {
            var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.VariantId == variantId);
            variant.StockQuantity = reason == "stock" ? 0 : 2;
            if (reason == "variant") variant.IsActive = false;
            if (reason == "product") variant.Product.IsActive = false;
            await db.SaveChangesAsync();
        }

        var cart = (await ReadJsonAsync(await client.GetAsync("/api/cart"))).GetProperty("data");
        var item = cart.GetProperty("items")[0];
        Assert.False(item.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(reason == "stock", item.GetProperty("isForSale").GetBoolean());
        var response = await client.PutAsJsonAsync($"/api/cart/items/{item.GetProperty("cartItemId").GetInt32()}", new { quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(5, await WithDbAsync(factory, db => db.CartItems
            .Where(x => x.UserId == customer.UserId).Select(x => x.Quantity).SingleAsync()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task InvalidRequestQuantity_IsRejectedOnBothAddAndUpdate(int quantity)
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "cart-invalid-quantity");
        Authorize(client, customer.AccessToken);
        var variant = await CreateVariantAsync(factory, 200);
        await AddToCartAsync(client, variant, 99);
        var id = await WithDbAsync(factory, db => db.CartItems
            .Where(x => x.UserId == customer.UserId).Select(x => x.CartItemId).SingleAsync());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/cart/items", new { variantId = variant, quantity })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/cart/items/{id}", new { quantity })).StatusCode);
        Assert.Equal(99, await WithDbAsync(factory, db => db.CartItems
            .Where(x => x.CartItemId == id).Select(x => x.Quantity).SingleAsync()));
    }
}
