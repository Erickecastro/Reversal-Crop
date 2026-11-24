using SkiaSharp;
using ReversalCrop.Core.Imaging;

namespace ReversalCrop.Core.Tests;

public class ReversalRendererTests
{
    [Theory]
    [InlineData(512, 4)]
    [InlineData(2508, 2)]
    public void Export_has_requested_dimensions_and_transparent_corners(int size, int sample)
    {
        byte[] source = CreatePng(32, 32, SKColors.Crimson);
        byte[] output = ReversalRenderer.RenderIconPng(source, size, supersample: sample);
        using var bitmap = SKBitmap.Decode(output)!;

        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(255, bitmap.GetPixel(size / 2, size / 2).Alpha);
    }

    [Fact]
    public void Preview_preserves_transparency_outside_profile()
    {
        byte[] output = ReversalRenderer.RenderPreview(CreatePng(9, 17, SKColors.DodgerBlue));
        using var bitmap = SKBitmap.Decode(output)!;
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(255, bitmap.GetPixel(256, 256).Alpha);
    }

    [Theory]
    [InlineData(8, 64)]
    [InlineData(1600, 300)]
    [InlineData(400, 400)]
    public void Renderer_accepts_varied_source_aspect_and_size(int width, int height)
    {
        byte[] source = CreatePng(width, height, SKColors.SeaGreen);
        byte[] output = ReversalRenderer.RenderIconPng(source, 128, zoom: 1.25, offsetX: 45, offsetY: -25);
        using var result = SKBitmap.Decode(output)!;
        Assert.Equal(128, result.Width);
        Assert.Equal(128, result.Height);
        Assert.Equal(0, result.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Renderer_rejects_invalid_adjustments()
    {
        byte[] source = CreatePng(16, 16, SKColors.White);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReversalRenderer.RenderIconPng(source, 128, double.NaN));
    }

    private static byte[] CreatePng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
