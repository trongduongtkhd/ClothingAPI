using ClothingAPI.Data;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class AdminOrderService : IAdminOrderService
{
    private readonly AppDbContext _context;

    public AdminOrderService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AdminOrderSummaryDto>> GetAllAsync(AdminOrderQueryDto query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100);

        var orders = _context.Orders
            .AsNoTracking()
            .Include(x => x.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();

            orders = orders.Where(x =>
                x.OrderCode.ToLower().Contains(keyword) ||
                x.ReceiverName.ToLower().Contains(keyword) ||
                x.User.Email.ToLower().Contains(keyword));
        }

        if (query.Status.HasValue)
        {
            orders = orders.Where(x => x.OrderStatus == query.Status.Value);

        }

        if (query.PaymentStatus.HasValue)
        {
            orders = orders.Where(x => x.PaymentStatus == query.PaymentStatus.Value);
        }

        var totalItems = await orders.CountAsync();

        var orderList = await orders
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<AdminOrderSummaryDto>
        {
            Items = orderList.Select(x => new AdminOrderSummaryDto
            {
                OrderId = x.OrderId,
                OrderCode = x.OrderCode,
                CustomerName = x.User.FullName,
                CustomerEmail = x.User.Email,
                ReceiverName = x.ReceiverName,
                TotalAmount = x.TotalAmount,
                OrderStatus = x.OrderStatus,
                PaymentStatus = x.PaymentStatus,
                CreatedAt = x.CreatedAt
            }).ToList(),

            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<OrderDetailDto> GetByIdAsync(int orderId)
    {
        var order = await GetOrderWithDetailsAsync(orderId);

        return MapOrderDetail(order);
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(int adminUserId, int orderId, UpdateOrderStatusDto dto)

    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var order = await _context.Orders
            .Include(x => x.OrderItems)
                .ThenInclude(x => x.Variant)
            .Include(x => x.CouponUsages)
            .FirstOrDefaultAsync(x => x.OrderId == orderId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        if (!IsValidStatusTransition(order.OrderStatus, dto.Status))
        {
            throw new BadRequestException($"Không thể chuyển trạng thái từ {order.OrderStatus} sang {dto.Status}.");
        }

        if (dto.Status == OrderStatus.Cancelled)
        {
            foreach (var orderItem in order.OrderItems)
            {
                orderItem.Variant.StockQuantity += orderItem.Quantity;
            }

            if (order.CouponUsages.Count > 0)
            {
                var couponUsage = order.CouponUsages.First();

                var coupon = await _context.Coupons.FirstOrDefaultAsync(x => x.CouponId == couponUsage.CouponId);

                if (coupon is not null && coupon.UsedCount > 0)
                {
                    coupon.UsedCount -= 1;
                }

                _context.CouponUsages.Remove(couponUsage);
            }
        }

        order.OrderStatus = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Status = dto.Status,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? $"Admin cập nhật trạng thái thành {dto.Status}." : dto.Note.Trim(),
            ChangedByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(orderId);
    }

    public async Task<PaymentDto> UpdatePaymentStatusAsync(int adminUserId, int orderId, UpdatePaymentStatusDto dto)

    {
        var order = await _context.Orders
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.OrderId == orderId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            throw new BadRequestException(
                "Không thể cập nhật thanh toán cho đơn hàng đã hủy."
            );
        }

        var payment = order.Payments.FirstOrDefault();

        if (payment is null)
        {
            throw new NotFoundException(
                "Không tìm thấy thông tin thanh toán của đơn hàng."
            );
        }

        payment.PaymentStatus = dto.PaymentStatus;

        if (dto.PaymentStatus == PaymentStatus.Paid)
        {
            payment.PaidAt ??= DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(payment.TransactionCode))
            {
                payment.TransactionCode = $"{payment.PaymentMethod}-{order.OrderCode}";
            }
        }

        order.PaymentStatus = dto.PaymentStatus;
        order.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new PaymentDto
        {
            PaymentId = payment.PaymentId,
            PaymentMethod = payment.PaymentMethod,
            Amount = payment.Amount,
            PaymentStatus = payment.PaymentStatus,
            TransactionCode = payment.TransactionCode,
            PaidAt = payment.PaidAt,
            CreatedAt = payment.CreatedAt
        };
    }

    private async Task<Order> GetOrderWithDetailsAsync(int orderId)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Include(x => x.Payments)
            .Include(x => x.OrderStatusHistories)
                .ThenInclude(x => x.ChangedByUser)
            .FirstOrDefaultAsync(x => x.OrderId == orderId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        return order;
    }

    private static bool IsValidStatusTransition(OrderStatus currentStatus, OrderStatus newStatus)
    {
        return (currentStatus, newStatus) switch
        {
            (OrderStatus.Pending, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipping) => true,
            (OrderStatus.Shipping, OrderStatus.Completed) => true,

            (OrderStatus.Pending, OrderStatus.Cancelled) => true,
            (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,

            _ => false
        };
    }

    private static OrderDetailDto MapOrderDetail(Order order)
    {
        return new OrderDetailDto
        {
            OrderId = order.OrderId,
            OrderCode = order.OrderCode,
            ReceiverName = order.ReceiverName,
            ReceiverPhone = order.ReceiverPhone,
            ShippingAddress = order.ShippingAddress,
            Note = order.Note,
            Subtotal = order.Subtotal,
            ShippingFee = order.ShippingFee,
            DiscountAmount = order.DiscountAmount,
            TotalAmount = order.TotalAmount,
            CouponCode = order.CouponCode,
            OrderStatus = order.OrderStatus,
            PaymentStatus = order.PaymentStatus,
            CreatedAt = order.CreatedAt,

            Items = order.OrderItems.Select(x => new OrderItemDto
            {
                OrderItemId = x.OrderItemId,
                VariantId = x.VariantId,
                ProductName = x.ProductName,
                SKU = x.SKU,
                ColorName = x.ColorName,
                SizeName = x.SizeName,
                UnitPrice = x.UnitPrice,
                Quantity = x.Quantity,
                TotalPrice = x.TotalPrice
            }).ToList(),

            Payments = order.Payments.Select(x => new PaymentDto
            {
                PaymentId = x.PaymentId,
                PaymentMethod = x.PaymentMethod,
                Amount = x.Amount,
                PaymentStatus = x.PaymentStatus,
                TransactionCode = x.TransactionCode,
                PaidAt = x.PaidAt,
                CreatedAt = x.CreatedAt
            }).ToList(),

            StatusHistories = order.OrderStatusHistories
                .OrderBy(x => x.CreatedAt)
                .Select(x => new OrderStatusHistoryDto
                {
                    OrderStatusHistoryId = x.OrderStatusHistoryId,
                    Status = x.Status,
                    Note = x.Note,
                    ChangedByName = x.ChangedByUser?.FullName,
                    CreatedAt = x.CreatedAt
                }).ToList()
        };
    }
}