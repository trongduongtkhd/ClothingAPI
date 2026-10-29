using System.Net;
using Microsoft.EntityFrameworkCore;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

public class CartDeletionIntegrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task DeleteOneItem_ReturnsRemainingCartWithCorrectTotals_AndPreservesOtherUsers()
    {
        using var owner = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(owner, "cart-delete-owner");
        Authorize(owner, customer.AccessToken);
        var first = await CreateVariantAsync(factory, 10, 100000m);
        var second = await CreateVariantAsync(factory, 10, 150000m);
        var third = await CreateVariantAsync(factory, 10, 200000m);
        await AddToCartAsync(owner, first, 1);
        await AddToCartAsync(owner, second, 3);
        await AddToCartAsync(owner, third, 2);
        await using (var db = factory.CreateDbContext())
        {
            // Existing unavailable lines must remain visible/removable after another line is deleted.
            (await db.ProductVariants.SingleAsync(x => x.VariantId == third)).IsActive = false;
            await db.SaveChangesAsync();
        }

        using var other = factory.CreateClient();
        var otherCustomer = await RegisterAndLoginCustomerAsync(other, "cart-delete-other");
        Authorize(other, otherCustomer.AccessToken);
        await AddToCartAsync(other, first, 2);
        var deletedId = await WithDbAsync(factory, db => db.CartItems
            .Where(x => x.UserId == customer.UserId && x.VariantId == first)
            .Select(x => x.CartItemId).SingleAsync());

        var response = await owner.DeleteAsync($"/api/cart/items/{deletedId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
        var cart = body.GetProperty("data");
        Assert.Equal(System.Text.Json.JsonValueKind.Object, cart.ValueKind);
        var items = cart.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.DoesNotContain(items, x => x.GetProperty("variantId").GetInt32() == first);
        Assert.Contains(items, x => x.GetProperty("variantId").GetInt32() == second && x.GetProperty("quantity").GetInt32() == 3);
        Assert.Contains(items, x => x.GetProperty("variantId").GetInt32() == third && x.GetProperty("quantity").GetInt32() == 2);
        Assert.False(items.Single(x => x.GetProperty("variantId").GetInt32() == third).GetProperty("isAvailable").GetBoolean());
        Assert.Equal(5, cart.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(850000m, cart.GetProperty("subtotal").GetDecimal());
        Assert.Equal(0m, cart.GetProperty("shippingFee").GetDecimal());
        Assert.Equal(2, await WithDbAsync(factory, db => db.CartItems.CountAsync(x => x.UserId == customer.UserId)));
        Assert.Equal(2, await WithDbAsync(factory, db => db.CartItems.Where(x => x.UserId == otherCustomer.UserId).SumAsync(x => x.Quantity)));
        Assert.Equal(10, await WithDbAsync(factory, db => db.ProductVariants.Where(x => x.VariantId == first).Select(x => x.StockQuantity).SingleAsync()));

        var reloaded = (await ReadJsonAsync(await owner.GetAsync("/api/cart"))).GetProperty("data");
        Assert.Equal(5, reloaded.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(850000m, reloaded.GetProperty("subtotal").GetDecimal());
        Assert.Equal(2, reloaded.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task DeleteLastItem_ReturnsAnEmptyCartObject_InsteadOfNull()
    {
        using var client = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(client, "cart-delete-last");
        Authorize(client, customer.AccessToken);
        var variant = await CreateVariantAsync(factory, 10);
        await AddToCartAsync(client, variant, 1);
        var id = await WithDbAsync(factory, db => db.CartItems.Where(x => x.UserId == customer.UserId).Select(x => x.CartItemId).SingleAsync());
        var response = await client.DeleteAsync($"/api/cart/items/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cart = (await ReadJsonAsync(response)).GetProperty("data");
        Assert.Equal(0, cart.GetProperty("items").GetArrayLength());
        Assert.Equal(0, cart.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(0m, cart.GetProperty("subtotal").GetDecimal());
        Assert.Equal(0, await WithDbAsync(factory, db => db.CartItems.CountAsync(x => x.UserId == customer.UserId)));
    }

    [Fact]
    public async Task DeleteAnotherCustomersItem_Returns404_AndPreservesBothCarts()
    {
        using var owner = factory.CreateClient();
        var customer = await RegisterAndLoginCustomerAsync(owner, "cart-delete-protected");
        Authorize(owner, customer.AccessToken);
        var variant = await CreateVariantAsync(factory, 10);
        await AddToCartAsync(owner, variant, 2);
        var id = await WithDbAsync(factory, db => db.CartItems.Where(x => x.UserId == customer.UserId).Select(x => x.CartItemId).SingleAsync());
        using var outsider = factory.CreateClient();
        var other = await RegisterAndLoginCustomerAsync(outsider, "cart-delete-outsider");
        Authorize(outsider, other.AccessToken);
        await AddToCartAsync(outsider, variant, 1);

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync($"/api/cart/items/{id}")).StatusCode);
        Assert.Equal(2, await WithDbAsync(factory, db => db.CartItems.Where(x => x.UserId == customer.UserId).SumAsync(x => x.Quantity)));
        Assert.Equal(1, await WithDbAsync(factory, db => db.CartItems.Where(x => x.UserId == other.UserId).SumAsync(x => x.Quantity)));
    }
}
