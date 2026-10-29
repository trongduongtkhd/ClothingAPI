using ClothingAPI.Data;
using ClothingAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Helpers;

public static class CouponUsageCounter
{
    public static async Task LockAsync(AppDbContext db, int couponId)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction is required.");
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT CouponId FROM Coupons WITH (UPDLOCK, HOLDLOCK) WHERE CouponId = {couponId};");
        var tracked = db.ChangeTracker.Entries<Coupon>().FirstOrDefault(x => x.Entity.CouponId == couponId);
        if (tracked is not null) await tracked.ReloadAsync();
    }

    public static async Task IncrementAsync(AppDbContext db, int couponId)
    {
        await db.Coupons.Where(x => x.CouponId == couponId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedCount, x => x.UsedCount + 1));
        await RefreshAsync(db, couponId);
    }

    // Call before locking stock, in the same order in every cancel/expiry path.
    public static async Task ReleaseAsync(AppDbContext db, IEnumerable<CouponUsage> usages)
    {
        foreach (var group in usages.GroupBy(x => x.CouponId).OrderBy(x => x.Key))
        {
            await LockAsync(db, group.Key);
            var quantity = group.Count();
            await db.Coupons.Where(x => x.CouponId == group.Key).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.UsedCount, x => x.UsedCount >= quantity ? x.UsedCount - quantity : 0));
            await RefreshAsync(db, group.Key);
        }
    }

    private static async Task RefreshAsync(AppDbContext db, int id)
    {
        var tracked = db.ChangeTracker.Entries<Coupon>().FirstOrDefault(x => x.Entity.CouponId == id);
        if (tracked is not null) await tracked.ReloadAsync();
    }
}
