using Microsoft.Extensions.Logging;

namespace ClothingAPI.UnitTests;

/// Logger giả: giữ lại mọi dòng log (đã format) để test kiểm tra mức log và nội dung (ví dụ: không lộ token).
public sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : $" | {exception}")));
    }
}
