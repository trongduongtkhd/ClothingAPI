using ClothingAPI.DTOs.PurchaseOrders;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// PurchaseOrderService.CreateAsync / UpdateAsync / CancelAsync
/// (giá vốn bình quân tính trong các hàm private ApplyStockIn / ReverseStockIn).
public class StockCostTests : UnitTestBase
{
    public StockCostTests(ITestOutputHelper output) : base(output) { }

    private (int Stock, decimal? Cost) VariantState(int variantId)
    {
        var variant = Db.Query(db => db.ProductVariants.Single(v => v.VariantId == variantId));
        return (variant.StockQuantity, variant.AverageCostPrice);
    }

    private string State(int variantId)
    {
        var (stock, cost) = VariantState(variantId);
        return $"tồn = {stock}, giá vốn = {(cost.HasValue ? Money(cost.Value) : "null")}";
    }

    private CreatePurchaseOrderDto Dto(int variantId, int quantity, decimal unitCost) => new()
    {
        SupplierId = Db.SupplierId,
        Items = [new CreatePurchaseOrderItemDto { VariantId = variantId, Quantity = quantity, UnitCost = unitCost }]
    };

    private async Task<Exception?> CreateAsync(int variantId, int quantity, decimal unitCost)
    {
        await using var context = Db.NewContext();
        return await CaptureAsync(() => new PurchaseOrderService(context).CreateAsync(Db.AdminId, Dto(variantId, quantity, unitCost)));
    }

    private async Task<Exception?> UpdateAsync(int purchaseOrderId, int variantId, int quantity, decimal unitCost)
    {
        await using var context = Db.NewContext();
        return await CaptureAsync(() => new PurchaseOrderService(context).UpdateAsync(purchaseOrderId, Dto(variantId, quantity, unitCost)));
    }

    private async Task<Exception?> CancelAsync(int purchaseOrderId, string reason)
    {
        await using var context = Db.NewContext();
        return await CaptureAsync(() => new PurchaseOrderService(context).CancelAsync(purchaseOrderId, new CancelPurchaseOrderDto { Reason = reason }));
    }

    [Fact]
    public async Task PurchaseAmountExceedsDatabasePrecision_IsRejectedWithoutChangingStock()
    {
        var (variantId, _) = Db.AddVariant(stock: 10, averageCost: 100000);
        var error = await CreateAsync(variantId, 2, 5000000000000000m);
        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Tổng tiền phiếu nhập vượt giới hạn.", error!.Message);
        Assert.Equal((10, (decimal?)100000m), VariantState(variantId));
        Assert.Equal(0, Db.Query(db => db.PurchaseOrders.Count()));
    }

    [Fact]
    public async Task UT045_GiaVon_NhapTheoViDuSRS_120000()
    {
        var (variantId, _) = Db.AddVariant(stock: 10, averageCost: 100000);
        Step1("Biến thể tồn 10, giá vốn 100.000; tạo phiếu nhập 20 cái giá 130.000");

        var error = await CreateAsync(variantId, 20, 130000);
        Actual($"{Describe(error)}; {State(variantId)}");

        var (stock, cost) = VariantState(variantId);
        Assert.Null(error);
        Assert.Equal(30, stock);
        Assert.Equal(120000m, cost);
    }

    [Fact]
    public async Task UT046_GiaVon_NhapKhiTon0_BangGiaNhap()
    {
        var (variantId, _) = Db.AddVariant(stock: 0, averageCost: null);
        Step1("Biến thể tồn 0, chưa có giá vốn; tạo phiếu nhập 15 cái giá 90.000");

        var error = await CreateAsync(variantId, 15, 90000);
        Actual($"{Describe(error)}; {State(variantId)}");

        var (stock, cost) = VariantState(variantId);
        Assert.Null(error);
        Assert.Equal(15, stock);
        Assert.Equal(90000m, cost);
    }

    [Fact]
    public async Task UT047_GiaVon_LamTron2ChuSo()
    {
        var (variantId, _) = Db.AddVariant(stock: 3, averageCost: 100000);
        Step1("Biến thể tồn 3, giá vốn 100.000; tạo phiếu nhập 4 cái giá 101.000 ((3×100.000 + 4×101.000)/7 = 100.571,428…)");

        var error = await CreateAsync(variantId, 4, 101000);
        Actual($"{Describe(error)}; {State(variantId)}");

        var (stock, cost) = VariantState(variantId);
        Assert.Null(error);
        Assert.Equal(7, stock);
        Assert.Equal(100571.43m, cost);
    }

    [Fact]
    public async Task UT048_GiaVon_HoanTacKhiHuyPhieu_TinhLaiDung()
    {
        var (variantId, _) = Db.AddVariant(stock: 30, averageCost: 120000);
        var purchaseOrderId = Db.AddPurchaseOrder(variantId, 20, 130000);
        Step1("Biến thể tồn 30, giá vốn 120.000; phiếu nhập (Completed) 20 cái giá 130.000; huỷ phiếu với lý do \"Nhập nhầm\"");

        var error = await CancelAsync(purchaseOrderId, "Nhập nhầm");
        Actual($"{Describe(error)}; {State(variantId)}");

        var (stock, cost) = VariantState(variantId);
        Assert.Null(error);
        Assert.Equal(10, stock);
        Assert.Equal(100000m, cost);
    }

    [Fact]
    public async Task UT049_GiaVon_HoanTacVeTon0_GiuGiaVonCu()
    {
        var (variantId, _) = Db.AddVariant(stock: 15, averageCost: 90000);
        var purchaseOrderId = Db.AddPurchaseOrder(variantId, 15, 90000);
        Step1("Biến thể tồn 15, giá vốn 90.000; phiếu nhập (Completed) 15 cái giá 90.000; huỷ phiếu với lý do \"Nhập nhầm\"");

        var error = await CancelAsync(purchaseOrderId, "Nhập nhầm");
        Actual($"{Describe(error)}; {State(variantId)}");

        var (stock, cost) = VariantState(variantId);
        Assert.Null(error);
        Assert.Equal(0, stock);
        Assert.Equal(90000m, cost);
    }

    [Fact]
    public async Task UT050_SuaPhieu_LamTonAm_BiTuChoi()
    {
        var (variantId, sku) = Db.AddVariant(stock: 5, averageCost: 100000);
        var purchaseOrderId = Db.AddPurchaseOrder(variantId, 20, 100000);
        Step1($"Biến thể {sku} tồn hiện tại 5; phiếu nhập cũ 20 cái; sửa phiếu thành 10 cái");

        var error = await UpdateAsync(purchaseOrderId, variantId, 10, 100000);
        Actual($"{Describe(error)}; {State(variantId)}");

        Assert.IsType<BadRequestException>(error);
        Assert.StartsWith($"Không thể sửa: biến thể {sku} hiện chỉ còn tồn 5, không đủ để giảm từ 20 xuống 10", error!.Message);
        Assert.Equal(5, VariantState(variantId).Stock);
    }

    [Fact]
    public async Task UT051_SuaPhieu_TonVuaBang0_DuocChapNhan()
    {
        var (variantId, _) = Db.AddVariant(stock: 10, averageCost: 100000);
        var purchaseOrderId = Db.AddPurchaseOrder(variantId, 20, 100000);
        Step1("Biến thể tồn hiện tại 10; phiếu nhập cũ 20 cái giá 100.000; sửa phiếu thành 10 cái giá 100.000");

        var error = await UpdateAsync(purchaseOrderId, variantId, 10, 100000);
        Actual($"{Describe(error)}; {State(variantId)}");

        Assert.Null(error);
        Assert.Equal(0, VariantState(variantId).Stock);
    }

    [Fact]
    public async Task UT052_HuyPhieu_TonKhongDu_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 5, averageCost: 100000);
        var purchaseOrderId = Db.AddPurchaseOrder(variantId, 20, 100000);
        Step1("Biến thể tồn hiện tại 5; phiếu đã nhập 20 cái; huỷ phiếu với lý do \"Nhập nhầm\"");

        var error = await CancelAsync(purchaseOrderId, "Nhập nhầm");
        var status = Db.Query(db => db.PurchaseOrders.Single(p => p.PurchaseOrderId == purchaseOrderId).Status);
        Actual($"{Describe(error)}; {State(variantId)}; trạng thái phiếu = {status}");

        Assert.IsType<BadRequestException>(error);
        Assert.StartsWith("Không thể hủy phiếu vì hàng đã được bán bớt", error!.Message);
    }

    [Fact]
    public async Task UT053_HuyPhieu_ThieuLyDo_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 20, averageCost: 100000);
        var purchaseOrderId = Db.AddPurchaseOrder(variantId, 20, 100000);
        var dto = new CancelPurchaseOrderDto { Reason = "" };
        Step1("Phiếu nhập (Completed) 20 cái, tồn 20; yêu cầu huỷ với lý do rỗng \"\" (quy tắc nằm ở [Required] của CancelPurchaseOrderDto)");

        // Quy tắc bắt buộc nhập lý do được cài bằng DataAnnotations trên DTO (ASP.NET [ApiController] kiểm tra trước khi gọi service).
        var errors = ValidateDto(dto);
        Exception? serviceError = null;
        if (errors.Count == 0)
        {
            serviceError = await CancelAsync(purchaseOrderId, dto.Reason);
        }
        var status = Db.Query(db => db.PurchaseOrders.Single(p => p.PurchaseOrderId == purchaseOrderId).Status);
        Actual(errors.Count > 0
            ? $"validation DTO trả lỗi: \"{string.Join("; ", errors)}\"; service không được gọi; trạng thái phiếu giữ = {status}"
            : $"DTO hợp lệ, service: {Describe(serviceError)}; trạng thái phiếu = {status}");

        Assert.Contains("Vui lòng nhập lý do hủy phiếu.", errors);
        Assert.Equal(PurchaseOrderStatuses.Completed, status);
    }

    [Fact]
    public async Task UT054_HuyPhieu_PhieuDaHuy_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 20, averageCost: 100000);
        var purchaseOrderId = Db.AddPurchaseOrder(variantId, 20, 100000, PurchaseOrderStatuses.Cancelled);
        Step1("Phiếu nhập 20 cái đang ở trạng thái Cancelled (Đã huỷ); huỷ lại với lý do \"Huỷ lần 2\"");

        var error = await CancelAsync(purchaseOrderId, "Huỷ lần 2");
        Actual($"{Describe(error)}; {State(variantId)}");

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Phiếu nhập kho đã bị hủy, không thể thao tác thêm.", error!.Message);
    }
}
