using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Chat;

public class ChatRequestDto : IValidatableObject
{
    [Required, StringLength(1000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    [Required, MaxLength(8)]
    public List<ChatMessageDto> History { get; set; } = [];

    [Required, MaxLength(5)]
    public List<int> ProductIds { get; set; } = [];

    [StringLength(250)]
    public string? ProductSlug { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (History?.Any(message => message is null) == true)
            yield return new ValidationResult("Lịch sử trò chuyện không hợp lệ.", [nameof(History)]);
    }
}

public class ChatMessageDto
{
    [Required, RegularExpression("^(user|assistant)$")]
    public string Role { get; set; } = "user";

    [Required, StringLength(600)]
    public string Content { get; set; } = string.Empty;
}

public class ChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public List<ChatProductDto> Products { get; set; } = [];
    public string[] Suggestions { get; set; } = [];
    public bool UsedAi { get; set; }
}

public class ChatProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? BrandName { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Material { get; set; }
    public string? Description { get; set; }
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public List<ChatVariantDto> Variants { get; set; } = [];
}

public class ChatVariantDto
{
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public decimal Price { get; set; }
}
