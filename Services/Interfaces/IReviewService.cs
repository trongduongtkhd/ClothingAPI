using ClothingAPI.DTOs.Reviews;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface IReviewService
    {
        Task<PagedResult<PublicReviewDto>> GetProductReviewsAsync(int productId, int page, int pageSize);
        Task<AdminReviewDto> CreateAsync(int userId, CreateReviewDto dto);
        Task<AdminReviewDto> UpdateAsync(int userId, int reviewId, UpdateReviewDto dto);
        Task DeleteAsync(int userId, int reviewId);
        Task<PagedResult<AdminReviewDto>> GetAllForAdminAsync(AdminReviewQueryDto query);
        Task SetApprovalAsync(int reviewId, bool isApproved);
       

    }
}
