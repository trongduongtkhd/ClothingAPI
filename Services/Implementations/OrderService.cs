using ClothingAPI.Data;
using ClothingAPI.DTOs.Coupons;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class OrderService : IOrderService
{
    private const decimal StandardShippingFee = 30000m;
    private const decimal FreeShippingThreshold = 500000m;

    private readonly AppDbContext _context;
    private readonly ICouponService _couponService;

    public OrderService(AppDbContext context, ICouponService couponService)
    {
        _context = context;
        _couponService = couponService;
    }

    public async Task<OrderDetailDto> CreateAsync(int userId, CreateOrderDto dto)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();


        var address = await _context.Addresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AddressId == dto.AddressId && x.UserId == userId);

        if (address is null)
        {
            throw new NotFoundException("Không tìm thấy địa chỉ giao hàng.");
        }

        var cartItems = await _context.CartItems
            .Include(x => x.Variant)
                .ThenInclude(x => x.Product)
            .Include(x => x.Variant)
                .ThenInclude(x => x.Color)
            .Include(x => x.Variant)
                .ThenInclude(x => x.Size)
            .Where(x => x.UserId == userId)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            throw new BadRequestException("Giỏ hàng đang trống.");
        }

        decimal subtotal = 0;

        foreach (var cartItem in cartItems)
        {
            var variant = cartItem.Variant;

            if (!variant.IsActive || !variant.Product.IsActive)
            {
                throw new BadRequestException(
                    $"Sản phẩm '{variant.Product.ProductName}' hiện không còn được bán."
                );
            }

            if (variant.StockQuantity < cartItem.Quantity)
            {
                throw new BadRequestException(
                    $"Sản phẩm '{variant.Product.ProductName}' không đủ tồn kho."
                );
            }

            var unitPrice = variant.SalePrice ?? variant.Price;

            subtotal += unitPrice * cartItem.Quantity;
        }

        CouponValidationDto? couponValidation = null;
        Coupon? coupon = null;

        if (!string.IsNullOrWhiteSpace(dto.CouponCode))
        {
            couponValidation = await _couponService.ValidateForCartAsync(
                userId,
                new ValidateCouponDto
                {
                    Code = dto.CouponCode
                }
            );

            coupon = await _context.Coupons
                .FirstOrDefaultAsync(x =>
                    x.CouponId == couponValidation.CouponId);

            if (coupon is null)
            {
                throw new BadRequestException(
                    "Mã giảm giá không còn tồn tại."
                );
            }
        }

        var shippingFee = CalculateShippingFee(subtotal);
        var discountAmount = couponValidation?.DiscountAmount ?? 0m;
        var totalAmount = subtotal - discountAmount + shippingFee;

        var order = new Order
        {
            OrderCode = await GenerateOrderCodeAsync(),
            UserId = userId,
            ReceiverName = address.ReceiverName,
            ReceiverPhone = address.ReceiverPhone,
            ShippingAddress = BuildShippingAddress(address),
            Note = NormalizeNullableText(dto.Note),
            Subtotal = subtotal,
            ShippingFee = shippingFee,
            DiscountAmount = discountAmount,
            TotalAmount = totalAmount,
            CouponId = coupon?.CouponId,
            CouponCode = coupon?.Code,
            OrderStatus = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var cartItem in cartItems)
        {
            var variant = cartItem.Variant;
            var unitPrice = variant.SalePrice ?? variant.Price;

            order.OrderItems.Add(new OrderItem
            {
                VariantId = variant.VariantId,
                ProductName = variant.Product.ProductName,
                SKU = variant.SKU,
                ColorName = variant.Color.ColorName,
                SizeName = variant.Size.SizeName,
                UnitPrice = unitPrice,
                Quantity = cartItem.Quantity,
                TotalPrice = unitPrice * cartItem.Quantity
            });

            variant.StockQuantity -= cartItem.Quantity;
        }

        order.Payments.Add(new Payment
        {
            PaymentMethod = dto.PaymentMethod,
            Amount = totalAmount,
            PaymentStatus = PaymentStatus.Unpaid,
            CreatedAt = DateTime.UtcNow
        });

        order.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Pending,
            Note = "Khách hàng đã tạo đơn hàng.",
            ChangedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        _context.Orders.Add(order);

        if (coupon is not null)
        {
            coupon.UsedCount += 1;

            order.CouponUsages.Add(new CouponUsage
            {
                CouponId = coupon.CouponId,
                UserId = userId,
                DiscountAmount = discountAmount,
                UsedAt = DateTime.UtcNow
            });
        }

        _context.CartItems.RemoveRange(cartItems);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetMyOrderByIdAsync(userId, order.OrderId);
    }

    public async Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(int userId, OrderQueryDto query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100);

        var orders = _context.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.UserId == userId)
            .AsQueryable();

        if (query.Status.HasValue)
        {
            orders = orders.Where(x => x.OrderStatus == query.Status.Value);
        }

        var totalItems = await orders.CountAsync();

        var orderList = await orders
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<OrderSummaryDto>
        {
            Items = orderList.Select(x => new OrderSummaryDto
            {
                OrderId = x.OrderId,
                OrderCode = x.OrderCode,
                TotalAmount = x.TotalAmount,
                OrderStatus = x.OrderStatus,
                PaymentStatus = x.PaymentStatus,
                CreatedAt = x.CreatedAt,
                TotalItems = x.OrderItems.Sum(item => item.Quantity)
            }).ToList(),

            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<OrderDetailDto> GetMyOrderByIdAsync(int userId, int orderId)
    {
        var order = await GetOrderWithDetailsAsync(orderId);

        if (order.UserId != userId)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        return MapOrderDetail(order);
    }

    public async Task CancelAsync(int userId, int orderId, CancelOrderDto dto)

    {
        await using var transaction = await _context.Database.BeginTransactionAsync();


        var order = await _context.Orders
            .Include(x => x.OrderItems)
                .ThenInclude(x => x.Variant)
            .Include(x => x.Payments)
            .Include(x => x.CouponUsages)
            .FirstOrDefaultAsync(x =>
                x.OrderId == orderId &&
                x.UserId == userId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        if (order.OrderStatus != OrderStatus.Pending)
        {
            throw new BadRequestException(
                "Chỉ có thể hủy đơn hàng đang chờ xác nhận."
            );
        }

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new BadRequestException(
                "Đơn hàng đã thanh toán không thể hủy trực tiếp."
            );
        }

        foreach (var orderItem in order.OrderItems)
        {
            orderItem.Variant.StockQuantity += orderItem.Quantity;
        }

        if (order.CouponUsages.Count > 0)
        {
            var couponUsage = order.CouponUsages.First();

            var coupon = await _context.Coupons
                .FirstOrDefaultAsync(x =>
                    x.CouponId == couponUsage.CouponId);

            if (coupon is not null && coupon.UsedCount > 0)
            {
                coupon.UsedCount -= 1;
            }

            _context.CouponUsages.Remove(couponUsage);
        }

        order.OrderStatus = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.OrderId,
            Status = OrderStatus.Cancelled,
            Note = string.IsNullOrWhiteSpace(dto.Reason) ? "Khách hàng đã hủy đơn hàng." : dto.Reason.Trim(),
            ChangedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
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

    private async Task<string> GenerateOrderCodeAsync()
    {
        string orderCode;

        do
        {
            orderCode = $"DH{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 1000)}";

        }
        while (await _context.Orders.AnyAsync(x => x.OrderCode == orderCode));
        return orderCode;
    }

    private static decimal CalculateShippingFee(decimal subtotal)
    {
        return subtotal >= FreeShippingThreshold ? 0m : StandardShippingFee;
    }

    private static string BuildShippingAddress(Address address)
    {
        var parts = new[]
        {
            address.AddressDetail,
            address.Ward,
            address.District,
            address.Province
        };

        return string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));

    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
    public async Task<MockQrDto> GetMockQrAsync(int userId, int orderId)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x =>
                x.OrderId == orderId &&
                x.UserId == userId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        var payment = order.Payments.FirstOrDefault();

        if (payment is null || payment.PaymentMethod != PaymentMethod.MockQR)
        {
            throw new BadRequestException("Đơn hàng này không sử dụng phương thức thanh toán QR.");
        }

        return new MockQrDto
        {
            OrderCode = order.OrderCode,
            Amount = order.TotalAmount,
            TransferContent = order.OrderCode
        };
    }

    public async Task<PaymentDto> ConfirmMockQrAsync(int userId, int orderId)
    {
        var order = await _context.Orders
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x =>
                x.OrderId == orderId &&
                x.UserId == userId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            throw new BadRequestException("Không thể thanh toán cho đơn hàng đã bị hủy.");
        }

        var payment = order.Payments.FirstOrDefault();

        if (payment is null || payment.PaymentMethod != PaymentMethod.MockQR)
        {
            throw new BadRequestException(
                "Đơn hàng này không sử dụng phương thức thanh toán QR."
            );
        }

        if (payment.PaymentStatus == PaymentStatus.Paid)
        {
            throw new BadRequestException("Đơn hàng đã được thanh toán.");
        }

        payment.PaymentStatus = PaymentStatus.Paid;
        payment.PaidAt = DateTime.UtcNow;
        payment.TransactionCode = $"MOCKQR-{DateTime.UtcNow:yyyyMMdd}-{order.OrderId:D6}";

        order.PaymentStatus = PaymentStatus.Paid;
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
}