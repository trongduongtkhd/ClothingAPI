using ClothingAPI.DTOs.Coupons;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Moq;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// Đơn giá: PriceHelper.GetUnitPrice. Tạm tính / phí vận chuyển / tổng tiền: OrderService.CreateAsync
/// (phí ship nằm trong hàm private CalculateShippingFee). ICouponService được mock.
public class OrderPricingTests : UnitTestBase
{
    private readonly Mock<ICouponService> _couponService = new(MockBehavior.Strict);

    public OrderPricingTests(ITestOutputHelper output) : base(output) { }

    private async Task<(OrderDetailDto? Order, Exception? Error)> CreateOrderAsync(string? couponCode = null)
    {
        var addressId = Db.AddAddress(Db.CustomerId);
        await using var context = Db.NewContext();
        var service = new OrderService(context, _couponService.Object);
        OrderDetailDto? order = null;
        var error = await CaptureAsync(async () => order = await service.CreateAsync(Db.CustomerId, new CreateOrderDto
        {
            AddressId = addressId,
            PaymentMethod = PaymentMethod.COD,
            CouponCode = couponCode
        }));
        return (order, error);
    }

    private void CartItem(decimal price, int quantity)
    {
        var (variantId, _) = Db.AddVariant(basePrice: price, stock: 100);
        Db.AddCartItem(Db.CustomerId, variantId, quantity);
    }

    /// Mock ICouponService trả về số tiền giảm cố định cho mã có thật trong CSDL.
    private void MockCoupon(string code, decimal discount)
    {
        var couponId = Db.AddCoupon(code, DiscountType.FixedAmount, discount);
        _couponService
            .Setup(x => x.ValidateForCartAsync(Db.CustomerId, It.Is<ValidateCouponDto>(d => d.Code == code)))
            .ReturnsAsync(new CouponValidationDto { CouponId = couponId, CouponCode = code, DiscountAmount = discount });
    }

    private void ReportOrder(OrderDetailDto? order, Exception? error) =>
        Actual(error is null
            ? $"đơn tạo thành công: tạm tính = {Money(order!.Subtotal)}, giảm = {Money(order.DiscountAmount)}, phí vận chuyển = {Money(order.ShippingFee)}, tổng tiền = {Money(order.TotalAmount)}"
            : Describe(error));

    [Fact]
    public void UT021_DonGia_CoGiaKhuyenMai_DungGiaKhuyenMai()
    {
        var variant = new ProductVariant { Price = null, Product = new Product { BasePrice = 400000m, SalePrice = 300000m } };
        Step1("Sản phẩm giá gốc 400.000, giá khuyến mãi 300.000; biến thể không có giá riêng");

        var unitPrice = PriceHelper.GetUnitPrice(variant);
        Actual($"PriceHelper.GetUnitPrice trả về đơn giá = {Money(unitPrice)}");

        Assert.Equal(300000m, unitPrice);
    }

    [Fact]
    public void UT022_DonGia_KhongKhuyenMai_DungGiaGoc()
    {
        var variant = new ProductVariant { Price = null, Product = new Product { BasePrice = 200000m, SalePrice = null } };
        Step1("Sản phẩm giá gốc 200.000, giá khuyến mãi để trống; biến thể không có giá riêng");

        var unitPrice = PriceHelper.GetUnitPrice(variant);
        Actual($"PriceHelper.GetUnitPrice trả về đơn giá = {Money(unitPrice)}");

        Assert.Equal(200000m, unitPrice);
    }

    [Fact]
    public async Task UT023_TamTinh_NhieuDong_TongDung()
    {
        CartItem(300000, 2);
        CartItem(200000, 1);
        Step1("Giỏ 2 dòng: 2 × 300.000 + 1 × 200.000; không dùng mã");

        var (order, error) = await CreateOrderAsync();
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(800000m, order!.Subtotal);
    }

    [Fact]
    public async Task UT024_PhiVanChuyen_499999_Mat30000()
    {
        CartItem(499999, 1);
        Step1("Giỏ 1 × 499.999 (tạm tính 499.999); không dùng mã");

        var (order, error) = await CreateOrderAsync();
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(499999m, order!.Subtotal);
        Assert.Equal(30000m, order.ShippingFee);
    }

    [Fact]
    public async Task UT025_PhiVanChuyen_500000_MienPhi()
    {
        CartItem(500000, 1);
        Step1("Giỏ 1 × 500.000 (tạm tính 500.000); không dùng mã");

        var (order, error) = await CreateOrderAsync();
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(0m, order!.ShippingFee);
    }

    [Fact]
    public async Task UT026_PhiVanChuyen_500001_MienPhi()
    {
        CartItem(500001, 1);
        Step1("Giỏ 1 × 500.001 (tạm tính 500.001); không dùng mã");

        var (order, error) = await CreateOrderAsync();
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(0m, order!.ShippingFee);
    }

    [Fact]
    public async Task UT027_PhiVanChuyen_RatNho_Mat30000()
    {
        CartItem(1, 1);
        Step1("Giỏ 1 × 1 đồng (tạm tính 1); không dùng mã");

        var (order, error) = await CreateOrderAsync();
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(30000m, order!.ShippingFee);
    }

    [Fact]
    public async Task UT028_TongTien_KhongMa_DungCongThuc()
    {
        CartItem(300000, 1);
        Step1("Giỏ 1 × 300.000 (tạm tính 300.000); không dùng mã (giảm 0)");

        var (order, error) = await CreateOrderAsync();
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(330000m, order!.TotalAmount);
    }

    [Fact]
    public async Task UT029_TongTien_CoMa_DungCongThuc()
    {
        CartItem(400000, 2);
        MockCoupon("GIAM50K", 50000);
        Step1("Giỏ 2 × 400.000 (tạm tính 800.000); mã \"GIAM50K\" (ICouponService mock) giảm 50.000");

        var (order, error) = await CreateOrderAsync("GIAM50K");
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(0m, order!.ShippingFee);
        Assert.Equal(750000m, order.TotalAmount);
    }

    [Fact]
    public async Task UT030_TongTien_GiamLamTamTinhDuoiNguong_PhiTinhTheoTamTinh()
    {
        CartItem(520000, 1);
        MockCoupon("GIAM50K", 50000);
        Step1("Giỏ 1 × 520.000 (tạm tính 520.000); mã \"GIAM50K\" (ICouponService mock) giảm 50.000 ⇒ sau giảm 470.000 < 500.000");

        var (order, error) = await CreateOrderAsync("GIAM50K");
        ReportOrder(order, error);

        Assert.Null(error);
        Assert.Equal(0m, order!.ShippingFee);
        Assert.Equal(470000m, order.TotalAmount);
    }
}
