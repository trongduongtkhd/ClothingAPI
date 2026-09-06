using ClothingAPI.Data;
using ClothingAPI.DTOs.Dashboard;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class AdminDashboardService : IAdminDashboardService
{
    private const int LowStockThreshold = 5;

    private readonly AppDbContext _context;

    public AdminDashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardDto> GetAsync(DashboardQueryDto query)
    {
        if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value > query.ToDate.Value)
        {
            throw new BadRequestException("Ngày bắt đầu không được lớn hơn ngày kết thúc.");

        }

        var orders = _context.Orders
            .AsNoTracking()
            .Include(x => x.User)
            .AsQueryable();

        if (query.FromDate.HasValue)
        {
            orders = orders.Where(x => x.CreatedAt >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            var endDateExclusive = query.ToDate.Value.Date.AddDays(1);

            orders = orders.Where(x => x.CreatedAt < endDateExclusive);

        }

        var completedOrders = orders.Where(x => x.OrderStatus == OrderStatus.Completed);

        var totalRevenue = await completedOrders.SumAsync(x => (decimal?)x.TotalAmount) ?? 0m;

        var totalOrders = await orders.CountAsync();

        var pendingOrders = await orders.CountAsync(x => x.OrderStatus == OrderStatus.Pending);


        var totalCustomers = await _context.Users
            .AsNoTracking()
            .CountAsync(x => x.UserRoles.Any(userRole => userRole.Role.RoleName == "Customer"));



        var lowStockVariants = await _context.ProductVariants
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Color)
            .Include(x => x.Size)
            .Where(x =>
                x.IsActive &&
                x.Product.IsActive &&
                x.StockQuantity <= LowStockThreshold)
            .OrderBy(x => x.StockQuantity)
            .ThenBy(x => x.Product.ProductName)
            .Take(10)
            .ToListAsync();

        var recentOrders = await orders
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .ToListAsync();

        var bestSellingProducts = await _context.OrderItems
            .AsNoTracking()
            .Where(x => x.Order.OrderStatus == OrderStatus.Completed)
            .GroupBy(x => new
            {
                ProductId = x.Variant.ProductId,
                x.ProductName
            })
            .Select(group => new BestSellingProductDto
            {
                ProductId = group.Key.ProductId,
                ProductName = group.Key.ProductName,
                TotalQuantitySold = group.Sum(x => x.Quantity),
                TotalRevenue = group.Sum(x => x.TotalPrice)
            })
            .OrderByDescending(x => x.TotalQuantitySold)
            .Take(5)
            .ToListAsync();

        return new DashboardDto
        {
            TotalRevenue = totalRevenue,
            TotalOrders = totalOrders,
            TotalCustomers = totalCustomers,
            PendingOrders = pendingOrders,
            LowStockProducts = lowStockVariants.Count,

            RecentOrders = recentOrders.Select(x => new RecentOrderDto
            {
                OrderId = x.OrderId,
                OrderCode = x.OrderCode,
                CustomerName = x.User.FullName,
                TotalAmount = x.TotalAmount,
                OrderStatus = x.OrderStatus,
                CreatedAt = x.CreatedAt
            }).ToList(),

            BestSellingProducts = bestSellingProducts,

            LowStockVariants = lowStockVariants.Select(x =>
                new LowStockVariantDto
                {
                    VariantId = x.VariantId,
                    ProductId = x.ProductId,
                    ProductName = x.Product.ProductName,
                    SKU = x.SKU,
                    ColorName = x.Color.ColorName,
                    SizeName = x.Size.SizeName,
                    StockQuantity = x.StockQuantity
                }
            ).ToList()
        };
    }
}