using ClothingAPI.DTOs.Reviews;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/reviews")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AdminReviewDto>>> Create([FromBody] CreateReviewDto dto)
    {
        var userId = User.GetUserId();

        var result = await _reviewService.CreateAsync(userId, dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<AdminReviewDto>(
                true,
                "Gửi đánh giá thành công. Đánh giá đang chờ duyệt.",
                result
            ));
    }

    [HttpPut("{reviewId:int}")]
    public async Task<ActionResult<ApiResponse<AdminReviewDto>>> Update(int reviewId, [FromBody] UpdateReviewDto dto)
    {
        var userId = User.GetUserId();

        var result = await _reviewService.UpdateAsync(userId, reviewId, dto);

        return Ok(new ApiResponse<AdminReviewDto>(
            true,
            "Cập nhật đánh giá thành công. Đánh giá đang chờ duyệt lại.",
            result
        ));
    }

    [HttpDelete("{reviewId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int reviewId)
    {
        var userId = User.GetUserId();

        await _reviewService.DeleteAsync(userId, reviewId);

        return Ok(new ApiResponse<object>(
            true,
            "Xóa đánh giá thành công."
        ));
    }
}