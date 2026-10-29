using ClothingAPI.Services.Implementations;
using SkiaSharp;

namespace ClothingAPI.UnitTests;

public class ImageOptimizationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "clothing-image-test-" + Guid.NewGuid().ToString("N"));

    private string WritePng(int width, int height)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid() + ".png");
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(230, 20, 30, 180));
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, bytes.ToArray()); return path;
    }

    [Fact]
    public void ResizesWithoutChangingAspectRatioOrOriginalFile()
    {
        var path = WritePng(1200, 800); var original = File.ReadAllBytes(path);
        var bytes = ImageVariantEncoder.Encode(path, 640);
        using var image = SKBitmap.Decode(bytes);
        Assert.Equal(640, image.Width); Assert.Equal(427, image.Height);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void DoesNotUpscaleSmallImagesAndPreservesTransparency()
    {
        var bytes = ImageVariantEncoder.Encode(WritePng(100, 50), 640);
        using var image = SKBitmap.Decode(bytes);
        Assert.Equal(100, image.Width); Assert.Equal(50, image.Height);
        Assert.InRange((int)image.GetPixel(20, 20).Alpha, 170, 190);
    }

    [Fact]
    public void RejectsUnboundedVariantSizesAndInvalidImages()
    {
        var path = WritePng(20, 20);
        Assert.Null(ImageVariantEncoder.Encode(path, 777));
        File.WriteAllText(path, "not an image");
        Assert.Null(ImageVariantEncoder.Encode(path, 640));
    }

    [Fact]
    public void HonorsExifRotationInsteadOfTurningPortraitImagesSideways()
    {
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "rotated.jpg");
        using var bitmap = new SKBitmap(200, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 50, 200, 50, paint);
        }
        using var source = SKImage.FromBitmap(bitmap);
        using var encoded = source.Encode(SKEncodedImageFormat.Jpeg, 100);
        byte[] exif = [0xff,0xe1,0,34, 69,120,105,102,0,0, 73,73,42,0,8,0,0,0, 1,0, 0x12,1,3,0,1,0,0,0,6,0,0,0, 0,0,0,0];
        var jpg = encoded.ToArray(); File.WriteAllBytes(path, jpg.Take(2).Concat(exif).Concat(jpg.Skip(2)).ToArray());
        using var output = SKBitmap.Decode(ImageVariantEncoder.Encode(path, 160));
        Assert.Equal(100, output.Width); Assert.Equal(200, output.Height);
        Assert.True(output.GetPixel(20, 100).Blue > 200);
        Assert.True(output.GetPixel(80, 100).Red > 200);
    }

    public void Dispose()
    {
        var full = Path.GetFullPath(directory);
        if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("clothing-image-test-") && Directory.Exists(full)) Directory.Delete(full, true);
    }
}
