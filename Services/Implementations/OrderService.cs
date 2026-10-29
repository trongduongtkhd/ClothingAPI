using ClothingAPI.Data;
using ClothingAPI.DTOs.Coupons;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClothingAPI.Services.Implementations;

public class OrderService : IOrderService
{
    private readonly AppDbContext _context;
    private readonly ICouponService _couponService;
    private readonly VnpaySettings _vnpay;

    public OrderService(AppDbContext context, ICouponService couponService, IOptions<VnpaySettings>? vnpay = null)
    {
        _context = context;
        _couponService = couponService;
        _vnpay = vnpay?.Value ?? new VnpaySettings();
    }

    public async Task<OrderDetailDto> CreateAsync(int userId, CreateOrderDto dto)
    {
        if (!dto.RequestId.HasValue || dto.RequestId.Value == Guid.Empty)
            throw new BadRequestException("Mã yêu cầu đặt hàng không hợp lệ.");
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            dto.AddressId, dto.PaymentMethod,
            CouponCode = dto.CouponCode?.Trim().ToUpperInvariant() ?? "",
            Note = NormalizeNullableText(dto.Note), dto.BuyNowItem
        }))));
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await TransactionLock.AcquireAsync(_context, $"checkout-{userId}-{dto.RequestId.Value:N}");
        var existing = await _context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.RequestId == dto.RequestId);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
                throw new ConflictException("Mã yêu cầu đã được dùng cho nội dung khác. Vui lòng kiểm tra đơn hàng trước đó.");
            await transaction.CommitAsync();
            return await GetMyOrderByIdAsync(userId, existing.OrderId);
        }
        if (dto.BuyNowItem is null)
            await TransactionLock.AcquireAsync(_context, $"cart-{userId}");

        if (!Enum.IsDefined(dto.PaymentMethod))
            throw new BadRequestException("Phương thức thanh toán không hợp lệ.");
        if (dto.PaymentMethod == PaymentMethod.VnPay && !_vnpay.IsConfigured)
            throw new BadRequestException("VNPAY Sandbox chưa được cấu hình.");
        var address = await _context.Addresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AddressId == dto.AddressId && x.UserId == userId);

        if (address is null)
        {
            throw new NotFoundException("Không tìm thấy địa chỉ giao hàng.");
        }

        // "Mua ngay": chỉ đặt sản phẩm được chọn, giỏ hàng giữ nguyên.
        var cartItems = new List<CartItem>();
        List<(ProductVariant Variant, int Quantity)> lines;

        if (dto.BuyNowItem is not null)
        {
            var buyNowVariant = await _context.ProductVariants
                .Include(x => x.Product)
                .Include(x => x.Color)
                .Include(x => x.Size)
                .FirstOrDefaultAsync(x => x.VariantId == dto.BuyNowItem.VariantId);

            if (buyNowVariant is null)
            {
                throw new BadRequestException("Sản phẩm không tồn tại.");
            }

            lines = new() { (buyNowVariant, dto.BuyNowItem.Quantity) };
        }
        else
        {
            cartItems = await _context.CartItems
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

            lines = cartItems.Select(x => (x.Variant, x.Quantity)).ToList();
        }

        decimal subtotal = 0;

        foreach (var (variant, quantity) in lines)
        {
            if (quantity is < 1 or > 100)
                throw new BadRequestException("Số lượng phải từ 1 đến 100.");

            if (!variant.IsActive || !variant.Product.IsActive)
            {
                throw new BadRequestException(
                    $"Sản phẩm '{variant.Product.ProductName}' hiện không còn được bán."
                );
            }

            if (variant.StockQuantity < quantity)
            {
                throw new BadRequestException(
                    $"Sản phẩm '{variant.Product.ProductName}' không đủ tồn kho."
                );
            }

            var unitPrice = PriceHelper.GetUnitPrice(variant);

            subtotal += unitPrice * quantity;
        }

        CouponValidationDto? couponValidation = null;
        Coupon? coupon = null;

        if (!string.IsNullOrWhiteSpace(dto.CouponCode))
        {
            var code = dto.CouponCode.Trim().ToUpperInvariant();
            var couponId = await _context.Coupons.Where(x => x.Code == code)
                .Select(x => (int?)x.CouponId).SingleOrDefaultAsync();
            if (couponId.HasValue) await CouponUsageCounter.LockAsync(_context, couponId.Value);

            couponValidation = await _couponService.ValidateForCartAsync(
                userId,
                new ValidateCouponDto
                {
                    Code = dto.CouponCode,
                    BuyNowItem = dto.BuyNowItem
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

        var shippingFee = ShippingHelper.CalculateShippingFee(subtotal);
        var discountAmount = couponValidation?.DiscountAmount ?? 0m;
        var totalAmount = subtotal - discountAmount + shippingFee;
        if (dto.PaymentMethod == PaymentMethod.VnPay) VnpayProtocol.Amount(totalAmount);

        // Reserve against the current database value, not the earlier entity snapshot.
        // The transaction rolls every reservation back if any line or order save fails.
        // Use a stable lock order for carts containing several variants.
        foreach (var (variant, quantity) in lines.OrderBy(x => x.Variant.VariantId))
        {
            var affected = await _context.ProductVariants
                .Where(x => x.VariantId == variant.VariantId
                    && x.StockQuantity >= quantity
                    && x.IsActive && x.Product.IsActive)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    x => x.StockQuantity, x => x.StockQuantity - quantity));

            if (affected != 1)
                throw new BadRequestException(
                    $"Sản phẩm '{variant.Product.ProductName}' không đủ tồn kho hoặc không còn được bán.");

            // A purchase receipt may have changed average cost while this reservation
            // waited. Stamp the cost at the actual reservation, preserving sale prices.
            // ExecuteUpdate bypasses tracking, so synchronize the original values too.
            var reserved = await _context.ProductVariants
                .Where(x => x.VariantId == variant.VariantId)
                .Select(x => new { x.StockQuantity, x.AverageCostPrice })
                .SingleAsync();
            var stock = _context.Entry(variant).Property(x => x.StockQuantity);
            stock.CurrentValue = reserved.StockQuantity;
            stock.OriginalValue = reserved.StockQuantity;
            stock.IsModified = false;
            var cost = _context.Entry(variant).Property(x => x.AverageCostPrice);
            cost.CurrentValue = reserved.AverageCostPrice;
            cost.OriginalValue = reserved.AverageCostPrice;
            cost.IsModified = false;
        }

        var order = new Order
        {
            RequestId = dto.RequestId,
            RequestHash = requestHash,
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

        foreach (var (variant, quantity) in lines)
        {
            var unitPrice = PriceHelper.GetUnitPrice(variant);

            order.OrderItems.Add(new OrderItem
            {
                VariantId = variant.VariantId,
                ProductName = variant.Product.ProductName,
                SKU = variant.SKU,
                ColorName = variant.Color.ColorName,
                SizeName = variant.Size.SizeName,
                UnitPrice = unitPrice,
                // Chốt giá vốn tại thời điểm bán để tính lợi nhuận về sau.
                UnitCost = variant.AverageCostPrice ?? 0m,
                Quantity = quantity,
                TotalPrice = unitPrice * quantity
            });
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
            await CouponUsageCounter.IncrementAsync(_context, coupon.CouponId);

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

    public async Task<OrderDetailDto> GetMyOrderByRequestIdAsync(int userId, Guid requestId)
    {
        var orderId = await _context.Orders.AsNoTracking()
            .Where(x => x.UserId == userId && x.RequestId == requestId)
            .Select(x => (int?)x.OrderId).SingleOrDefaultAsync();
        if (!orderId.HasValue) throw new NotFoundException("Chưa tìm thấy đơn hàng của yêu cầu này.");
        return await GetMyOrderByIdAsync(userId, orderId.Value);
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
        await VnpayOrderLock.AcquireAsync(_context, orderId);


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

        if (order.Payments.Any(x => x.PaymentMethod == PaymentMethod.VnPay))
        {
            await VnpayOrderLock.EnsureNoActiveAttemptAsync(_context, orderId);
            if (await _context.PaymentAttempts.AnyAsync(x => x.Payment.OrderId == orderId && x.RequiresReview))
                throw new BadRequestException("Giao dịch cần shop đối soát trước khi hủy đơn.");
        }

        await CouponUsageCounter.ReleaseAsync(_context, order.CouponUsages);
        await OrderStockRestorer.RestoreAsync(_context, order.OrderItems);

        if (order.CouponUsages.Count > 0)
        {
            _context.CouponUsages.RemoveRange(order.CouponUsages);
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
            .Include(x => x.ReturnRequest)
            .FirstOrDefaultAsync(x => x.OrderId == orderId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        return order;
    }

    public async Task<OrderDetailDto> CreateReturnRequestAsync(int userId, int orderId, CreateReturnRequestDto dto)
    {
        var order = await _context.Orders
            .Include(x => x.OrderStatusHistories)
            .Include(x => x.ReturnRequest)
            .FirstOrDefaultAsync(x =>
                x.OrderId == orderId &&
                x.UserId == userId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        if (order.ReturnRequest is not null)
        {
            throw new BadRequestException("Đơn hàng này đã có yêu cầu trả hàng.");
        }

        if (order.OrderStatus != OrderStatus.Completed)
        {
            throw new BadRequestException("Chỉ có thể yêu cầu trả hàng với đơn đã hoàn thành.");
        }

        if (order.PaymentStatus != PaymentStatus.Paid)
        {
            throw new BadRequestException("Chỉ có thể yêu cầu trả hàng với đơn đã thanh toán.");
        }

        if (!ReturnPolicy.CanRequest(order, DateTime.UtcNow))
        {
            throw new BadRequestException(
                $"Đã quá thời hạn {ReturnPolicy.WindowDays} ngày để yêu cầu trả hàng."
            );
        }

        var reason = dto.Reason.Trim();

        if (reason.Length < 10)
        {
            throw new BadRequestException("Lý do trả hàng phải từ 10 đến 500 ký tự.");
        }

        _context.ReturnRequests.Add(new ReturnRequest
        {
            OrderId = order.OrderId,
            UserId = userId,
            Reason = reason,
            Status = ReturnRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.OrderId,
            Status = order.OrderStatus,
            Note = ReturnPolicy.TrimNote($"Khách yêu cầu trả hàng: {reason}"),
            ChangedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        order.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await GetMyOrderByIdAsync(userId, orderId);
    }

    private async Task<string> GenerateOrderCodeAsync()
    {
        string orderCode;

        do
        {
            orderCode = $"DH{VietnamTime.Now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 1000)}";

        }
        while (await _context.Orders.AnyAsync(x => x.OrderCode == orderCode));
        return orderCode;
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
                }).ToList(),

            ReturnRequest = ReturnPolicy.ToDto(order.ReturnRequest),
            CanRequestReturn = ReturnPolicy.CanRequest(order, DateTime.UtcNow),
            ReturnDeadline = ReturnPolicy.GetDeadline(order)
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
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await VnpayOrderLock.AcquireAsync(_context, orderId);
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
        await transaction.CommitAsync();

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
