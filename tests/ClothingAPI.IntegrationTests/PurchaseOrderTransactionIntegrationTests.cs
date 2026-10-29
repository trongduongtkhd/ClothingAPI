using ClothingAPI.DTOs.PurchaseOrders;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ClothingAPI.IntegrationTests.TestHelpers;

namespace ClothingAPI.IntegrationTests;

public class PurchaseOrderTransactionIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("cancel")]
    public async Task SaveFails_RollsBackStockCostReceiptAndItems(string operation)
    {
        using var client = factory.CreateClient();
        var admin = await CreateAdminAndLoginAsync(factory, client);
        var supplier = await WithDbAsync(factory, async db =>
        {
            var entity = new Supplier { SupplierName = $"Rollback {Guid.NewGuid():N}" };
            db.Suppliers.Add(entity);
            await db.SaveChangesAsync();
            return entity.SupplierId;
        });
        var first = await CreateVariantAsync(factory, 10);
        var second = await CreateVariantAsync(factory, 10);
        await WithDbAsync(factory, async db =>
        {
            await db.ProductVariants.Where(x => x.VariantId == first || x.VariantId == second)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AverageCostPrice, 100000m));
            return 0;
        });
        CreatePurchaseOrderDto Dto(int q, decimal cost) => new()
        {
            SupplierId = supplier,
            Items = new[] { first, second }.Select(id => new CreatePurchaseOrderItemDto
                { VariantId = id, Quantity = q, UnitCost = cost }).ToList()
        };
        var receiptId = 0;
        if (operation != "create")
        {
            await using var seedScope = factory.Services.CreateAsyncScope();
            receiptId = (await seedScope.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateAsync(admin.UserId, Dto(2, 100000m))).PurchaseOrderId;
        }
        await using (var command = factory.Connection.CreateCommand())
        {
            // This DDL uses only integer fixture IDs in an in-memory test database.
            command.CommandText = operation == "cancel"
                ? $"""
                  CREATE TRIGGER L03_RejectReceiptSave BEFORE UPDATE ON PurchaseOrders
                  WHEN OLD.PurchaseOrderId = {receiptId}
                  BEGIN SELECT RAISE(ABORT, 'L03 test: receipt save rejected'); END;
                  """
                : $"""
                  CREATE TRIGGER L03_RejectReceiptSave BEFORE INSERT ON PurchaseOrderItems
                  WHEN NEW.VariantId = {second}
                  BEGIN SELECT RAISE(ABORT, 'L03 test: receipt save rejected'); END;
                  """;
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            var error = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                if (operation == "create") await service.CreateAsync(admin.UserId, Dto(5, 200000m));
                else if (operation == "update") await service.UpdateAsync(receiptId, Dto(5, 200000m));
                else await service.CancelAsync(receiptId, new CancelPurchaseOrderDto { Reason = "Đổi ý" });
            });
            Assert.Contains("L03 test: receipt save rejected", error.InnerException?.Message);
            await WithDbAsync(factory, async db =>
            {
                var variants = await db.ProductVariants.Where(x => x.VariantId == first || x.VariantId == second)
                    .ToListAsync();
                Assert.All(variants, v =>
                {
                    Assert.Equal(operation == "create" ? 10 : 12, v.StockQuantity);
                    Assert.Equal(100000m, v.AverageCostPrice);
                });
                var receipts = await db.PurchaseOrders.Include(x => x.Items)
                    .Where(x => x.CreatedByUserId == admin.UserId).ToListAsync();
                if (operation == "create") Assert.Empty(receipts);
                else
                {
                    var receipt = Assert.Single(receipts);
                    Assert.Equal(PurchaseOrderStatuses.Completed, receipt.Status);
                    Assert.Equal(400000m, receipt.TotalAmount);
                    Assert.Equal(2, receipt.Items.Count);
                    Assert.All(receipt.Items, i => { Assert.Equal(2, i.Quantity); Assert.Equal(100000m, i.UnitCost); });
                }
                return 0;
            });
        }
        finally
        {
            await using var command = factory.Connection.CreateCommand();
            command.CommandText = "DROP TRIGGER IF EXISTS L03_RejectReceiptSave;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
