using ClothingAPI.Data;
using ClothingAPI.DTOs.Reviews;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class ReviewService : IReviewService
{
    private readonly AppDbContext _context;

    public ReviewService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<PublicReviewDto>> GetProductReviewsAsync(int productId, int page, int pageSize)
    {
        var productExists = await _context.Products.AnyAsync(x => x.ProductId == productId && x.IsActive);

        if (!productExists)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm.");
        }

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var reviews = _context.Reviews
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x =>
                x.ProductId == productId &&
                x.IsApproved)
            .AsQueryable();

        var totalItems = await reviews.CountAsync();

        var reviewList = await reviews
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<PublicReviewDto>
        {
            Items = reviewList.Select(x => new PublicReviewDto
            {
                ReviewId = x.ReviewId,
                UserFullName = x.User.FullName,
                Rating = x.Rating,
                Comment = x.Comment,
                CreatedAt = x.CreatedAt
            }).ToList(),

            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<AdminReviewDto> CreateAsync(int userId, CreateReviewDto dto)
    {
        var orderItem = await _context.OrderItems
            .Include(x => x.Order)
            .Include(x => x.Variant)
            .FirstOrDefaultAsync(x => x.OrderItemId == dto.OrderItemId);

        if (orderItem is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm trong đơn hàng.");
        }

        if (orderItem.Order.UserId != userId)
        {
            throw new BadRequestException(
                "Bạn không có quyền đánh giá sản phẩm này."
            );
        }

        if (orderItem.Order.OrderStatus != OrderStatus.Completed)
        {
            throw new BadRequestException(
                "Bạn chỉ có thể đánh giá sau khi đơn hàng đã hoàn thành."
            );
        }

        var reviewExists = await _context.Reviews.AnyAsync(x => x.OrderItemId == dto.OrderItemId);

        if (reviewExists)
        {
            throw new BadRequestException(
                "Sản phẩm trong đơn hàng này đã được đánh giá."
            );
        }

        var review = new Review
        {
            ProductId = orderItem.Variant.ProductId,
            UserId = userId,
            OrderItemId = dto.OrderItemId,
            Rating = dto.Rating,
            Comment = NormalizeNullableText(dto.Comment),
            IsApproved = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        return await GetAdminReviewDtoAsync(review.ReviewId);
    }

    public async Task<AdminReviewDto> UpdateAsync(int userId, int reviewId, UpdateReviewDto dto)

    {
        var review = await _context.Reviews
            .FirstOrDefaultAsync(x => x.ReviewId == reviewId && x.UserId == userId);

        if (review is null)
        {
            throw new NotFoundException("Không tìm thấy đánh giá.");
        }

        review.Rating = dto.Rating;
        review.Comment = NormalizeNullableText(dto.Comment);

        // Sửa review cần Admin duyệt lại.
        review.IsApproved = false;

        await _context.SaveChangesAsync();

        return await GetAdminReviewDtoAsync(reviewId);
    }

    public async Task DeleteAsync(int userId, int reviewId)
    {
        var review = await _context.Reviews
            .FirstOrDefaultAsync(x =>
                x.ReviewId == reviewId &&
                x.UserId == userId);

        if (review is null)
        {
            throw new NotFoundException("Không tìm thấy đánh giá.");
        }

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();
    }


    public async Task<PagedResult<MyReviewDto>> GetMyReviewsAsync(int userId, MyReviewQueryDto query)

    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100);

        var reviews = _context.Reviews
            .AsNoTracking()
            .Include(x => x.Product)
            .Where(x => x.UserId == userId);

        var totalItems = await reviews.CountAsync();

        var reviewList = await reviews
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<MyReviewDto>
        {
            Items = reviewList.Select(x => new MyReviewDto
            {
                ReviewId = x.ReviewId,
                ProductId = x.ProductId,
                ProductName = x.Product.ProductName,
                OrderItemId = x.OrderItemId,
                Rating = x.Rating,
                Comment = x.Comment,
                IsApproved = x.IsApproved,
                CreatedAt = x.CreatedAt
            }).ToList(),

            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<PagedResult<AdminReviewDto>> GetAllForAdminAsync(AdminReviewQueryDto query)

    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100);

        var reviews = _context.Reviews
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.User)
            .AsQueryable();

        if (query.IsApproved.HasValue)
        {
            reviews = reviews.Where(x => x.IsApproved == query.IsApproved.Value);

        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();

            reviews = reviews.Where(x =>
                x.Product.ProductName.ToLower().Contains(keyword) ||
                x.User.FullName.ToLower().Contains(keyword) ||
                x.User.Email.ToLower().Contains(keyword));
        }

        var totalItems = await reviews.CountAsync();

        var reviewList = await reviews
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<AdminReviewDto>
        {
            Items = reviewList.Select(MapAdminReviewDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task SetApprovalAsync(int reviewId, bool isApproved)
    {
        var review = await _context.Reviews.FirstOrDefaultAsync(x => x.ReviewId == reviewId);

        if (review is null)
        {
            throw new NotFoundException("Không tìm thấy đánh giá.");
        }

        review.IsApproved = isApproved;

        await _context.SaveChangesAsync();
    }

    private async Task<AdminReviewDto> GetAdminReviewDtoAsync(int reviewId)
    {
        var review = await _context.Reviews
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.ReviewId == reviewId);

        if (review is null)
        {
            throw new NotFoundException("Không tìm thấy đánh giá.");
        }

        return MapAdminReviewDto(review);
    }

    private static AdminReviewDto MapAdminReviewDto(Review review)
    {
        return new AdminReviewDto
        {
            ReviewId = review.ReviewId,
            ProductId = review.ProductId,
            ProductName = review.Product.ProductName,
            UserId = review.UserId,
            UserFullName = review.User.FullName,
            UserEmail = review.User.Email,
            OrderItemId = review.OrderItemId,
            Rating = review.Rating,
            Comment = review.Comment,
            IsApproved = review.IsApproved,
            CreatedAt = review.CreatedAt
        };
    }

   

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}