using ClothingAPI.DTOs.Uploads;
using ClothingAPI.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/uploads")]
[Authorize]
public class ProfileUploadsController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    private static readonly string[] AllowedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    ];

    private const long MaxFileSize = 5 * 1024 * 1024;

    public ProfileUploadsController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<UploadImageDto>>> UploadAvatar([FromForm] UploadImageRequestDto dto)
    {
        var file = dto.File;

        if (file is null || file.Length == 0)
        {
            return BadRequest(new ApiResponse<object>(
                false,
                "Vui lòng chọn ảnh đại diện."
            ));
        }

        if (file.Length > MaxFileSize)
        {
            return BadRequest(new ApiResponse<object>(
                false,
                "Ảnh không được lớn hơn 5 MB."
            ));
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest(new ApiResponse<object>(
                false,
                "Chỉ hỗ trợ file JPG, JPEG, PNG hoặc WEBP."
            ));
        }

        var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");


        var uploadFolder = Path.Combine(webRootPath, "uploads", "avatars");

        Directory.CreateDirectory(uploadFolder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadFolder, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        var imageUrl = $"{Request.Scheme}://{Request.Host}/uploads/avatars/{fileName}";

        return Ok(new ApiResponse<UploadImageDto>(
            true,
            "Upload ảnh đại diện thành công.",
            new UploadImageDto
            {
                ImageUrl = imageUrl,
                FileName = fileName
            }
        ));
    }
}