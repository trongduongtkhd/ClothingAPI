using ClothingAPI.DTOs.Chat;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Implementations;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/chat")]
[AllowAnonymous]
[EnableRateLimiting("chat")]
public class ChatController(IChatService chat) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(32768)]
    public async Task<ActionResult<ApiResponse<ChatResponseDto>>> Reply(ChatRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(new ApiResponse<ChatResponseDto>(true, "Đã trả lời.", await chat.ReplyAsync(request, cancellationToken)));
        }
        catch (ChatUnavailableException error)
        {
            return StatusCode(503, new ApiResponse<ChatResponseDto>(false, error.Message));
        }
    }
}
