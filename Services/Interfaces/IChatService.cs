using ClothingAPI.DTOs.Chat;

namespace ClothingAPI.Services.Interfaces;

public interface IChatService
{
    Task<ChatResponseDto> ReplyAsync(ChatRequestDto request, CancellationToken cancellationToken);
}

public interface IChatIntentClient
{
    Task<ChatIntent> InterpretAsync(ChatRequestDto request, IReadOnlyList<ChatCatalogEntry> catalog,
        CancellationToken cancellationToken);
}

public record ChatCatalogEntry(int Id, string Name, string Category, string? Brand);

public class ChatIntent
{
    public string Intent { get; set; } = "search";
    public string? Keywords { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Gender { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Sort { get; set; }
    public bool UseContext { get; set; }
}
