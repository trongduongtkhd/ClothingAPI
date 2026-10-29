using SkiaSharp;

namespace ClothingAPI.Services.Implementations;

public static class ImageVariantEncoder
{
    public static readonly int[] Widths = [160, 320, 640, 960, 1280, 1920];
    public const string Version = "webp-q80-v1";

    public static byte[]? Encode(string sourcePath, int requestedWidth)
    {
        if (!Widths.Contains(requestedWidth)) return null;
        using var stream = File.OpenRead(sourcePath);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > 25_000_000 || codec.FrameCount > 1) return null;
        using var source = SKBitmap.Decode(codec);
        if (source is null) return null;
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var naturalWidth = swap ? source.Height : source.Width;
        var naturalHeight = swap ? source.Width : source.Height;
        var width = Math.Min(requestedWidth, naturalWidth);
        var height = Math.Max(1, (int)Math.Round((double)naturalHeight * width / naturalWidth));
        using var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)width / naturalWidth, (float)height / naturalHeight);
            switch (codec.EncodedOrigin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(source.Width, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(source.Width, source.Height); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, source.Height); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(source.Height, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(source.Height, source.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, source.Width); canvas.RotateDegrees(-90); break;
            }
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        using var encoded = SKImage.FromBitmap(output);
        using var data = encoded.Encode(SKEncodedImageFormat.Webp, 80);
        return data?.ToArray();
    }
}
