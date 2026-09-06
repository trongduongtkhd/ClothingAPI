using ClothingAPI.Data;
using ClothingAPI.DTOs.Products;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class ProductService : IProductService
{
    private readonly AppDbContext _context;
    public ProductService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ProductListDto>> GetAllAsync(ProductQueryDto query)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100);

        var products = _context.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.ProductImages)
            .Include(x => x.ProductVariants)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();

            products = products.Where(x => x.ProductName.ToLower().Contains(keyword));
        }

        if (query.IsActive.HasValue)
        {
            products = products.Where(x => x.IsActive == query.IsActive.Value);
        }

        var totalItems = await products.CountAsync();

        var items = await products
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProductListDto
            {
                ProductId = x.ProductId,
                ProductName = x.ProductName,
                Slug = x.Slug,
                CategoryName = x.Category.CategoryName,
                BrandName = x.Brand != null ? x.Brand.BrandName : null,
                BasePrice = x.BasePrice,
                SalePrice = x.SalePrice,
                ThumbnailUrl = x.ProductImages
                    .Where(image => image.IsThumbnail)
                    .OrderBy(image => image.DisplayOrder)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                TotalStockQuantity = x.ProductVariants
                    .Where(variant => variant.IsActive)
                    .Sum(variant => (int?)variant.StockQuantity) ?? 0,
                IsFeatured = x.IsFeatured,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt
            }).ToListAsync();


        return new PagedResult<ProductListDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<ProductDetailDto> GetByIdAsync(int productId)
    {
        var product = await GetProductWithDetailsAsync(productId);

        return MapProductDetail(product);
    }

    public async Task<ProductDetailDto> CreateAsync(UpsertProductDto dto)
    {
        ValidatePrice(dto.BasePrice, dto.SalePrice);

        await ValidateCategoryAndBrandAsync(dto.CategoryId, dto.BrandId);

        var product = new Product
        {
            CategoryId = dto.CategoryId,
            BrandId = dto.BrandId,
            ProductName = dto.ProductName.Trim(),
            Slug = await GenerateUniqueSlugAsync(dto.ProductName),
            ShortDescription = NormalizeNullableText(dto.ShortDescription),
            Description = dto.Description.Trim(),
            Material = NormalizeNullableText(dto.Material),
            Gender = dto.Gender,
            BasePrice = dto.BasePrice,
            SalePrice = dto.SalePrice,
            IsFeatured = dto.IsFeatured,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(product.ProductId);
    }

    public async Task<ProductDetailDto> UpdateAsync(int productId, UpsertProductDto dto)
    {
        ValidatePrice(dto.BasePrice, dto.SalePrice);

        var product = await _context.Products
            .FirstOrDefaultAsync(x => x.ProductId == productId);

        if (product is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm.");
        }

        await ValidateCategoryAndBrandAsync(dto.CategoryId, dto.BrandId);

        product.CategoryId = dto.CategoryId;
        product.BrandId = dto.BrandId;
        product.ProductName = dto.ProductName.Trim();
        product.Slug = await GenerateUniqueSlugAsync(dto.ProductName, productId);
        product.ShortDescription = NormalizeNullableText(dto.ShortDescription);
        product.Description = dto.Description.Trim();
        product.Material = NormalizeNullableText(dto.Material);
        product.Gender = dto.Gender;
        product.BasePrice = dto.BasePrice;
        product.SalePrice = dto.SalePrice;
        product.IsFeatured = dto.IsFeatured;
        product.IsActive = dto.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await GetByIdAsync(productId);
    }

    public async Task DeleteAsync(int productId)
    {
        var product = await _context.Products
            .Include(x => x.ProductVariants)
            .FirstOrDefaultAsync(x => x.ProductId == productId);

        if (product is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm.");
        }

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;

        foreach (var variant in product.ProductVariants)
        {
            variant.IsActive = false;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<ProductVariantDto> CreateVariantAsync(int productId, UpsertProductVariantDto dto)

    {
        ValidatePrice(dto.Price, dto.SalePrice);

        await EnsureProductExistsAsync(productId);
        await ValidateColorAndSizeAsync(dto.ColorId, dto.SizeId);

        var sku = dto.SKU.Trim().ToUpper();

        var skuExists = await _context.ProductVariants
            .AnyAsync(x => x.SKU.ToUpper() == sku);

        if (skuExists)
        {
            throw new BadRequestException("SKU đã tồn tại.");
        }

        var duplicateVariant = await _context.ProductVariants.AnyAsync(x =>
            x.ProductId == productId &&
            x.ColorId == dto.ColorId &&
            x.SizeId == dto.SizeId);

        if (duplicateVariant)
        {
            throw new BadRequestException(
                "Biến thể có màu và size này đã tồn tại."
            );
        }

        var variant = new ProductVariant
        {
            ProductId = productId,
            ColorId = dto.ColorId,
            SizeId = dto.SizeId,
            SKU = sku,
            Price = dto.Price,
            SalePrice = dto.SalePrice,
            StockQuantity = dto.StockQuantity,
            ImageUrl = NormalizeNullableText(dto.ImageUrl),
            IsActive = dto.IsActive
        };

        _context.ProductVariants.Add(variant);
        await _context.SaveChangesAsync();

        return await GetVariantDtoAsync(variant.VariantId);
    }

    public async Task<ProductVariantDto> UpdateVariantAsync(int productId, int variantId, UpsertProductVariantDto dto)
    {
        ValidatePrice(dto.Price, dto.SalePrice);

        var variant = await _context.ProductVariants.FirstOrDefaultAsync(x => x.VariantId == variantId && x.ProductId == productId);

        if (variant is null)
        {
            throw new NotFoundException("Không tìm thấy biến thể sản phẩm.");
        }

        await ValidateColorAndSizeAsync(dto.ColorId, dto.SizeId);

        var sku = dto.SKU.Trim().ToUpper();

        var skuExists = await _context.ProductVariants.AnyAsync(x => x.SKU.ToUpper() == sku && x.VariantId != variantId);

        if (skuExists)
        {
            throw new BadRequestException("SKU đã tồn tại.");
        }

        var duplicateVariant = await _context.ProductVariants.AnyAsync(x =>
            x.ProductId == productId &&
            x.ColorId == dto.ColorId &&
            x.SizeId == dto.SizeId &&
            x.VariantId != variantId);

        if (duplicateVariant)
        {
            throw new BadRequestException(
                "Biến thể có màu và size này đã tồn tại."
            );
        }

        variant.ColorId = dto.ColorId;
        variant.SizeId = dto.SizeId;
        variant.SKU = sku;
        variant.Price = dto.Price;
        variant.SalePrice = dto.SalePrice;
        variant.StockQuantity = dto.StockQuantity;
        variant.ImageUrl = NormalizeNullableText(dto.ImageUrl);
        variant.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return await GetVariantDtoAsync(variantId);
    }

    public async Task DeleteVariantAsync(int productId, int variantId)
    {
        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(x =>
                x.VariantId == variantId &&
                x.ProductId == productId);

        if (variant is null)
        {
            throw new NotFoundException("Không tìm thấy biến thể sản phẩm.");
        }

        variant.IsActive = false;

        await _context.SaveChangesAsync();
    }

    public async Task<ProductImageDto> CreateImageAsync(int productId, UpsertProductImageDto dto)
    {
        await EnsureProductExistsAsync(productId);

        if (dto.IsThumbnail)
        {
            await ClearProductThumbnailAsync(productId);
        }

        var image = new ProductImage
        {
            ProductId = productId,
            ImageUrl = dto.ImageUrl.Trim(),
            DisplayOrder = dto.DisplayOrder,
            IsThumbnail = dto.IsThumbnail
        };

        _context.ProductImages.Add(image);
        await _context.SaveChangesAsync();

        return MapImageDto(image);
    }

    public async Task<ProductImageDto> UpdateImageAsync(int productId, int imageId, UpsertProductImageDto dto)
    {
        var image = await _context.ProductImages
            .FirstOrDefaultAsync(x =>
                x.ImageId == imageId &&
                x.ProductId == productId);

        if (image is null)
        {
            throw new NotFoundException("Không tìm thấy ảnh sản phẩm.");
        }

        if (dto.IsThumbnail)
        {
            await ClearProductThumbnailAsync(productId, imageId);
        }

        image.ImageUrl = dto.ImageUrl.Trim();
        image.DisplayOrder = dto.DisplayOrder;
        image.IsThumbnail = dto.IsThumbnail;

        await _context.SaveChangesAsync();

        return MapImageDto(image);
    }

    public async Task DeleteImageAsync(int productId, int imageId)
    {
        var image = await _context.ProductImages
            .FirstOrDefaultAsync(x =>
                x.ImageId == imageId &&
                x.ProductId == productId);

        if (image is null)
        {
            throw new NotFoundException("Không tìm thấy ảnh sản phẩm.");
        }

        _context.ProductImages.Remove(image);
        await _context.SaveChangesAsync();
    }

    private async Task<Product> GetProductWithDetailsAsync(int productId)
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
            .FirstOrDefaultAsync(x => x.ProductId == productId);

        if (product is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm.");
        }

        return product;
    }

    private async Task EnsureProductExistsAsync(int productId)
    {
        var exists = await _context.Products
            .AnyAsync(x => x.ProductId == productId);

        if (!exists)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm.");
        }
    }

    private async Task ValidateCategoryAndBrandAsync(int categoryId, int? brandId)
    {
        var categoryExists = await _context.Categories.AnyAsync(x => x.CategoryId == categoryId);
        if (!categoryExists)
        {
            throw new BadRequestException("Danh mục không tồn tại.");
        }

        if (brandId.HasValue)
        {
            var brandExists = await _context.Brands
                .AnyAsync(x => x.BrandId == brandId.Value);

            if (!brandExists)
            {
                throw new BadRequestException("Thương hiệu không tồn tại.");
            }
        }
    }

    private async Task ValidateColorAndSizeAsync(int colorId, int sizeId)
    {
        var colorExists = await _context.Colors
            .AnyAsync(x => x.ColorId == colorId);

        if (!colorExists)
        {
            throw new BadRequestException("Màu sắc không tồn tại.");
        }

        var sizeExists = await _context.Sizes
            .AnyAsync(x => x.SizeId == sizeId);

        if (!sizeExists)
        {
            throw new BadRequestException("Size không tồn tại.");
        }
    }

    private async Task<ProductVariantDto> GetVariantDtoAsync(int variantId)
    {
        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(x => x.Color)
            .Include(x => x.Size)
            .FirstOrDefaultAsync(x => x.VariantId == variantId);

        if (variant is null)
        {
            throw new NotFoundException("Không tìm thấy biến thể sản phẩm.");
        }

        return MapVariantDto(variant);
    }

    private async Task ClearProductThumbnailAsync(int productId, int? excludedImageId = null)
    {
        var oldThumbnails = await _context.ProductImages
            .Where(x =>
                x.ProductId == productId &&
                x.IsThumbnail &&
                (!excludedImageId.HasValue ||
                 x.ImageId != excludedImageId.Value))
            .ToListAsync();

        foreach (var oldThumbnail in oldThumbnails)
        {
            oldThumbnail.IsThumbnail = false;
        }
    }

    private async Task<string> GenerateUniqueSlugAsync(string productName, int? excludedProductId = null)
    {
        var baseSlug = SlugHelper.Generate(productName);

        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            throw new BadRequestException(
                "Không thể tạo slug từ tên sản phẩm."
            );
        }

        var slug = baseSlug;
        var number = 2;

        while (await _context.Products.AnyAsync(x =>
            x.Slug == slug &&
            (!excludedProductId.HasValue ||
             x.ProductId != excludedProductId.Value)))
        {
            slug = $"{baseSlug}-{number}";
            number++;
        }

        return slug;
    }

    private static void ValidatePrice(decimal price, decimal? salePrice)
    {
        if (salePrice.HasValue && salePrice.Value > price)
        {
            throw new BadRequestException(
                "Giá giảm không được lớn hơn giá gốc."
            );
        }
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ProductDetailDto MapProductDetail(Product product)
    {
        return new ProductDetailDto
        {
            ProductId = product.ProductId,
            CategoryId = product.CategoryId,
            CategoryName = product.Category.CategoryName,
            BrandId = product.BrandId,
            BrandName = product.Brand?.BrandName,
            ProductName = product.ProductName,
            Slug = product.Slug,
            ShortDescription = product.ShortDescription,
            Description = product.Description,
            Material = product.Material,
            Gender = product.Gender,
            BasePrice = product.BasePrice,
            SalePrice = product.SalePrice,
            IsFeatured = product.IsFeatured,
            IsActive = product.IsActive,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            Variants = product.ProductVariants
                .OrderBy(x => x.Color.ColorName)
                .ThenBy(x => x.Size.DisplayOrder)
                .Select(MapVariantDto)
                .ToList(),
            Images = product.ProductImages
                .OrderByDescending(x => x.IsThumbnail)
                .ThenBy(x => x.DisplayOrder)
                .Select(MapImageDto)
                .ToList()
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
}