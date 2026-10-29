using System.Security.Cryptography;
using System.Text;
using ClothingAPI.Services.Implementations;
using Microsoft.Net.Http.Headers;

namespace ClothingAPI.Middleware;

public sealed class OptimizedImageMiddleware(RequestDelegate next, IWebHostEnvironment environment,
    ILogger<OptimizedImageMiddleware> logger)
{
    private static readonly SemaphoreSlim EncoderSlots = new(2);
    private static readonly HashSet<string> Folders = ["products", "variants", "categories", "brands", "avatars"];
    private static readonly HashSet<string> Extensions = [".jpg", ".jpeg", ".png", ".webp"];

    public async Task InvokeAsync(HttpContext context)
    {
        var parts = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            || parts is not { Length: 3 } || parts[0] != "uploads" || !Folders.Contains(parts[1])
            || Path.GetFileName(parts[2]) != parts[2] || !Extensions.Contains(Path.GetExtension(parts[2]).ToLowerInvariant())
            || !int.TryParse(context.Request.Query["w"], out var width) || !ImageVariantEncoder.Widths.Contains(width))
        {
            await next(context); return;
        }
        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var source = new FileInfo(Path.Combine(root, "uploads", parts[1], parts[2]));
        if (!source.Exists) { await next(context); return; }
        // Include mtime/length so replacing a source never serves an old derived image.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{ImageVariantEncoder.Version}|{parts[1]}/{parts[2]}|{source.LastWriteTimeUtc.Ticks}|{source.Length}|{width}"))).ToLowerInvariant();
        var cacheDirectory = Path.Combine(root, "uploads", "optimized");
        var cached = Path.Combine(cacheDirectory, key + ".webp");
        if (!File.Exists(cached))
        {
            await EncoderSlots.WaitAsync(context.RequestAborted);
            try
            {
                if (!File.Exists(cached))
                {
                    var bytes = ImageVariantEncoder.Encode(source.FullName, width);
                    if (bytes is null) { await next(context); return; }
                    Directory.CreateDirectory(cacheDirectory);
                    var temp = Path.Combine(cacheDirectory, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    await File.WriteAllBytesAsync(temp, bytes, context.RequestAborted);
                    File.Move(temp, cached, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TypeInitializationException or DllNotFoundException)
            {
                logger.LogWarning("Image optimization unavailable ({ErrorType}); serving original", ex.GetType().Name);
                await next(context); return;
            }
            finally { EncoderSlots.Release(); }
        }
        context.Response.Headers.CacheControl = "public, max-age=604800";
        context.Response.Headers.Vary = "Accept-Encoding";
        await Results.File(cached, "image/webp", lastModified: source.LastWriteTimeUtc,
            entityTag: new EntityTagHeaderValue('"' + key + '"'), enableRangeProcessing: true).ExecuteAsync(context);
    }
}
