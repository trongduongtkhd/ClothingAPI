using ClothingAPI.DTOs.Coupons;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Services.Implementations;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// Kiểm tra CouponService.ValidateForCartAsync (hợp lệ + số tiền giảm, qua hàm private CalculateDiscount).
public class CouponTests : UnitTestBase
{
    public CouponTests(ITestOutputHelper output) : base(output) { }

    private async Task<(CouponValidationDto? Result, Exception? Error)> ApplyAsync(string code)
    {
        await using var context = Db.NewContext();
        var service = new CouponService(context);
        CouponValidationDto? result = null;
        var error = await CaptureAsync(async () =>
            result = await service.ValidateForCartAsync(Db.CustomerId, new ValidateCouponDto { Code = code }));
        return (result, error);
    }

    /// Giỏ của khách: 1 sản phẩm thuộc danh mục Áo với giá cho trước.
    private void CartWithAo(decimal price, int quantity = 1)
    {
        var (variantId, _) = Db.AddVariant(basePrice: price, stock: 100, categoryId: Db.CategoryAoId);
        Db.AddCartItem(Db.CustomerId, variantId, quantity);
    }

    private void CartWithQuan(decimal price, int quantity = 1)
    {
        var (variantId, _) = Db.AddVariant(basePrice: price, stock: 100, categoryId: Db.CategoryQuanId);
        Db.AddCartItem(Db.CustomerId, variantId, quantity);
    }

    private void ReportError(Exception? error) => Actual(Describe(error));

    private void ReportValid(CouponValidationDto? result, Exception? error) =>
        Actual(error is null
            ? $"mã hợp lệ (CouponCode = {result!.CouponCode}, giá trị áp dụng = {Money(result.EligibleSubtotal)}, số tiền giảm = {Money(result.DiscountAmount)})"
            : Describe(error));

    [Fact]
    public async Task UT001_ApDungMa_MaKhongTonTai_BiTuChoi()
    {
        CartWithAo(300000);
        Step1("CSDL không có mã \"KHONGCO\"; giỏ 1 áo 300.000; áp dụng mã \"KHONGCO\"");

        var (_, error) = await ApplyAsync("KHONGCO");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Mã giảm giá không tồn tại.", error!.Message);
    }

    [Fact]
    public async Task UT002_ApDungMa_MaDaTat_BiTuChoi()
    {
        CartWithAo(300000);
        Db.AddCoupon("TATMA", isActive: false);
        Step1("Mã \"TATMA\" tồn tại, IsActive = false; giỏ 1 áo 300.000");

        var (_, error) = await ApplyAsync("TATMA");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Mã giảm giá hiện không hoạt động.", error!.Message);
    }

    [Fact]
    public async Task UT003_ApDungMa_ChuaDenHan_BiTuChoi()
    {
        CartWithAo(300000);
        Db.AddCoupon("CHUADEN", start: DateTime.UtcNow.AddDays(1), end: DateTime.UtcNow.AddDays(10));
        Step1("Mã \"CHUADEN\": StartDate = hiện tại + 1 ngày, EndDate = hiện tại + 10 ngày; giỏ 1 áo 300.000");

        var (_, error) = await ApplyAsync("CHUADEN");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Mã giảm giá chưa đến thời gian sử dụng.", error!.Message);
    }

    [Fact]
    public async Task UT004_ApDungMa_DaHetHan_BiTuChoi()
    {
        CartWithAo(300000);
        Db.AddCoupon("HETHAN", start: DateTime.UtcNow.AddDays(-10), end: DateTime.UtcNow.AddMinutes(-1));
        Step1("Mã \"HETHAN\": EndDate = hiện tại − 1 phút; giỏ 1 áo 300.000");

        var (_, error) = await ApplyAsync("HETHAN");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Mã giảm giá đã hết hạn.", error!.Message);
    }

    [Fact]
    public async Task UT005_ApDungMa_HetTongLuot_BiTuChoi()
    {
        CartWithAo(300000);
        Db.AddCoupon("HETLUOT", usageLimit: 10, usedCount: 10);
        Step1("Mã \"HETLUOT\": UsageLimit = 10, UsedCount = 10; giỏ 1 áo 300.000");

        var (_, error) = await ApplyAsync("HETLUOT");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Mã giảm giá đã hết lượt sử dụng.", error!.Message);
    }

    [Fact]
    public async Task UT006_ApDungMa_ConMotLuot_DuocApDung()
    {
        CartWithAo(300000);
        Db.AddCoupon("CON1LUOT", usageLimit: 10, usedCount: 9);
        Step1("Mã \"CON1LUOT\" (10%): UsageLimit = 10, UsedCount = 9; giỏ 1 áo 300.000");

        var (result, error) = await ApplyAsync("CON1LUOT");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal("CON1LUOT", result!.CouponCode);
    }

    [Fact]
    public async Task UT007_ApDungMa_HetLuotCuaKhach_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(basePrice: 300000, stock: 100);
        var couponId = Db.AddCoupon("MOTLAN", usageLimitPerUser: 1);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(variantId, 1)], couponId: couponId, discount: 30000);
        Db.AddCartItem(Db.CustomerId, variantId, 1);
        Step1("Mã \"MOTLAN\": UsageLimitPerUser = 1; khách đã có 1 CouponUsage (đơn trước); giỏ 1 áo 300.000");

        var (_, error) = await ApplyAsync("MOTLAN");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Bạn đã sử dụng mã giảm giá này quá số lần cho phép.", error!.Message);
    }

    [Fact]
    public async Task UT008_ApDungMa_KhongGioiHanLuot_DuocApDung()
    {
        CartWithAo(300000);
        Db.AddCoupon("VOHAN", usageLimit: null, usageLimitPerUser: null, usedCount: 999);
        Step1("Mã \"VOHAN\" (10%): UsageLimit = null, UsageLimitPerUser = null, UsedCount = 999; giỏ 1 áo 300.000");

        var (result, error) = await ApplyAsync("VOHAN");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal("VOHAN", result!.CouponCode);
    }

    [Fact]
    public async Task UT009_ApDungMa_NgoaiPhamVi_BiTuChoi()
    {
        CartWithQuan(300000);
        Db.AddCoupon("CHIAO", categoryIds: [Db.CategoryAoId]);
        Step1("Mã \"CHIAO\" chỉ áp dụng danh mục Áo; giỏ chỉ có 1 quần 300.000");

        var (_, error) = await ApplyAsync("CHIAO");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Mã giảm giá không áp dụng cho sản phẩm trong giỏ.", error!.Message);
    }

    [Fact]
    public async Task UT010_ApDungMa_ThieuMotDongToiThieu_BiTuChoi()
    {
        CartWithAo(199999);
        Db.AddCoupon("TOITHIEU", minOrder: 200000);
        Step1("Mã \"TOITHIEU\": MinOrderAmount = 200.000; giỏ 1 áo 199.999 (giá trị áp dụng 199.999)");

        var (_, error) = await ApplyAsync("TOITHIEU");
        ReportError(error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Đơn hàng hợp lệ phải từ 200.000 đ để dùng mã này.", error!.Message);
    }

    [Fact]
    public async Task UT011_ApDungMa_VuaDuToiThieu_DuocApDung()
    {
        CartWithAo(200000);
        Db.AddCoupon("TOITHIEU", minOrder: 200000);
        Step1("Mã \"TOITHIEU\" (10%): MinOrderAmount = 200.000; giỏ 1 áo 200.000 (giá trị áp dụng 200.000)");

        var (result, error) = await ApplyAsync("TOITHIEU");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(200000m, result!.EligibleSubtotal);
    }

    [Fact]
    public async Task UT012_TinhGiam_PhanTram_DungTyLe()
    {
        CartWithAo(300000);
        Db.AddCoupon("PT10", DiscountType.Percentage, 10);
        Step1("Mã \"PT10\": giảm 10%, không giới hạn tối đa; giỏ 1 áo 300.000");

        var (result, error) = await ApplyAsync("PT10");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(31000m, result!.DiscountAmount);
    }

    [Fact]
    public async Task UT013_TinhGiam_PhanTram_BiChanBoiToiDa()
    {
        CartWithAo(800000);
        Db.AddCoupon("PT10MAX", DiscountType.Percentage, 10, maxDiscount: 50000);
        Step1("Mã \"PT10MAX\": giảm 10%, tối đa 50.000; giỏ 1 áo 800.000");

        var (result, error) = await ApplyAsync("PT10MAX");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(50000m, result!.DiscountAmount);
    }

    [Fact]
    public async Task UT014_TinhGiam_PhanTram_DungBangToiDa()
    {
        CartWithAo(500000);
        Db.AddCoupon("PT10MAX", DiscountType.Percentage, 10, maxDiscount: 50000);
        Step1("Mã \"PT10MAX\": giảm 10%, tối đa 50.000; giỏ 1 áo 500.000");

        var (result, error) = await ApplyAsync("PT10MAX");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(50000m, result!.DiscountAmount);
    }

    [Fact]
    public async Task UT015_TinhGiam_PhanTram100_GiamToanBo()
    {
        CartWithAo(250000);
        Db.AddCoupon("PT100", DiscountType.Percentage, 100);
        Step1("Mã \"PT100\": giảm 100%; giỏ 1 áo 250.000");

        var (result, error) = await ApplyAsync("PT100");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(250000m, result!.DiscountAmount);
    }

    [Fact]
    public async Task UT016_TinhGiam_CoDinh_DungSoTien()
    {
        CartWithAo(300000);
        Db.AddCoupon("CD30K", DiscountType.FixedAmount, 30000);
        Step1("Mã \"CD30K\": giảm cố định 30.000; giỏ 1 áo 300.000");

        var (result, error) = await ApplyAsync("CD30K");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(30000m, result!.DiscountAmount);
    }

    [Fact]
    public async Task UT017_TinhGiam_CoDinhLonHonGiaTri_ChiGiamBangGiaTri()
    {
        CartWithAo(80000);
        Db.AddCoupon("CD100K", DiscountType.FixedAmount, 100000);
        Step1("Mã \"CD100K\": giảm cố định 100.000; giỏ 1 áo 80.000");

        var (result, error) = await ApplyAsync("CD100K");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(80000m, result!.DiscountAmount);
    }

    [Fact]
    public async Task UT018_TinhGiam_TheoDanhMuc_ChiTinhSanPhamThuocPhamVi()
    {
        CartWithAo(200000);
        CartWithQuan(300000);
        Db.AddCoupon("AO10", DiscountType.Percentage, 10, categoryIds: [Db.CategoryAoId]);
        Step1("Mã \"AO10\": giảm 10% chỉ cho danh mục Áo; giỏ 1 áo 200.000 + 1 quần 300.000");

        var (result, error) = await ApplyAsync("AO10");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(20000m, result!.DiscountAmount);
    }

    [Fact]
    public async Task UT019_TinhGiam_ToanCuaHang_TinhTrenTamTinh()
    {
        CartWithAo(200000);
        CartWithQuan(300000);
        Db.AddCoupon("ALL10", DiscountType.Percentage, 10);
        Step1("Mã \"ALL10\": giảm 10% toàn cửa hàng; giỏ 1 áo 200.000 + 1 quần 300.000 (tạm tính 500.000)");

        var (result, error) = await ApplyAsync("ALL10");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal(500000m, result!.Subtotal);
        Assert.Equal(50000m, result.DiscountAmount);
    }

    [Fact]
    public async Task UT020_ApDungMa_MaChuThuong_DuocChuanHoa()
    {
        CartWithAo(300000);
        Db.AddCoupon("GIAM10");
        Step1("CSDL lưu mã \"GIAM10\" (10%); giỏ 1 áo 300.000; khách nhập \"giam10\"");

        var (result, error) = await ApplyAsync("giam10");
        ReportValid(result, error);

        Assert.Null(error);
        Assert.Equal("GIAM10", result!.CouponCode);
    }
}
