using ClothingAPI.DTOs.Storefront;
using ClothingAPI.Enums;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;

namespace ClothingAPI.UnitTests;

public class StorefrontCategoryFilterTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly int _rootId;
    private readonly int _shirtsId;
    private readonly int _winterId;
    private readonly int _hiddenId;
    private readonly int _brandId;
    private readonly int _directProductId;
    private readonly int _shirtProductId;
    private readonly int _winterProductId;
    private readonly int _pantsProductId;

    public StorefrontCategoryFilterTests()
    {
        using var context = _db.NewContext();
        var root = Category("Nam");
        var shirts = Category("Áo Nam", root);
        var winter = Category("Áo Mùa Đông", shirts);
        var pants = Category("Quần Nam", root);
        var other = Category("Nữ");
        var hidden = Category("Danh mục tắt", root, false);
        var belowHidden = Category("Con của danh mục tắt", hidden);
        var brand = new Brand { BrandName = "Test brand" };
        var directProduct = Product("Quần gán trực tiếp", root, Gender.Unisex, 100);
        var shirtProduct = Product("Áo nam", shirts, Gender.Male, 200, brand: brand);
        var winterProduct = Product("Áo mùa đông", winter, Gender.Male, 400, 250, brand);
        var pantsProduct = Product("Quần", pants, Gender.Female, 300);
        var inactiveProduct = Product("Sản phẩm tắt", shirts, Gender.Male, 100);
        inactiveProduct.IsActive = false;
        context.AddRange(directProduct, shirtProduct, winterProduct, pantsProduct, inactiveProduct,
            Product("Ngoài nhánh", other, Gender.Male, 150),
            Product("Trong danh mục tắt", hidden, Gender.Male, 100),
            Product("Dưới nhánh tắt", belowHidden, Gender.Male, 100));
        context.SaveChanges();
        _rootId = root.CategoryId;
        _shirtsId = shirts.CategoryId;
        _winterId = winter.CategoryId;
        _hiddenId = hidden.CategoryId;
        _brandId = brand.BrandId;
        _directProductId = directProduct.ProductId;
        _shirtProductId = shirtProduct.ProductId;
        _winterProductId = winterProduct.ProductId;
        _pantsProductId = pantsProduct.ProductId;
    }

    private static Category Category(string name, Category? parent = null, bool active = true) => new()
    {
        CategoryName = name, Slug = Guid.NewGuid().ToString("N"), ParentCategory = parent, IsActive = active
    };

    private static Product Product(string name, Category category, Gender gender, decimal price,
        decimal? salePrice = null, Brand? brand = null) => new()
    {
        ProductName = name, Slug = Guid.NewGuid().ToString("N"), Description = name,
        Category = category, Gender = gender, BasePrice = price, SalePrice = salePrice, Brand = brand
    };

    private async Task<Helpers.PagedResult<PublicProductListDto>> GetProducts(PublicProductQueryDto query)
    {
        await using var context = _db.NewContext();
        return await new StorefrontService(context).GetProductsAsync(query);
    }

    [Fact]
    public async Task Parent_IncludesDirectProductsAndAllActiveDescendants_ExcludesOtherAndInactiveBranches()
    {
        var result = await GetProducts(new() { CategoryId = _rootId });
        Assert.Equal(4, result.TotalItems);
        Assert.Equal(new[] { _directProductId, _shirtProductId, _winterProductId, _pantsProductId }.Order(),
            result.Items.Select(x => x.ProductId).Order());
    }

    [Fact]
    public async Task Child_IncludesOwnDescendants_ExcludesParentAndSiblingProducts()
    {
        var result = await GetProducts(new() { CategoryId = _shirtsId });
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(new[] { _shirtProductId, _winterProductId }.Order(), result.Items.Select(x => x.ProductId).Order());
        var leaf = await GetProducts(new() { CategoryId = _winterId });
        Assert.Equal(_winterProductId, Assert.Single(leaf.Items).ProductId);
    }

    [Theory]
    [InlineData(Gender.Male, 2)]
    [InlineData(Gender.Female, 1)]
    [InlineData(Gender.Unisex, 1)]
    public async Task Gender_RemainsAnExactIndependentFilter(Gender gender, int expectedCount)
    {
        var result = await GetProducts(new() { CategoryId = _rootId, Gender = gender });
        Assert.Equal(expectedCount, result.TotalItems);
    }

    [Fact]
    public async Task Branch_CombinesWithBrandKeywordGenderAndEffectivePrice()
    {
        var result = await GetProducts(new()
        {
            CategoryId = _rootId, BrandId = _brandId, Keyword = "mùa đông", Gender = Gender.Male,
            MinPrice = 225, MaxPrice = 275, Sort = "priceAsc"
        });
        Assert.Equal(_winterProductId, Assert.Single(result.Items).ProductId);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Branch_IsAppliedBeforeCountingSortingAndPagination()
    {
        var first = await GetProducts(new() { CategoryId = _rootId, Sort = "priceAsc", PageSize = 2 });
        var second = await GetProducts(new() { CategoryId = _rootId, Sort = "priceAsc", PageSize = 2, Page = 2 });
        Assert.Equal(4, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(4, second.TotalItems);
        Assert.Equal(new[] { _directProductId, _shirtProductId }, first.Items.Select(x => x.ProductId));
        Assert.Equal(new[] { _winterProductId, _pantsProductId }, second.Items.Select(x => x.ProductId));
    }

    [Fact]
    public async Task InactiveOrUnknownCategory_ReturnsNoProducts()
    {
        foreach (var categoryId in new[] { _hiddenId, int.MaxValue })
        {
            var result = await GetProducts(new() { CategoryId = categoryId });
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalItems);
        }
    }

    public void Dispose() => _db.Dispose();
}
