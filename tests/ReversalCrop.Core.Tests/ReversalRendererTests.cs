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

    [Fact]
    public void Horizontal_offset_moves_the_crop_in_the_expected_direction()
    {
        using var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, x < 32 ? SKColors.Red : SKColors.Blue);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var left = SKBitmap.Decode(ReversalRenderer.RenderIconPng(data.ToArray(), 128, zoom: 2, offsetX: -100))!;
        using var right = SKBitmap.Decode(ReversalRenderer.RenderIconPng(data.ToArray(), 128, zoom: 2, offsetX: 100))!;

        SKColor leftCenter = left.GetPixel(64, 64);
        SKColor rightCenter = right.GetPixel(64, 64);
        Assert.True(leftCenter.Red > leftCenter.Blue);
        Assert.True(rightCenter.Blue > rightCenter.Red);
    }

    [Fact]
    public void Preview_and_export_share_the_same_profile_bounds()
    {
        byte[] source = CreatePng(64, 64, SKColors.Orange);
        using var preview = SKBitmap.Decode(ReversalRenderer.RenderPreview(source))!;
        using var export = SKBitmap.Decode(ReversalRenderer.RenderIconPng(source, 512, supersample: 4))!;

        Assert.InRange(Math.Abs(preview.GetPixel(36, 256).Alpha - export.GetPixel(36, 256).Alpha), 0, 8);
        Assert.Equal(preview.GetPixel(40, 256).Alpha, export.GetPixel(40, 256).Alpha);
    }

    [Fact]
    public void Renderer_preserves_partial_alpha_from_source_image()
    {
        using var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var translucent = new SKPaint { Color = new SKColor(240, 40, 20, 128) };
            canvas.DrawRect(new SKRect(16, 16, 48, 48), translucent);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = SKBitmap.Decode(ReversalRenderer.RenderIconPng(data.ToArray(), 128))!;
        SKColor center = output.GetPixel(64, 64);

        Assert.InRange(center.Alpha, 124, 132);
        Assert.True(center.Red > center.Blue);
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
