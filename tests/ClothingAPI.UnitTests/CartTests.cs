using ClothingAPI.DTOs.Cart;
using ClothingAPI.Exceptions;
using ClothingAPI.Services.Implementations;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// CartService.AddItemAsync / UpdateItemAsync: validate DTO và giới hạn sau cộng dồn tại service.
public class CartTests : UnitTestBase
{
    public CartTests(ITestOutputHelper output) : base(output) { }

    private string CartState() => Db.Query(db =>
    {
        var rows = db.CartItems.Where(c => c.UserId == Db.CustomerId).Select(c => c.Quantity).ToList();
        return $"giỏ có {rows.Count} dòng" + (rows.Count > 0 ? $", số lượng = {string.Join(", ", rows)}" : "");
    });

    private async Task<(List<string> DtoErrors, Exception? Error)> AddAsync(int variantId, int quantity)
    {
        var dto = new AddCartItemDto { VariantId = variantId, Quantity = quantity };
        var dtoErrors = ValidateDto(dto);
        if (dtoErrors.Count > 0)
        {
            return (dtoErrors, null);
        }

        await using var context = Db.NewContext();
        return (dtoErrors, await CaptureAsync(() => new CartService(context).AddItemAsync(Db.CustomerId, dto)));
    }

    private async Task<(List<string> DtoErrors, Exception? Error)> UpdateAsync(int cartItemId, int quantity)
    {
        var dto = new UpdateCartItemDto { Quantity = quantity };
        var dtoErrors = ValidateDto(dto);
        if (dtoErrors.Count > 0)
        {
            return (dtoErrors, null);
        }

        await using var context = Db.NewContext();
        return (dtoErrors, await CaptureAsync(() => new CartService(context).UpdateItemAsync(Db.CustomerId, cartItemId, dto)));
    }

    private void Report(List<string> dtoErrors, Exception? error) =>
        Actual(dtoErrors.Count > 0
            ? $"validation DTO trả lỗi \"{string.Join("; ", dtoErrors)}\" (service không được gọi); {CartState()}"
            : $"{Describe(error)}; {CartState()}");

    [Theory]
    [InlineData(99, 1, true)]
    [InlineData(99, 2, false)]
    [InlineData(100, 1, false)]
    [InlineData(int.MaxValue, 2, false)]
    public async Task AddItem_ValidatesAccumulatedQuantity_WithoutOverflow(int existing, int added, bool accepted)
    {
        var (variantId, _) = Db.AddVariant(stock: int.MaxValue);
        Db.AddCartItem(Db.CustomerId, variantId, existing);
        var (dtoErrors, error) = await AddAsync(variantId, added);
        Assert.Empty(dtoErrors);
        if (accepted)
            Assert.Null(error);
        else
        {
            Assert.IsType<BadRequestException>(error);
            Assert.Equal("Số lượng phải từ 1 đến 100.", error!.Message);
        }
        Assert.Equal(accepted ? existing + added : existing,
            Db.Query(db => db.CartItems.Single(c => c.VariantId == variantId).Quantity));
    }

    [Fact]
    public async Task UT055_ThemGio_HopLe_TaoDongMoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        Step1("Biến thể tồn 10; giỏ của khách đang trống; thêm 2");

        var (dtoErrors, error) = await AddAsync(variantId, 2);
        Report(dtoErrors, error);

        var rows = Db.Query(db => db.CartItems.Where(c => c.UserId == Db.CustomerId).ToList());
        Assert.Empty(dtoErrors);
        Assert.Null(error);
        Assert.Single(rows);
        Assert.Equal(2, rows[0].Quantity);
    }

    [Fact]
    public async Task UT056_ThemGio_DaCo_CongDon()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        Db.AddCartItem(Db.CustomerId, variantId, 2);
        Step1("Biến thể tồn 10; giỏ đã có 1 dòng số lượng 2 của biến thể này; thêm 1");

        var (dtoErrors, error) = await AddAsync(variantId, 1);
        Report(dtoErrors, error);

        var rows = Db.Query(db => db.CartItems.Where(c => c.UserId == Db.CustomerId).ToList());
        Assert.Null(error);
        Assert.Single(rows);
        Assert.Equal(3, rows[0].Quantity);
    }

    [Fact]
    public async Task UT057_ThemGio_CongDonVuotTon_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 5);
        Db.AddCartItem(Db.CustomerId, variantId, 4);
        Step1("Biến thể tồn 5; giỏ đã có số lượng 4; thêm 2 (cộng dồn 6 > 5)");

        var (dtoErrors, error) = await AddAsync(variantId, 2);
        Report(dtoErrors, error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Số lượng trong giỏ không được vượt quá tồn kho (5).", error!.Message);
    }

    [Fact]
    public async Task UT058_ThemGio_HetHang_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 0);
        Step1("Biến thể tồn 0; giỏ trống; thêm 1");

        var (dtoErrors, error) = await AddAsync(variantId, 1);
        Report(dtoErrors, error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Sản phẩm đã hết hàng.", error!.Message);
    }

    [Fact]
    public async Task UT059_ThemGio_NgungKinhDoanh_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 10, isActive: false);
        Step1("Biến thể tồn 10 nhưng IsActive = false (ngừng kinh doanh); thêm 1");

        var (dtoErrors, error) = await AddAsync(variantId, 1);
        Report(dtoErrors, error);

        Assert.IsType<BadRequestException>(error);
        Assert.Equal("Biến thể sản phẩm hiện không còn được bán.", error!.Message);
    }

    [Fact]
    public async Task UT060_CapNhatSoLuong_0_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        var cartItemId = Db.AddCartItem(Db.CustomerId, variantId, 3);
        Step1("Biến thể tồn 10; giỏ có dòng số lượng 3; cập nhật số lượng = 0");

        var (dtoErrors, error) = await UpdateAsync(cartItemId, 0);
        Report(dtoErrors, error);

        Assert.Contains("Số lượng phải từ 1 đến 100.", dtoErrors);
    }

    [Fact]
    public async Task UT061_CapNhatSoLuong_1_HopLe()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        var cartItemId = Db.AddCartItem(Db.CustomerId, variantId, 3);
        Step1("Biến thể tồn 10; giỏ có dòng số lượng 3; cập nhật số lượng = 1");

        var (dtoErrors, error) = await UpdateAsync(cartItemId, 1);
        Report(dtoErrors, error);

        Assert.Empty(dtoErrors);
        Assert.Null(error);
        Assert.Equal(1, Db.Query(db => db.CartItems.Single(c => c.CartItemId == cartItemId).Quantity));
    }

    [Fact]
    public async Task UT062_CapNhatSoLuong_100_HopLe()
    {
        var (variantId, _) = Db.AddVariant(stock: 200);
        var cartItemId = Db.AddCartItem(Db.CustomerId, variantId, 1);
        Step1("Biến thể tồn 200; giỏ có dòng số lượng 1; cập nhật số lượng = 100");

        var (dtoErrors, error) = await UpdateAsync(cartItemId, 100);
        Report(dtoErrors, error);

        Assert.Empty(dtoErrors);
        Assert.Null(error);
        Assert.Equal(100, Db.Query(db => db.CartItems.Single(c => c.CartItemId == cartItemId).Quantity));
    }

    [Fact]
    public async Task UT063_CapNhatSoLuong_101_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 200);
        var cartItemId = Db.AddCartItem(Db.CustomerId, variantId, 1);
        Step1("Biến thể tồn 200; giỏ có dòng số lượng 1; cập nhật số lượng = 101");

        var (dtoErrors, error) = await UpdateAsync(cartItemId, 101);
        Report(dtoErrors, error);

        Assert.Contains("Số lượng phải từ 1 đến 100.", dtoErrors);
        Assert.Equal(1, Db.Query(db => db.CartItems.Single(c => c.CartItemId == cartItemId).Quantity));
    }

    [Fact]
    public async Task UT064_CapNhatSoLuong_VuotTon_BiTuChoi()
    {
        var (variantId, _) = Db.AddVariant(stock: 10);
        var cartItemId = Db.AddCartItem(Db.CustomerId, variantId, 1);
        Step1("Biến thể tồn 10; giỏ có dòng số lượng 1; cập nhật số lượng = 11");

        var (dtoErrors, error) = await UpdateAsync(cartItemId, 11);
        Report(dtoErrors, error);

        Assert.IsType<BadRequestException>(error);
        Assert.Contains("vượt quá tồn kho (10)", error!.Message);
    }
}
