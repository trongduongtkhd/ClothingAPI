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
            .Include(x => x.ReturnRequest)
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

        if (query.HasPendingReturn == true)
        {
            orders = orders.Where(x =>
                x.ReturnRequest != null &&
                x.ReturnRequest.Status == ReturnRequestStatus.Pending);
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
                CreatedAt = x.CreatedAt,
                HasPendingReturn = x.ReturnRequest != null &&
                                   x.ReturnRequest.Status == ReturnRequestStatus.Pending
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
        await VnpayOrderLock.AcquireAsync(_context, orderId);

        var order = await _context.Orders
            .Include(x => x.OrderItems)
                .ThenInclude(x => x.Variant)
            .Include(x => x.CouponUsages)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.OrderId == orderId);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        if (!IsValidStatusTransition(order.OrderStatus, dto.Status))
        {
            throw new BadRequestException($"Không thể chuyển trạng thái từ {order.OrderStatus} sang {dto.Status}.");
        }

        string? defaultNote = null;
        var vnpayPayment = order.Payments.FirstOrDefault(x => x.PaymentMethod == PaymentMethod.VnPay);
        if (vnpayPayment is not null)
        {
            if (dto.Status == OrderStatus.Cancelled)
            {
                if (order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Refunded)
                    throw new BadRequestException("Đơn VNPAY đã thanh toán cần hoàn tiền qua cổng trước khi hủy; chức năng hoàn tiền chưa được bật.");
                await VnpayOrderLock.EnsureNoActiveAttemptAsync(_context, orderId);
                if (await _context.PaymentAttempts.AnyAsync(x => x.Payment.OrderId == orderId && x.RequiresReview))
                    throw new BadRequestException("Giao dịch VNPAY cần đối soát trước khi hủy.");
            }
            else if (order.PaymentStatus != PaymentStatus.Paid)
                throw new BadRequestException("Cần nhận xác nhận thanh toán VNPAY trước khi xử lý giao hàng.");
        }

        // Hoàn thành = đã giao hàng. Với COD, shipper thu tiền mặt lúc giao nên ghi nhận đã thanh toán luôn;
        // với thanh toán online thì phải thanh toán xong mới được hoàn thành.
        if (dto.Status == OrderStatus.Completed)
        {
            var payment = order.Payments.FirstOrDefault();

            if (payment is not null && payment.PaymentStatus != PaymentStatus.Paid)
            {
                if (payment.PaymentMethod != PaymentMethod.COD)
                {
                    throw new BadRequestException(
                        "Đơn thanh toán online chưa được thanh toán, không thể hoàn thành."
                    );
                }

                MarkPaymentAsPaid(order, payment);
                defaultNote = $"Đã giao hàng và thu tiền mặt (COD) {order.TotalAmount:N0} ₫.";
            }
        }

        if (dto.Status == OrderStatus.Cancelled)
        {
            await CouponUsageCounter.ReleaseAsync(_context, order.CouponUsages);
            await OrderStockRestorer.RestoreAsync(_context, order.OrderItems);

            // Khách đã thanh toán trước (QR) mà đơn bị hủy thì phải hoàn tiền.
            var payment = order.Payments.FirstOrDefault();

            if (payment is not null && payment.PaymentStatus == PaymentStatus.Paid)
            {
                MarkPaymentAsRefunded(order, payment);
                defaultNote = $"Đã hủy đơn và hoàn {order.TotalAmount:N0} ₫ cho khách.";
            }

            if (order.CouponUsages.Count > 0)
            {
                _context.CouponUsages.RemoveRange(order.CouponUsages);
            }
        }

        order.OrderStatus = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Status = dto.Status,
            Note = string.IsNullOrWhiteSpace(dto.Note)
                ? defaultNote ?? $"Admin cập nhật trạng thái thành {dto.Status}."
                : dto.Note.Trim(),
            ChangedByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(orderId);
    }

    public async Task<PaymentDto> UpdatePaymentStatusAsync(int adminUserId, int orderId, UpdatePaymentStatusDto dto)

    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await VnpayOrderLock.AcquireAsync(_context, orderId);
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

        if (dto.PaymentStatus == PaymentStatus.Refunded)
        {
            throw new BadRequestException(
                "Hoàn tiền được thực hiện khi duyệt yêu cầu trả hàng hoặc hủy đơn đã thanh toán."
            );
        }

        if (order.OrderStatus == OrderStatus.Returned)
        {
            throw new BadRequestException(
                "Không thể cập nhật thanh toán cho đơn hàng đã trả."
            );
        }

        var payment = order.Payments.FirstOrDefault();

        if (payment is null)
        {
            throw new NotFoundException(
                "Không tìm thấy thông tin thanh toán của đơn hàng."
            );
        }

        if (payment.PaymentMethod == PaymentMethod.VnPay)
            throw new BadRequestException("Thanh toán VNPAY được xác nhận tự động từ cổng, không thể thay đổi thủ công.");

        // COD chỉ thu tiền khi giao hàng: hệ thống tự ghi nhận lúc đơn Hoàn thành.
        // Cho phép bấm tay với đơn đã Hoàn thành mà vẫn chưa thanh toán (dữ liệu cũ).
        if (dto.PaymentStatus == PaymentStatus.Paid &&
            payment.PaymentMethod == PaymentMethod.COD &&
            order.OrderStatus != OrderStatus.Completed)
        {
            throw new BadRequestException(
                "Đơn COD sẽ tự ghi nhận đã thanh toán khi giao hàng thành công."
            );
        }

        if (dto.PaymentStatus == PaymentStatus.Paid)
        {
            MarkPaymentAsPaid(order, payment);
        }
        else
        {
            payment.PaymentStatus = dto.PaymentStatus;
            order.PaymentStatus = dto.PaymentStatus;
        }

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

    public async Task<OrderDetailDto> ApproveReturnAsync(int adminUserId, int orderId, ProcessReturnRequestDto dto)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await VnpayOrderLock.AcquireAsync(_context, orderId);

        var order = await _context.Orders
            .Include(x => x.OrderItems)
                .ThenInclude(x => x.Variant)
            .Include(x => x.Payments)
            .Include(x => x.ReturnRequest)
            .FirstOrDefaultAsync(x => x.OrderId == orderId);

        var request = GetPendingReturnRequest(order);

        var payment = order!.Payments.FirstOrDefault();

        if (payment?.PaymentMethod == PaymentMethod.VnPay)
            throw new BadRequestException("Hoàn tiền qua VNPAY chưa được bật. Chưa thể duyệt trả hàng cho giao dịch này.");

        await OrderStockRestorer.RestoreAsync(_context, order.OrderItems);

        if (payment is not null)
        {
            MarkPaymentAsRefunded(order, payment);
        }
        else
        {
            order.PaymentStatus = PaymentStatus.Refunded;
        }

        var note = NormalizeNote(dto.Note);

        request.Status = ReturnRequestStatus.Approved;
        request.AdminNote = note;
        request.ProcessedAt = DateTime.UtcNow;
        request.ProcessedByUserId = adminUserId;

        order.OrderStatus = OrderStatus.Returned;
        order.UpdatedAt = DateTime.UtcNow;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Status = OrderStatus.Returned,
            Note = ReturnPolicy.TrimNote(
                $"Đã duyệt trả hàng, hoàn {order.TotalAmount:N0} ₫ cho khách." +
                (note is null ? string.Empty : $" {note}")
            ),
            ChangedByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(orderId);
    }

    public async Task<OrderDetailDto> RejectReturnAsync(int adminUserId, int orderId, ProcessReturnRequestDto dto)
    {
        var note = NormalizeNote(dto.Note);

        if (note is null)
        {
            throw new BadRequestException("Vui lòng nhập lý do từ chối yêu cầu trả hàng.");
        }

        var order = await _context.Orders
            .Include(x => x.ReturnRequest)
            .FirstOrDefaultAsync(x => x.OrderId == orderId);

        var request = GetPendingReturnRequest(order);

        request.Status = ReturnRequestStatus.Rejected;
        request.AdminNote = note;
        request.ProcessedAt = DateTime.UtcNow;
        request.ProcessedByUserId = adminUserId;

        order!.UpdatedAt = DateTime.UtcNow;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Status = order.OrderStatus,
            Note = ReturnPolicy.TrimNote($"Từ chối yêu cầu trả hàng: {note}"),
            ChangedByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return await GetByIdAsync(orderId);
    }

    private static ReturnRequest GetPendingReturnRequest(Order? order)
    {
        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        if (order.ReturnRequest is null)
        {
            throw new BadRequestException("Đơn hàng này không có yêu cầu trả hàng.");
        }

        if (order.ReturnRequest.Status != ReturnRequestStatus.Pending)
        {
            throw new BadRequestException("Yêu cầu trả hàng này đã được xử lý.");
        }

        return order.ReturnRequest;
    }

    private static string? NormalizeNote(string? note)
    {
        return string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private static void MarkPaymentAsRefunded(Order order, Payment payment)
    {
        payment.PaymentStatus = PaymentStatus.Refunded;
        order.PaymentStatus = PaymentStatus.Refunded;
    }

    private static void MarkPaymentAsPaid(Order order, Payment payment)
    {
        payment.PaymentStatus = PaymentStatus.Paid;
        payment.PaidAt ??= DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(payment.TransactionCode))
        {
            payment.TransactionCode = $"{payment.PaymentMethod}-{order.OrderCode}";
        }

        order.PaymentStatus = PaymentStatus.Paid;
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
                }).ToList(),

            ReturnRequest = ReturnPolicy.ToDto(order.ReturnRequest),
            CanRequestReturn = ReturnPolicy.CanRequest(order, DateTime.UtcNow),
            ReturnDeadline = ReturnPolicy.GetDeadline(order)
        };
    }
}
