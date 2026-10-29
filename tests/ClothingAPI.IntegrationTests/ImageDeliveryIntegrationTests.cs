using System.Net;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;

namespace ClothingAPI.IntegrationTests;

public class ImageDeliveryIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "clothing-image-test-" + Guid.NewGuid().ToString("N"));
    private const string ImagePath = "/uploads/products/sample.png";

    public ImageDeliveryIntegrationTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "uploads", "products"));
        using var bitmap = new SKBitmap(1200, 800);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.DarkGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(root, "uploads", "products", "sample.png"), data.ToArray());
    }

    [Fact]
    public async Task ServesCachedWebpAndReturns304ForMatchingETag()
    {
        using var factory = new CustomWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.UseWebRoot(root));
        using var client = app.CreateClient();
        var first = await client.GetAsync(ImagePath + "?w=640");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/webp", first.Content.Headers.ContentType!.MediaType);
        Assert.True(first.Headers.CacheControl!.Public);
        var bytes = await first.Content.ReadAsByteArrayAsync();
        using var output = SKBitmap.Decode(bytes); Assert.Equal(640, output.Width);
        var request = new HttpRequestMessage(HttpMethod.Get, ImagePath + "?w=640");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "uploads", "optimized"), "*.webp"));
    }

    [Fact]
    public async Task InvalidWidthServesOriginalAndMissingFileRemains404()
    {
        using var factory = new CustomWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.UseWebRoot(root));
        using var client = app.CreateClient();
        var original = await client.GetAsync(ImagePath + "?w=999999");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal("image/png", original.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/uploads/products/missing.png?w=640")).StatusCode);
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(root);
        if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("clothing-image-test-") && Directory.Exists(full)) Directory.Delete(full, true);
    }
}
