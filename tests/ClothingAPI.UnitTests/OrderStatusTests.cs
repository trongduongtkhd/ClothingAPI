using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Moq;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// AdminOrderService.UpdateOrderStatusAsync / UpdatePaymentStatusAsync và OrderService.CancelAsync (khách huỷ).
public class OrderStatusTests : UnitTestBase
{
    public OrderStatusTests(ITestOutputHelper output) : base(output) { }

    private int NewOrder(OrderStatus status, PaymentStatus payment = PaymentStatus.Unpaid)
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        return Db.AddOrder(Db.CustomerId, status, payment, items: [(variantId, 1)]).OrderId;
    }

    private async Task<Exception?> ChangeStatusAsync(int orderId, OrderStatus newStatus, string? note = null)
    {
        await using var context = Db.NewContext();
        var service = new AdminOrderService(context);
        return await CaptureAsync(() => service.UpdateOrderStatusAsync(Db.AdminId, orderId, new UpdateOrderStatusDto { Status = newStatus, Note = note }));
    }

    private OrderStatus StatusOf(int orderId) => Db.Query(db => db.Orders.Single(o => o.OrderId == orderId).OrderStatus);

    private int HistoryCount(int orderId) => Db.Query(db => db.OrderStatusHistories.Count(h => h.OrderId == orderId));

    private async Task<(OrderStatus Before, OrderStatus After, int Histories, Exception? Error)> TransitionAsync(OrderStatus from, OrderStatus to)
    {
        var orderId = NewOrder(from);
        var error = await ChangeStatusAsync(orderId, to);
        var after = StatusOf(orderId);
        var histories = HistoryCount(orderId);
        Actual($"{Describe(error)}; trạng thái đơn sau khi chạy = {after}; số bản ghi lịch sử = {histories}");
        return (from, after, histories, error);
    }

    [Fact]
    public async Task UT031_ChuyenTrangThai_ChoXacNhanSangDaXacNhan_HopLe()
    {
        Step1("Đơn ở trạng thái Pending (Chờ xác nhận), chưa có lịch sử; admin chuyển sang Confirmed (Đã xác nhận)");

        var (_, after, histories, error) = await TransitionAsync(OrderStatus.Pending, OrderStatus.Confirmed);

        Assert.Null(error);
        Assert.Equal(OrderStatus.Confirmed, after);
        Assert.Equal(1, histories);
    }

    [Fact]
    public async Task UT032_ChuyenTrangThai_DaXacNhanSangDangGiao_HopLe()
    {
        Step1("Đơn ở trạng thái Confirmed (Đã xác nhận); admin chuyển sang Shipping (Đang giao)");

        var (_, after, _, error) = await TransitionAsync(OrderStatus.Confirmed, OrderStatus.Shipping);

        Assert.Null(error);
        Assert.Equal(OrderStatus.Shipping, after);
    }

    [Fact]
    public async Task UT033_ChuyenTrangThai_DangGiaoSangHoanThanh_HopLe()
    {
        Step1("Đơn ở trạng thái Shipping (Đang giao); admin chuyển sang Completed (Hoàn thành)");

        var (_, after, _, error) = await TransitionAsync(OrderStatus.Shipping, OrderStatus.Completed);

        Assert.Null(error);
        Assert.Equal(OrderStatus.Completed, after);
    }

    [Fact]
    public async Task UT034_ChuyenTrangThai_NhayBuoc_BiTuChoi()
    {
        Step1("Đơn ở trạng thái Pending (Chờ xác nhận); admin chuyển thẳng sang Completed (Hoàn thành)");

        var (_, after, _, error) = await TransitionAsync(OrderStatus.Pending, OrderStatus.Completed);

        // Thông báo thật dùng tên enum (Pending/Completed) thay cho "Chờ xác nhận"/"Hoàn thành" – chấp nhận theo yêu cầu.
        Assert.IsType<BadRequestException>(error);
        Assert.StartsWith("Không thể chuyển trạng thái từ", error!.Message);
        Assert.Contains("Pending", error.Message);
        Assert.Contains("Completed", error.Message);
        Assert.Equal(OrderStatus.Pending, after);
    }

    [Fact]
    public async Task UT035_ChuyenTrangThai_QuayLai_BiTuChoi()
    {
        Step1("Đơn ở trạng thái Shipping (Đang giao); admin chuyển ngược về Confirmed (Đã xác nhận)");

        var (_, after, _, error) = await TransitionAsync(OrderStatus.Shipping, OrderStatus.Confirmed);

        Assert.IsType<BadRequestException>(error);
        Assert.StartsWith("Không thể chuyển trạng thái", error!.Message);
        Assert.Equal(OrderStatus.Shipping, after);
    }

    [Fact]
    public async Task UT036_ChuyenTrangThai_TuHoanThanh_BiTuChoi()
    {
        Step1("5 đơn ở trạng thái Completed (Hoàn thành); admin lần lượt chuyển sang Pending, Confirmed, Shipping, Completed, Cancelled");

        var results = new List<string>();
        var allRejected = true;
        foreach (var target in Enum.GetValues<OrderStatus>())
        {
            var orderId = NewOrder(OrderStatus.Completed);
            var error = await ChangeStatusAsync(orderId, target);
            var after = StatusOf(orderId);
            results.Add($"sang {target}: {(error is null ? "THÀNH CÔNG" : $"\"{error.Message}\"")}, trạng thái giữ {after}");
            allRejected &= error is BadRequestException
                && error.Message.StartsWith("Không thể chuyển trạng thái")
                && after == OrderStatus.Completed;
        }
        Actual(string.Join("; ", results));

        Assert.True(allRejected);
    }

    [Fact]
    public async Task UT037_ChuyenTrangThai_TuDaHuy_BiTuChoi()
    {
        Step1("Đơn ở trạng thái Cancelled (Đã huỷ); admin chuyển sang Confirmed (Đã xác nhận)");

        var (_, after, _, error) = await TransitionAsync(OrderStatus.Cancelled, OrderStatus.Confirmed);

        Assert.IsType<BadRequestException>(error);
        Assert.StartsWith("Không thể chuyển trạng thái", error!.Message);
        Assert.Equal(OrderStatus.Cancelled, after);
    }

    [Fact]
    public async Task UT038_AdminHuy_ChoXacNhan_HopLe()
    {
        // Tồn 8 = 10 ban đầu − 2 đã bán cho đơn này; mã đã được dùng 1 lượt bởi đơn này.
        var (variantId, _) = Db.AddVariant(stock: 8);
        var couponId = Db.AddCoupon("HUYDON", usedCount: 1);
        var (orderId, _) = Db.AddOrder(Db.CustomerId, OrderStatus.Pending, items: [(variantId, 2)], couponId: couponId, discount: 40000);
        Step1("Đơn Pending (Chờ xác nhận) gồm 2 sản phẩm (tồn hiện tại 8), dùng mã \"HUYDON\" (UsedCount = 1, có 1 CouponUsage); admin huỷ với lý do \"Hết hàng\"");

        var error = await ChangeStatusAsync(orderId, OrderStatus.Cancelled, "Hết hàng");

        var status = StatusOf(orderId);
        var stock = Db.Query(db => db.ProductVariants.Single(v => v.VariantId == variantId).StockQuantity);
        var usedCount = Db.Query(db => db.Coupons.Single(c => c.CouponId == couponId).UsedCount);
        var usages = Db.Query(db => db.CouponUsages.Count(u => u.OrderId == orderId));
        var note = Db.Query(db => db.OrderStatusHistories.Where(h => h.OrderId == orderId).Select(h => h.Note).Single());
        Actual($"{Describe(error)}; trạng thái = {status}; tồn 8 → {stock}; UsedCount 1 → {usedCount}; CouponUsage còn {usages}; ghi chú lịch sử = \"{note}\"");

        Assert.Null(error);
        Assert.Equal(OrderStatus.Cancelled, status);
        Assert.Equal(10, stock);
        Assert.Equal(0, usedCount);
        Assert.Equal(0, usages);
        Assert.Equal("Hết hàng", note);
    }

    [Fact]
    public async Task UT039_AdminHuy_DaXacNhan_HopLe()
    {
        var (variantId, _) = Db.AddVariant(stock: 9);
        var (orderId, _) = Db.AddOrder(Db.CustomerId, OrderStatus.Confirmed, items: [(variantId, 1)]);
        Step1("Đơn Confirmed (Đã xác nhận) gồm 1 sản phẩm (tồn hiện tại 9); admin huỷ với lý do \"Khách đổi ý\"");

        var error = await ChangeStatusAsync(orderId, OrderStatus.Cancelled, "Khách đổi ý");

        var status = StatusOf(orderId);
        var stock = Db.Query(db => db.ProductVariants.Single(v => v.VariantId == variantId).StockQuantity);
        Actual($"{Describe(error)}; trạng thái = {status}; tồn 9 → {stock}");

        Assert.Null(error);
        Assert.Equal(OrderStatus.Cancelled, status);
    }

    [Fact]
    public async Task UT040_AdminHuy_DangGiao_BiTuChoi()
    {
        Step1("Đơn ở trạng thái Shipping (Đang giao); admin huỷ (chuyển sang Cancelled)");

        var (_, after, _, error) = await TransitionAsync(OrderStatus.Shipping, OrderStatus.Cancelled);

        Assert.IsType<BadRequestException>(error);
        Assert.StartsWith("Không thể chuyển trạng thái", error!.Message);
        Assert.Equal(OrderStatus.Shipping, after);
    }

    private async Task<Exception?> CustomerCancelAsync(int orderId)
    {
        await using var context = Db.NewContext();
        var service = new OrderService(context, Mock.Of<ICouponService>());
        return await CaptureAsync(() => service.CancelAsync(Db.CustomerId, orderId, new CancelOrderDto { Reason = "Đổi ý" }));
    }

    [Fact]
    public async Task UT041_KhachHuy_DaThanhToan_BiTuChoi()
    {
        var orderId = NewOrder(OrderStatus.Pending, PaymentStatus.Paid);
        Step1("Đơn của khách ở trạng thái Pending (Chờ xác nhận), PaymentStatus = Paid (Đã thanh toán); khách huỷ");

        var error = await CustomerCancelAsync(orderId);
        Actual($"{Describe(error)}; trạng thái đơn sau = {StatusOf(orderId)}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Đơn hàng đã thanh toán không thể hủy trực tiếp.", error!.Message);
    }

    [Fact]
    public async Task UT042_KhachHuy_DaXacNhan_BiTuChoi()
    {
        var orderId = NewOrder(OrderStatus.Confirmed, PaymentStatus.Unpaid);
        Step1("Đơn của khách ở trạng thái Confirmed (Đã xác nhận), PaymentStatus = Unpaid (Chưa thanh toán); khách huỷ");

        var error = await CustomerCancelAsync(orderId);
        Actual($"{Describe(error)}; trạng thái đơn sau = {StatusOf(orderId)}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Chỉ có thể hủy đơn hàng đang chờ xác nhận.", error!.Message);
    }

    private async Task<Exception?> ChangePaymentAsync(int orderId, PaymentStatus newStatus)
    {
        await using var context = Db.NewContext();
        var service = new AdminOrderService(context);
        return await CaptureAsync(() => service.UpdatePaymentStatusAsync(Db.AdminId, orderId, new UpdatePaymentStatusDto { PaymentStatus = newStatus }));
    }

    [Fact]
    public async Task UT043_ThanhToan_ChuaSangDa_GhiThoiDiemVaMaGiaoDich()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        var (orderId, orderCode) = Db.AddOrder(Db.CustomerId, OrderStatus.Completed, PaymentStatus.Unpaid, items: [(variantId, 1)]);
        Step1($"Đơn {orderCode} (COD) đã Completed nhưng PaymentStatus = Unpaid, PaidAt = null, TransactionCode = null (dữ liệu cũ); admin cập nhật sang Paid");

        var error = await ChangePaymentAsync(orderId, PaymentStatus.Paid);

        var payment = Db.Query(db => db.Payments.Single(p => p.OrderId == orderId));
        var orderPayment = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId).PaymentStatus);
        Actual($"{Describe(error)}; PaymentStatus = {payment.PaymentStatus} (đơn: {orderPayment}); PaidAt = {(payment.PaidAt.HasValue ? payment.PaidAt.Value.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" : "null")}; TransactionCode = {payment.TransactionCode ?? "null"}");

        Assert.Null(error);
        Assert.Equal(PaymentStatus.Paid, payment.PaymentStatus);
        Assert.NotNull(payment.PaidAt);
        Assert.False(string.IsNullOrEmpty(payment.TransactionCode));
    }

    [Fact]
    public async Task UT044_ThanhToan_DonDaHuy_BiTuChoi()
    {
        var orderId = NewOrder(OrderStatus.Cancelled, PaymentStatus.Unpaid);
        Step1("Đơn ở trạng thái Cancelled (Đã huỷ), PaymentStatus = Unpaid; admin cập nhật sang Paid");

        var error = await ChangePaymentAsync(orderId, PaymentStatus.Paid);
        var payment = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId).PaymentStatus);
        Actual($"{Describe(error)}; PaymentStatus giữ = {payment}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Không thể cập nhật thanh toán cho đơn hàng đã hủy.", error!.Message);
    }

    [Fact]
    public async Task UT045_HoanThanh_DonCOD_TuGhiNhanDaThanhToan()
    {
        var orderId = NewOrder(OrderStatus.Shipping, PaymentStatus.Unpaid);
        Step1("Đơn COD ở trạng thái Shipping (Đang giao), PaymentStatus = Unpaid; admin chuyển sang Completed (Hoàn thành)");

        var error = await ChangeStatusAsync(orderId, OrderStatus.Completed);

        var payment = Db.Query(db => db.Payments.Single(p => p.OrderId == orderId));
        var orderPayment = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId).PaymentStatus);
        var note = Db.Query(db => db.OrderStatusHistories.Single(h => h.OrderId == orderId).Note);
        Actual($"{Describe(error)}; trạng thái = {StatusOf(orderId)}; PaymentStatus = {payment.PaymentStatus} (đơn: {orderPayment}); PaidAt = {payment.PaidAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null"}; ghi chú = {note}");

        Assert.Null(error);
        Assert.Equal(OrderStatus.Completed, StatusOf(orderId));
        Assert.Equal(PaymentStatus.Paid, payment.PaymentStatus);
        Assert.Equal(PaymentStatus.Paid, orderPayment);
        Assert.NotNull(payment.PaidAt);
        Assert.StartsWith("Đã giao hàng và thu tiền mặt (COD)", note);
    }

    [Fact]
    public async Task UT046_ThanhToan_DonCODChuaGiao_BiTuChoi()
    {
        var orderId = NewOrder(OrderStatus.Confirmed, PaymentStatus.Unpaid);
        Step1("Đơn COD ở trạng thái Confirmed (Đã xác nhận), PaymentStatus = Unpaid; admin bấm tay sang Paid");

        var error = await ChangePaymentAsync(orderId, PaymentStatus.Paid);
        var payment = Db.Query(db => db.Orders.Single(o => o.OrderId == orderId).PaymentStatus);
        Actual($"{Describe(error)}; PaymentStatus giữ = {payment}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Đơn COD sẽ tự ghi nhận đã thanh toán khi giao hàng thành công.", error!.Message);
        Assert.Equal(PaymentStatus.Unpaid, payment);
    }

    [Fact]
    public async Task UT047_HoanThanh_DonQRChuaThanhToan_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        var (orderId, _) = Db.AddOrder(Db.CustomerId, OrderStatus.Shipping, PaymentStatus.Unpaid, items: [(variantId, 1)], paymentMethod: PaymentMethod.MockQR);
        Step1("Đơn thanh toán QR ở trạng thái Shipping (Đang giao), PaymentStatus = Unpaid; admin chuyển sang Completed");

        var error = await ChangeStatusAsync(orderId, OrderStatus.Completed);
        Actual($"{Describe(error)}; trạng thái giữ = {StatusOf(orderId)}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Đơn thanh toán online chưa được thanh toán, không thể hoàn thành.", error!.Message);
        Assert.Equal(OrderStatus.Shipping, StatusOf(orderId));
    }
}
