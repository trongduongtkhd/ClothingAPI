using ClothingAPI.DTOs.Reviews;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/admin/reviews")]
[Authorize(Roles = "Admin")]
public class AdminReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public AdminReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminReviewDto>>>> GetAll([FromQuery] AdminReviewQueryDto query)

    {
        var result = await _reviewService.GetAllForAdminAsync(query);

        return Ok(new ApiResponse<PagedResult<AdminReviewDto>>(
            true,
            "Lấy danh sách đánh giá thành công.",
            result
        ));
    }

    [HttpPut("{reviewId:int}/approve")]
    public async Task<ActionResult<ApiResponse<object>>> Approve(int reviewId)
    {
        await _reviewService.SetApprovalAsync(reviewId, true);

        return Ok(new ApiResponse<object>(
            true,
            "Duyệt đánh giá thành công."
        ));
    }

    [HttpPut("{reviewId:int}/reject")]
    public async Task<ActionResult<ApiResponse<object>>> Reject(int reviewId)
    {
        await _reviewService.SetApprovalAsync(reviewId, false);

        return Ok(new ApiResponse<object>(
            true,
            "Đã ẩn hoặc từ chối đánh giá."
        ));
    }
}