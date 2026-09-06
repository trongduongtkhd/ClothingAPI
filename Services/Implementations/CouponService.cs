using ClothingAPI.Data;
using ClothingAPI.DTOs.Coupons;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class CouponService : ICouponService
{
    private readonly AppDbContext _context;
    public CouponService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<CouponDto>> GetAllAsync()
    {
        var coupons = await _context.Coupons
            .AsNoTracking()
            .Include(x => x.CouponCategories)
            .Include(x => x.CouponProducts)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return coupons.Select(MapToDto).ToList();
    }

    public async Task<CouponDto> GetByIdAsync(int couponId)
    {
        var coupon = await GetCouponWithTargetsAsync(couponId);

        return MapToDto(coupon);
    }

    public async Task<CouponDto> CreateAsync(int adminUserId, UpsertCouponDto dto)
    {
        ValidateCouponData(dto);

        var code = dto.Code.Trim().ToUpper();

        var codeExists = await _context.Coupons.AnyAsync(x => x.Code == code);

        if (codeExists)
        {
            throw new BadRequestException("Mã giảm giá đã tồn tại.");
        }

        var categoryIds = dto.CategoryIds.Distinct().ToList();
        var productIds = dto.ProductIds.Distinct().ToList();

        await ValidateTargetIdsAsync(categoryIds, productIds);

        var coupon = new Coupon
        {
            Code = code,
            Name = dto.Name.Trim(),
            Description = NormalizeNullableText(dto.Description),
            DiscountType = dto.DiscountType,
            DiscountValue = dto.DiscountValue,
            MaxDiscountAmount = dto.MaxDiscountAmount,
            MinOrderAmount = dto.MinOrderAmount,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            UsageLimit = dto.UsageLimit,
            UsageLimitPerUser = dto.UsageLimitPerUser,
            UsedCount = 0,
            IsActive = dto.IsActive,
            CreatedByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var categoryId in categoryIds)
        {
            coupon.CouponCategories.Add(new CouponCategory
            {
                CategoryId = categoryId
            });
        }

        foreach (var productId in productIds)
        {
            coupon.CouponProducts.Add(new CouponProduct
            {
                ProductId = productId
            });
        }

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(coupon.CouponId);
    }

    public async Task<CouponDto> UpdateAsync(int couponId, UpsertCouponDto dto)
    {
        ValidateCouponData(dto);

        var coupon = await _context.Coupons
            .Include(x => x.CouponCategories)
            .Include(x => x.CouponProducts)
            .FirstOrDefaultAsync(x => x.CouponId == couponId);

        if (coupon is null)
        {
            throw new NotFoundException("Không tìm thấy mã giảm giá.");
        }

        var code = dto.Code.Trim().ToUpper();

        var codeExists = await _context.Coupons.AnyAsync(x => x.Code == code && x.CouponId != couponId);

        if (codeExists)
        {
            throw new BadRequestException("Mã giảm giá đã tồn tại.");
        }

        var categoryIds = dto.CategoryIds.Distinct().ToList();
        var productIds = dto.ProductIds.Distinct().ToList();

        await ValidateTargetIdsAsync(categoryIds, productIds);

        coupon.Code = code;
        coupon.Name = dto.Name.Trim();
        coupon.Description = NormalizeNullableText(dto.Description);
        coupon.DiscountType = dto.DiscountType;
        coupon.DiscountValue = dto.DiscountValue;
        coupon.MaxDiscountAmount = dto.MaxDiscountAmount;
        coupon.MinOrderAmount = dto.MinOrderAmount;
        coupon.StartDate = dto.StartDate;
        coupon.EndDate = dto.EndDate;
        coupon.UsageLimit = dto.UsageLimit;
        coupon.UsageLimitPerUser = dto.UsageLimitPerUser;
        coupon.IsActive = dto.IsActive;
        coupon.UpdatedAt = DateTime.UtcNow;

        _context.CouponCategories.RemoveRange(coupon.CouponCategories);
        _context.CouponProducts.RemoveRange(coupon.CouponProducts);

        coupon.CouponCategories = categoryIds.Select(x => new CouponCategory
        {
            CouponId = couponId,
            CategoryId = x
        }).ToList();

        coupon.CouponProducts = productIds.Select(x => new CouponProduct
        {
            CouponId = couponId,
            ProductId = x
        }).ToList();

        await _context.SaveChangesAsync();

        return await GetByIdAsync(couponId);
    }

    public async Task UpdateStatusAsync(int couponId, bool isActive)
    {
        var coupon = await _context.Coupons.FirstOrDefaultAsync(x => x.CouponId == couponId);

        if (coupon is null)
        {
            throw new NotFoundException("Không tìm thấy mã giảm giá.");
        }

        coupon.IsActive = isActive;
        coupon.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task<CouponValidationDto> ValidateForCartAsync(int userId, ValidateCouponDto dto)
    {
        var code = dto.Code.Trim().ToUpper();

        var coupon = await _context.Coupons
            .AsNoTracking()
            .Include(x => x.CouponCategories)
            .Include(x => x.CouponProducts)
            .FirstOrDefaultAsync(x => x.Code == code);

        if (coupon is null)
        {
            throw new BadRequestException("Mã giảm giá không tồn tại.");
        }

        var now = DateTime.UtcNow;

        if (!coupon.IsActive)
        {
            throw new BadRequestException("Mã giảm giá hiện không hoạt động.");
        }

        if (now < coupon.StartDate)
        {
            throw new BadRequestException("Mã giảm giá chưa đến thời gian sử dụng.");
        }

        if (now > coupon.EndDate)
        {
            throw new BadRequestException("Mã giảm giá đã hết hạn.");
        }

        if (coupon.UsageLimit.HasValue &&
            coupon.UsedCount >= coupon.UsageLimit.Value)
        {
            throw new BadRequestException("Mã giảm giá đã hết lượt sử dụng.");
        }

        if (coupon.UsageLimitPerUser.HasValue)
        {
            var userUsedCount = await _context.CouponUsages
                .CountAsync(x => x.CouponId == coupon.CouponId && x.UserId == userId);

            if (userUsedCount >= coupon.UsageLimitPerUser.Value)
            {
                throw new BadRequestException(
                    "Bạn đã sử dụng mã giảm giá này quá số lần cho phép."
                );
            }
        }

        var cartItems = await _context.CartItems
            .AsNoTracking()
            .Include(x => x.Variant)
                .ThenInclude(x => x.Product)
            .Where(x => x.UserId == userId)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            throw new BadRequestException("Giỏ hàng đang trống.");
        }

        var categoryIds = coupon.CouponCategories.Select(x => x.CategoryId).ToHashSet();

        var productIds = coupon.CouponProducts.Select(x => x.ProductId).ToHashSet();

        var hasTargetRestriction = categoryIds.Count > 0 || productIds.Count > 0;

        decimal subtotal = 0;
        decimal eligibleSubtotal = 0;

        foreach (var cartItem in cartItems)
        {
            var unitPrice = cartItem.Variant.SalePrice ?? cartItem.Variant.Price;

            var itemTotal = unitPrice * cartItem.Quantity;

            subtotal += itemTotal;

            var isEligible = !hasTargetRestriction || productIds.Contains(cartItem.Variant.ProductId) ||

                             categoryIds.Contains(cartItem.Variant.Product.CategoryId);

            if (isEligible)
            {
                eligibleSubtotal += itemTotal;
            }
        }

        if (eligibleSubtotal == 0)
        {
            throw new BadRequestException(
                "Mã giảm giá không áp dụng cho sản phẩm trong giỏ."
            );
        }

        if (eligibleSubtotal < coupon.MinOrderAmount)
        {
            throw new BadRequestException(
                $"Đơn hàng hợp lệ phải từ {coupon.MinOrderAmount:N0}đ để dùng mã này."
            );
        }

        var discountAmount = CalculateDiscount(coupon, eligibleSubtotal);

        return new CouponValidationDto
        {
            CouponId = coupon.CouponId,
            CouponCode = coupon.Code,
            CouponName = coupon.Name,
            Subtotal = subtotal,
            EligibleSubtotal = eligibleSubtotal,
            DiscountAmount = discountAmount,
            AmountAfterDiscount = subtotal - discountAmount
        };
    }

    private async Task<Coupon> GetCouponWithTargetsAsync(int couponId)
    {
        var coupon = await _context.Coupons
            .AsNoTracking()
            .Include(x => x.CouponCategories)
            .Include(x => x.CouponProducts)
            .FirstOrDefaultAsync(x => x.CouponId == couponId);

        if (coupon is null)
        {
            throw new NotFoundException("Không tìm thấy mã giảm giá.");
        }

        return coupon;
    }

    private async Task ValidateTargetIdsAsync(List<int> categoryIds, List<int> productIds)

    {
        if (categoryIds.Count > 0)
        {
            var validCategoryCount = await _context.Categories.CountAsync(x => categoryIds.Contains(x.CategoryId));

            if (validCategoryCount != categoryIds.Count)
            {
                throw new BadRequestException(
                    "Có danh mục áp dụng mã không tồn tại."
                );
            }
        }

        if (productIds.Count > 0)
        {
            var validProductCount = await _context.Products
                .CountAsync(x => productIds.Contains(x.ProductId));

            if (validProductCount != productIds.Count)
            {
                throw new BadRequestException(
                    "Có sản phẩm áp dụng mã không tồn tại."
                );
            }
        }
    }

    private static void ValidateCouponData(UpsertCouponDto dto)
    {
        if (dto.EndDate <= dto.StartDate)
        {
            throw new BadRequestException(
                "Thời gian kết thúc phải sau thời gian bắt đầu."
            );
        }

        if (dto.DiscountType == DiscountType.Percentage &&
            dto.DiscountValue > 100)
        {
            throw new BadRequestException(
                "Giá trị giảm theo phần trăm không được vượt quá 100%."
            );
        }

        if (dto.DiscountType == DiscountType.FixedAmount &&
            dto.MaxDiscountAmount.HasValue)
        {
            throw new BadRequestException(
                "Mã giảm theo số tiền cố định không cần giảm tối đa."
            );
        }
    }

    private static decimal CalculateDiscount(Coupon coupon, decimal eligibleSubtotal)

    {
        decimal discountAmount;

        if (coupon.DiscountType == DiscountType.Percentage)
        {
            discountAmount = eligibleSubtotal * coupon.DiscountValue / 100;

            if (coupon.MaxDiscountAmount.HasValue)
            {
                discountAmount = Math.Min(
                    discountAmount,
                    coupon.MaxDiscountAmount.Value
                );
            }
        }
        else
        {
            discountAmount = coupon.DiscountValue;
        }

        return Math.Min(discountAmount, eligibleSubtotal);
    }

    private static CouponDto MapToDto(Coupon coupon)
    {
        return new CouponDto
        {
            CouponId = coupon.CouponId,
            Code = coupon.Code,
            Name = coupon.Name,
            Description = coupon.Description,
            DiscountType = coupon.DiscountType,
            DiscountValue = coupon.DiscountValue,
            MaxDiscountAmount = coupon.MaxDiscountAmount,
            MinOrderAmount = coupon.MinOrderAmount,
            StartDate = coupon.StartDate,
            EndDate = coupon.EndDate,
            UsageLimit = coupon.UsageLimit,
            UsageLimitPerUser = coupon.UsageLimitPerUser,
            UsedCount = coupon.UsedCount,
            IsActive = coupon.IsActive,
            CategoryIds = coupon.CouponCategories
                .Select(x => x.CategoryId)
                .ToList(),
            ProductIds = coupon.CouponProducts
                .Select(x => x.ProductId)
                .ToList()
        };
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public async Task<PagedResult<CouponUsageDto>> GetUsagesAsync(int couponId, int page, int pageSize)

    {
        var couponExists = await _context.Coupons
            .AnyAsync(x => x.CouponId == couponId);

        if (!couponExists)
        {
            throw new NotFoundException("Không tìm thấy mã giảm giá.");
        }

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var usages = _context.CouponUsages
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Order)
            .Where(x => x.CouponId == couponId)
            .AsQueryable();

        var totalItems = await usages.CountAsync();

        var usageList = await usages
            .OrderByDescending(x => x.UsedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<CouponUsageDto>
        {
            Items = usageList.Select(x => new CouponUsageDto
            {
                CouponUsageId = x.CouponUsageId,
                UserId = x.UserId,
                UserFullName = x.User.FullName,
                UserEmail = x.User.Email,
                OrderId = x.OrderId,
                OrderCode = x.Order.OrderCode,
                DiscountAmount = x.DiscountAmount,
                UsedAt = x.UsedAt
            }).ToList(),

            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }
}