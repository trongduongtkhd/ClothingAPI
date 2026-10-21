using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Moq;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// Luồng trả hàng / hoàn tiền: OrderService.CreateReturnRequestAsync (khách) và
/// AdminOrderService.ApproveReturnAsync / RejectReturnAsync / huỷ đơn đã thanh toán (admin).
public class ReturnRequestTests : UnitTestBase
{
    public ReturnRequestTests(ITestOutputHelper output) : base(output) { }

    private (int OrderId, int VariantId) NewCompletedOrder(
        PaymentStatus payment = PaymentStatus.Paid,
        OrderStatus status = OrderStatus.Completed,
        int completedDaysAgo = 1)
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        var (orderId, _) = Db.AddOrder(Db.CustomerId, status, payment, items: [(variantId, 2)]);

        if (status == OrderStatus.Completed)
        {
            using var db = Db.NewContext();
            db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = orderId,
                Status = OrderStatus.Completed,
                Note = "Hoàn thành",
                ChangedByUserId = Db.AdminId,
                CreatedAt = DateTime.UtcNow.AddDays(-completedDaysAgo)
            });
            db.SaveChanges();
        }

        return (orderId, variantId);
    }

    private async Task<Exception?> RequestReturnAsync(int orderId, string reason = "Áo bị lỗi đường may ở tay áo")
    {
        await using var context = Db.NewContext();
        var service = new OrderService(context, Mock.Of<ICouponService>());
        return await CaptureAsync(() => service.CreateReturnRequestAsync(Db.CustomerId, orderId, new CreateReturnRequestDto { Reason = reason }));
    }

    private async Task<Exception?> ApproveAsync(int orderId, string? note = null)
    {
        await using var context = Db.NewContext();
        var service = new AdminOrderService(context);
        return await CaptureAsync(() => service.ApproveReturnAsync(Db.AdminId, orderId, new ProcessReturnRequestDto { Note = note }));
    }

    private async Task<Exception?> RejectAsync(int orderId, string? note)
    {
        await using var context = Db.NewContext();
        var service = new AdminOrderService(context);
        return await CaptureAsync(() => service.RejectReturnAsync(Db.AdminId, orderId, new ProcessReturnRequestDto { Note = note }));
    }

    private ReturnRequestStatus? RequestStatusOf(int orderId) =>
        Db.Query(db => db.ReturnRequests.SingleOrDefault(r => r.OrderId == orderId)?.Status);

    [Fact]
    public async Task UT138_YeuCauTraHang_DonHoanThanhDaThanhToan_HopLe()
    {
        var (orderId, _) = NewCompletedOrder();
        Step1("Đơn Completed, Paid, hoàn thành 1 ngày trước, chưa có yêu cầu; khách gửi yêu cầu trả hàng");

        var error = await RequestReturnAsync(orderId);
        Actual($"{Describe(error)}; trạng thái yêu cầu = {RequestStatusOf(orderId)}");

        Assert.Null(error);
        Assert.Equal(ReturnRequestStatus.Pending, RequestStatusOf(orderId));
    }

    [Fact]
    public async Task UT139_YeuCauTraHang_QuaHan7Ngay_BiTuChoi()
    {
        var (orderId, _) = NewCompletedOrder(completedDaysAgo: 8);
        Step1("Đơn Completed, Paid, hoàn thành 8 ngày trước; khách gửi yêu cầu trả hàng");

        var error = await RequestReturnAsync(orderId);
        Actual(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Đã quá thời hạn 7 ngày để yêu cầu trả hàng.", error!.Message);
        Assert.Null(RequestStatusOf(orderId));
    }

    [Fact]
    public async Task UT140_YeuCauTraHang_DonChuaHoanThanh_BiTuChoi()
    {
        var (orderId, _) = NewCompletedOrder(PaymentStatus.Unpaid, OrderStatus.Shipping);
        Step1("Đơn Shipping (Đang giao), Unpaid; khách gửi yêu cầu trả hàng");

        var error = await RequestReturnAsync(orderId);
        Actual(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Chỉ có thể yêu cầu trả hàng với đơn đã hoàn thành.", error!.Message);
    }

    [Fact]
    public async Task UT141_YeuCauTraHang_GuiLanHai_BiTuChoi()
    {
        var (orderId, _) = NewCompletedOrder();
        await RequestReturnAsync(orderId);
        Step1("Đơn Completed, Paid đã có 1 yêu cầu trả hàng; khách gửi thêm yêu cầu");

        var error = await RequestReturnAsync(orderId);
        Actual(Describe(error));

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Đơn hàng này đã có yêu cầu trả hàng.", error!.Message);
    }

    [Fact]
    public async Task UT142_DuyetTraHang_HoanTienVaCongLaiTonKho()
    {
        var (orderId, variantId) = NewCompletedOrder();
        await RequestReturnAsync(orderId);
        var stockBefore = Db.Query(db => db.ProductVariants.Single(v => v.VariantId == variantId).StockQuantity);
        Step1($"Đơn Completed, Paid (2 sản phẩm), có yêu cầu trả hàng Pending; tồn kho trước = {stockBefore}; admin duyệt");

        var error = await ApproveAsync(orderId);

        var order = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId));
        var payment = Db.Query(db => db.Payments.Single(p => p.OrderId == orderId).PaymentStatus);
        var stockAfter = Db.Query(db => db.ProductVariants.Single(v => v.VariantId == variantId).StockQuantity);
        Actual($"{Describe(error)}; đơn = {order.OrderStatus}; thanh toán = {order.PaymentStatus}/{payment}; yêu cầu = {RequestStatusOf(orderId)}; tồn kho sau = {stockAfter}");

        Assert.Null(error);
        Assert.Equal(OrderStatus.Returned, order.OrderStatus);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(PaymentStatus.Refunded, payment);
        Assert.Equal(ReturnRequestStatus.Approved, RequestStatusOf(orderId));
        Assert.Equal(stockBefore + 2, stockAfter);
    }

    [Fact]
    public async Task UT143_TuChoiTraHang_ThieuLyDo_BiTuChoi()
    {
        var (orderId, _) = NewCompletedOrder();
        await RequestReturnAsync(orderId);
        Step1("Đơn có yêu cầu trả hàng Pending; admin từ chối nhưng không nhập lý do");

        var error = await RejectAsync(orderId, "  ");
        Actual($"{Describe(error)}; yêu cầu giữ = {RequestStatusOf(orderId)}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal(ReturnRequestStatus.Pending, RequestStatusOf(orderId));
    }

    [Fact]
    public async Task UT144_TuChoiTraHang_CoLyDo_DonGiuHoanThanh()
    {
        var (orderId, _) = NewCompletedOrder();
        await RequestReturnAsync(orderId);
        Step1("Đơn có yêu cầu trả hàng Pending; admin từ chối với lý do");

        var error = await RejectAsync(orderId, "Sản phẩm đã qua sử dụng");
        var order = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId));
        var adminNote = Db.Query(db => db.ReturnRequests.Single(r => r.OrderId == orderId).AdminNote);
        Actual($"{Describe(error)}; đơn = {order.OrderStatus}/{order.PaymentStatus}; yêu cầu = {RequestStatusOf(orderId)}; ghi chú = {adminNote}");

        Assert.Null(error);
        Assert.Equal(OrderStatus.Completed, order.OrderStatus);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(ReturnRequestStatus.Rejected, RequestStatusOf(orderId));
        Assert.Equal("Sản phẩm đã qua sử dụng", adminNote);
    }

    [Fact]
    public async Task UT145_AdminHuyDonDaThanhToan_TuHoanTien()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        var (orderId, _) = Db.AddOrder(Db.CustomerId, OrderStatus.Confirmed, PaymentStatus.Paid, items: [(variantId, 1)], paymentMethod: PaymentMethod.MockQR);
        Step1("Đơn QR đã thanh toán, trạng thái Confirmed; admin huỷ đơn");

        await using var context = Db.NewContext();
        var service = new AdminOrderService(context);
        var error = await CaptureAsync(() => service.UpdateOrderStatusAsync(Db.AdminId, orderId, new UpdateOrderStatusDto { Status = OrderStatus.Cancelled }));

        var order = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId));
        Actual($"{Describe(error)}; đơn = {order.OrderStatus}; thanh toán = {order.PaymentStatus}");

        Assert.Null(error);
        Assert.Equal(OrderStatus.Cancelled, order.OrderStatus);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
    }

    [Fact]
    public async Task UT146_HoanTienThuCong_BiTuChoi()
    {
        var (orderId, _) = NewCompletedOrder();
        Step1("Đơn Completed, Paid; admin bấm tay chuyển thanh toán sang Refunded");

        await using var context = Db.NewContext();
        var service = new AdminOrderService(context);
        var error = await CaptureAsync(() => service.UpdatePaymentStatusAsync(Db.AdminId, orderId, new UpdatePaymentStatusDto { PaymentStatus = PaymentStatus.Refunded }));
        var payment = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId).PaymentStatus);
        Actual($"{Describe(error)}; thanh toán giữ = {payment}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal(PaymentStatus.Paid, payment);
    }
}
