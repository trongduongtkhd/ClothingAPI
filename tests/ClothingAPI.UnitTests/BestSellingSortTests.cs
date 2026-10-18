using ClothingAPI.DTOs.Storefront;
using ClothingAPI.Enums;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Implementations;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// StorefrontService.GetProductsAsync với Sort = "bestSelling" (trang Sản phẩm → "Bán chạy").
public class BestSellingSortTests : UnitTestBase
{
    public BestSellingSortTests(ITestOutputHelper output) : base(output) { }

    private int ProductIdOf(int variantId) =>
        Db.Query(db => db.ProductVariants.Single(v => v.VariantId == variantId).ProductId);

    private async Task<PagedResult<PublicProductListDto>> GetBestSellingAsync(int? categoryId = null)
    {
        await using var context = Db.NewContext();
        return await new StorefrontService(context).GetProductsAsync(new PublicProductQueryDto
        {
            Sort = "bestSelling",
            CategoryId = categoryId
        });
    }

    private static string Order(PagedResult<PublicProductListDto> result, Dictionary<int, string> labels) =>
        string.Join(" → ", result.Items.Select(x => labels.TryGetValue(x.ProductId, out var label) ? label : x.ProductName));

    [Fact]
    public async Task UT071_BanChay_SapXepTheoSoLuongDaBan()
    {
        var (a, _) = Db.AddVariant();
        var (b, _) = Db.AddVariant();
        var (c, _) = Db.AddVariant();
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(a, 2)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(b, 10)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(c, 5)]);
        var labels = new Dictionary<int, string> { [ProductIdOf(a)] = "A(2)", [ProductIdOf(b)] = "B(10)", [ProductIdOf(c)] = "C(5)" };
        Step1("3 sản phẩm A, B, C; đơn Completed bán lần lượt 2, 10, 5 cái; lọc sort = bestSelling");

        var result = await GetBestSellingAsync();
        Actual($"thứ tự: {Order(result, labels)}");

        Assert.Equal([ProductIdOf(b), ProductIdOf(c), ProductIdOf(a)], result.Items.Select(x => x.ProductId));
    }

    [Fact]
    public async Task UT072_BanChay_SanPhamCuBanNhieu_DungTruocSanPhamMoi()
    {
        var (oldProduct, _) = Db.AddVariant();
        var (newProduct, _) = Db.AddVariant();
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(oldProduct, 7)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(newProduct, 1)]);
        var labels = new Dictionary<int, string> { [ProductIdOf(oldProduct)] = "Cũ(7)", [ProductIdOf(newProduct)] = "Mới(1)" };
        Step1("Sản phẩm tạo trước bán 7 cái, sản phẩm tạo sau bán 1 cái; sort = bestSelling");

        var result = await GetBestSellingAsync();
        Actual($"thứ tự: {Order(result, labels)}");

        Assert.Equal(ProductIdOf(oldProduct), result.Items[0].ProductId);
    }

    [Fact]
    public async Task UT073_BanChay_KhongTinhDonHuyVaDonChuaHoanThanh()
    {
        var (a, _) = Db.AddVariant();
        var (b, _) = Db.AddVariant();
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(a, 3)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Cancelled, items: [(b, 20)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Pending, items: [(b, 15)]);
        var labels = new Dictionary<int, string> { [ProductIdOf(a)] = "A(Completed 3)", [ProductIdOf(b)] = "B(Cancelled 20 + Pending 15)" };
        Step1("A: đơn Completed 3 cái; B (tạo sau): đơn Cancelled 20 cái + đơn Pending 15 cái; sort = bestSelling");

        var result = await GetBestSellingAsync();
        Actual($"thứ tự: {Order(result, labels)}");

        Assert.Equal([ProductIdOf(a), ProductIdOf(b)], result.Items.Select(x => x.ProductId));
    }

    [Fact]
    public async Task UT074_BanChay_CongDonNhieuDon()
    {
        var (a, _) = Db.AddVariant();
        var (b, _) = Db.AddVariant();
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(a, 4)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(a, 4)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(b, 6)]);
        var labels = new Dictionary<int, string> { [ProductIdOf(a)] = "A(4+4)", [ProductIdOf(b)] = "B(6)" };
        Step1("A: 2 đơn Completed mỗi đơn 4 cái (tổng 8); B (tạo sau): 1 đơn Completed 6 cái; sort = bestSelling");

        var result = await GetBestSellingAsync();
        Actual($"thứ tự: {Order(result, labels)}");

        Assert.Equal([ProductIdOf(a), ProductIdOf(b)], result.Items.Select(x => x.ProductId));
    }

    [Fact]
    public async Task UT075_BanChay_KetHopLocDanhMuc()
    {
        var (ao1, _) = Db.AddVariant(categoryId: Db.CategoryAoId);
        var (quan, _) = Db.AddVariant(categoryId: Db.CategoryQuanId);
        var (ao2, _) = Db.AddVariant(categoryId: Db.CategoryAoId);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(ao1, 9)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(quan, 50)]);
        Db.AddOrder(Db.CustomerId, OrderStatus.Completed, items: [(ao2, 1)]);
        var labels = new Dictionary<int, string> { [ProductIdOf(ao1)] = "Áo1(9)", [ProductIdOf(quan)] = "Quần(50)", [ProductIdOf(ao2)] = "Áo2(1)" };
        Step1("Áo1 bán 9, Quần bán 50, Áo2 (tạo sau) bán 1; lọc danh mục Áo + sort = bestSelling");

        var result = await GetBestSellingAsync(Db.CategoryAoId);
        Actual($"tổng = {result.TotalItems}; thứ tự: {Order(result, labels)}");

        Assert.Equal(2, result.TotalItems);
        Assert.Equal([ProductIdOf(ao1), ProductIdOf(ao2)], result.Items.Select(x => x.ProductId));
    }
}
