using ClothingAPI.DTOs.Coupons;
using ClothingAPI.Helpers;

namespace ClothingAPI.Services.Interfaces
{
    public interface ICouponService
    {
        Task<List<CouponDto>> GetAllAsync();
        Task<CouponDto> GetByIdAsync(int couponId);
        Task<CouponDto> CreateAsync(int adminUserId, UpsertCouponDto dto);
        Task<CouponDto> UpdateAsync(int couponId, UpsertCouponDto dto);
        Task UpdateStatusAsync(int couponId, bool isActive);
        Task<CouponValidationDto> ValidateForCartAsync(int userId, ValidateCouponDto dto);
        Task<PagedResult<CouponUsageDto>> GetUsagesAsync(int couponId, int page, int pageSize);

        Task<List<AvailableCouponDto>> GetAvailableForUserAsync(int userId);

    }
}
