using ClothingAPI.Data;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Helpers;

public static class OrderStockRestorer
{
    public static async Task RestoreAsync(AppDbContext db, IEnumerable<OrderItem> items)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Order stock restoration requires a transaction.");

        // Different orders may return the same variant. Add to the current database
        // value, never write a stock total calculated from an earlier entity snapshot.
        foreach (var group in items.GroupBy(x => x.VariantId).OrderBy(x => x.Key))
        {
            var total = group.Sum(x => (long)x.Quantity);
            if (group.Any(x => x.Quantity <= 0) || total > int.MaxValue)
                throw new BadRequestException("Số lượng hoàn kho không hợp lệ.");

            var variantId = group.Key;
            var quantity = (int)total;
            var affected = await db.ProductVariants
                .Where(x => x.VariantId == variantId && x.StockQuantity <= int.MaxValue - quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    x => x.StockQuantity, x => x.StockQuantity + quantity));

            if (affected != 1)
                throw new BadRequestException("Không thể hoàn kho: biến thể không tồn tại hoặc tồn kho vượt giới hạn.");

            // Keep any already-loaded variant consistent without changing price/cost.
            var tracked = db.ChangeTracker.Entries<ProductVariant>()
                .FirstOrDefault(x => x.Entity.VariantId == variantId);
            if (tracked is not null)
            {
                var remaining = await db.ProductVariants.Where(x => x.VariantId == variantId)
                    .Select(x => x.StockQuantity).SingleAsync();
                var stock = tracked.Property(x => x.StockQuantity);
                stock.CurrentValue = remaining;
                stock.OriginalValue = remaining;
                stock.IsModified = false;
            }
        }
    }
}
