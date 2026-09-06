using ClothingAPI.Data;
using ClothingAPI.DTOs.Colors;
using ClothingAPI.DTOs.Products;
using ClothingAPI.DTOs.Sizes;
using ClothingAPI.DTOs.Storefront;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class StorefrontService : IStorefrontService
{
    private readonly AppDbContext _context;

    public StorefrontService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<PublicCategoryDto>> GetCategoriesAsync()
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.CategoryName)
            .ToListAsync();

        var categoryMap = categories.ToDictionary(
            x => x.CategoryId,
            x => new PublicCategoryDto
            {
                CategoryId = x.CategoryId,
                CategoryName = x.CategoryName,
                Description = x.Description,
                Slug = x.Slug,
                ImageUrl = x.ImageUrl
            }
        );

        var rootCategories = new List<PublicCategoryDto>();

        foreach (var category in categories)
        {
            var categoryDto = categoryMap[category.CategoryId];

            if (category.ParentCategoryId.HasValue &&
                categoryMap.TryGetValue(category.ParentCategoryId.Value, out var parentCategory))

            {
                parentCategory.Children.Add(categoryDto);
            }
            else
            {
                rootCategories.Add(categoryDto);
            }
        }

        return rootCategories;
    }

    public async Task<List<PublicBrandDto>> GetBrandsAsync()
    {
        var brands = await _context.Brands
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.BrandName)
            .ToListAsync();

        return brands.Select(x => new PublicBrandDto
        {
            BrandId = x.BrandId,
            BrandName = x.BrandName,
            LogoUrl = x.LogoUrl
        }).ToList();
    }

    public async Task<PagedResult<PublicProductListDto>> GetProductsAsync(PublicProductQueryDto query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 12 : Math.Min(query.PageSize, 100);

        var products = _context.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.ProductImages)
            .Where(x => x.IsActive && x.Category.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();

            products = products.Where(x => x.ProductName.ToLower().Contains(keyword));
        }

        if (query.CategoryId.HasValue)
        {
            products = products.Where(x =>
                x.CategoryId == query.CategoryId.Value);
        }

        if (query.BrandId.HasValue)
        {
            products = products.Where(x =>
                x.BrandId == query.BrandId.Value);
        }

        if (query.Gender.HasValue)
        {
            products = products.Where(x =>
                x.Gender == query.Gender.Value);
        }

        if (query.MinPrice.HasValue)
        {
            products = products.Where(x =>
                (x.SalePrice ?? x.BasePrice) >= query.MinPrice.Value);
        }

        if (query.MaxPrice.HasValue)
        {
            products = products.Where(x =>
                (x.SalePrice ?? x.BasePrice) <= query.MaxPrice.Value);
        }

        products = query.Sort?.Trim().ToLower() switch
        {
            "priceasc" => products.OrderBy(x => x.SalePrice ?? x.BasePrice),
            "pricedesc" => products.OrderByDescending(
                x => x.SalePrice ?? x.BasePrice
            ),

            // Chưa có OrderItems nên tạm thời sắp xếp newest.
            // Ta sẽ cập nhật bestSelling khi làm Order.
            "bestselling" => products.OrderByDescending(x => x.CreatedAt),

            _ => products.OrderByDescending(x => x.CreatedAt)
        };

        var totalItems = await products.CountAsync();

        var productList = await products
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<PublicProductListDto>
        {
            Items = productList.Select(MapProductListDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<List<PublicProductListDto>> GetFeaturedProductsAsync(int limit)
    {
        limit = NormalizeLimit(limit);

        var products = await _context.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.ProductImages)
            .Where(x => x.IsActive && x.IsFeatured && x.Category.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return products.Select(MapProductListDto).ToList();
    }

    public async Task<List<PublicProductListDto>> GetNewArrivalProductsAsync(int limit)
    {
        limit = NormalizeLimit(limit);

        var products = await _context.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.ProductImages)
            .Where(x => x.IsActive && x.Category.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return products.Select(MapProductListDto).ToList();
    }

    public async Task<PublicProductDetailDto> GetProductBySlugAsync(string slug)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.ProductImages)
            .Include(x => x.ProductVariants)
                .ThenInclude(x => x.Color)
            .Include(x => x.ProductVariants)
                .ThenInclude(x => x.Size)
            .FirstOrDefaultAsync(x =>
                x.Slug == slug &&
                x.IsActive &&
                x.Category.IsActive);

        if (product is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm.");
        }

        return new PublicProductDetailDto
        {
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            Slug = product.Slug,
            CategoryName = product.Category.CategoryName,
            BrandName = product.Brand?.BrandName,
            ShortDescription = product.ShortDescription,
            Description = product.Description,
            Material = product.Material,
            Gender = product.Gender,
            BasePrice = product.BasePrice,
            SalePrice = product.SalePrice,

            Images = product.ProductImages
                .OrderByDescending(x => x.IsThumbnail)
                .ThenBy(x => x.DisplayOrder)
                .Select(MapImageDto)
                .ToList(),

            Variants = product.ProductVariants
                .Where(x => x.IsActive)
                .OrderBy(x => x.Color.ColorName)
                .ThenBy(x => x.Size.DisplayOrder)
                .Select(MapVariantDto)
                .ToList()
        };
    }

    public async Task<List<PublicProductListDto>> GetRelatedProductsAsync(int productId, int limit)

    {
        limit = NormalizeLimit(limit);

        var currentProduct = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.ProductId == productId &&
                x.IsActive);

        if (currentProduct is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm.");
        }

        var relatedProducts = await _context.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.ProductImages)
            .Where(x =>
                x.ProductId != productId &&
                x.CategoryId == currentProduct.CategoryId &&
                x.IsActive &&
                x.Category.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return relatedProducts.Select(MapProductListDto).ToList();
    }

    private static int NormalizeLimit(int limit)
    {
        return limit < 1 ? 4 : Math.Min(limit, 20);
    }

    private static PublicProductListDto MapProductListDto(Product product)
    {
        var thumbnail = product.ProductImages
            .Where(x => x.IsThumbnail)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => x.ImageUrl)
            .FirstOrDefault()
            ?? product.ProductImages
                .OrderBy(x => x.DisplayOrder)
                .Select(x => x.ImageUrl)
                .FirstOrDefault();

        return new PublicProductListDto
        {
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            Slug = product.Slug,
            CategoryName = product.Category.CategoryName,
            BrandName = product.Brand?.BrandName,
            BasePrice = product.BasePrice,
            SalePrice = product.SalePrice,
            ThumbnailUrl = thumbnail,
            IsFeatured = product.IsFeatured
        };
    }

    private static ProductVariantDto MapVariantDto(ProductVariant variant)
    {
        return new ProductVariantDto
        {
            VariantId = variant.VariantId,
            ColorId = variant.ColorId,
            ColorName = variant.Color.ColorName,
            ColorCode = variant.Color.ColorCode,
            SizeId = variant.SizeId,
            SizeName = variant.Size.SizeName,
            SKU = variant.SKU,
            Price = variant.Price,
            SalePrice = variant.SalePrice,
            StockQuantity = variant.StockQuantity,
            ImageUrl = variant.ImageUrl,
            IsActive = variant.IsActive
        };
    }

    private static ProductImageDto MapImageDto(ProductImage image)
    {
        return new ProductImageDto
        {
            ImageId = image.ImageId,
            ImageUrl = image.ImageUrl,
            DisplayOrder = image.DisplayOrder,
            IsThumbnail = image.IsThumbnail
        };
    }

    public async Task<PublicCategoryDto> GetCategoryBySlugAsync(string slug)
    {
        var category = await _context.Categories
            .AsNoTracking() 
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive);

        if (category is null)
        {
            throw new NotFoundException("Không tìm thấy danh mục.");
        }

        return new PublicCategoryDto
        {
            CategoryId = category.CategoryId,
            CategoryName = category.CategoryName,
            Slug = category.Slug,
            Description = category.Description,
            ImageUrl = category.ImageUrl
        };
    }

    public async Task<List<ColorDto>> GetColorsAsync()
    {
        return await _context.Colors
            .AsNoTracking()
            .OrderBy(x => x.ColorName)
            .Select(x => new ColorDto
            {
                ColorId = x.ColorId,
                ColorName = x.ColorName,
                ColorCode = x.ColorCode
            })
            .ToListAsync();
    }

    public async Task<List<SizeDto>> GetSizesAsync()
    {
        return await _context.Sizes
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.SizeName)
            .Select(x => new SizeDto
            {
                SizeId = x.SizeId,
                SizeName = x.SizeName,
                DisplayOrder = x.DisplayOrder
            })
            .ToListAsync();
    }
}