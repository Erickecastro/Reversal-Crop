using System.Runtime.InteropServices;
using SkiaSharp;

namespace ReversalCrop.Core.Imaging;

public static class ReversalRenderer
{
    private const double ProfileX = 4.5 / 64.0;
    private const double ProfileSize = 55.0 / 64.0;
    private const double ProfileRadius = 15.0 / 64.0;

    public static byte[] RenderPreview(
        byte[] sourceBytes,
        double zoom = 1.0,
        double offsetX = 0,
        double offsetY = 0,
        CancellationToken cancellationToken = default)
    {
        byte[] icon = RenderIconPixels(
            sourceBytes, 512, zoom, offsetX, offsetY, 1,
            cancellationToken);

        return EncodePng(icon, 512, 512);
    }

    public static byte[] RenderIconPng(
        byte[] sourceBytes,
        int target,
        double zoom = 1.0,
        double offsetX = 0,
        double offsetY = 0,
        int supersample = 2)
    {
        byte[] pixels = RenderIconPixels(
            sourceBytes, target, zoom, offsetX, offsetY, supersample);

        return EncodePng(pixels, target, target);
    }

    private static byte[] RenderIconPixels(
        byte[] sourceBytes,
        int target,
        double zoom,
        double offsetX,
        double offsetY,
        int supersample,
        CancellationToken cancellationToken = default)
    {
        if (target <= 0 || supersample <= 0)
            throw new ArgumentOutOfRangeException(nameof(target));

        if (!double.IsFinite(zoom) || zoom < 0.01)
            throw new ArgumentOutOfRangeException(nameof(zoom));

        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
            throw new ArgumentOutOfRangeException(nameof(offsetX));

        using var decoded = SKBitmap.Decode(sourceBytes)
            ?? throw new InvalidOperationException(
                "Não foi possível decodificar a imagem.");

        int width = decoded.Width;
        int height = decoded.Height;

        using var normalized = new SKBitmap(
            new SKImageInfo(
                width, height,
                SKColorType.Rgba8888,
                SKAlphaType.Unpremul));

        using (var conversionCanvas = new SKCanvas(normalized))
        {
            conversionCanvas.Clear(SKColors.Transparent);
            conversionCanvas.DrawBitmap(decoded, 0, 0);
            conversionCanvas.Flush();
        }

        byte[] source = new byte[checked(width * height * 4)];

        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Marshal.Copy(
                IntPtr.Add(normalized.GetPixels(), y * normalized.RowBytes),
                source,
                y * width * 4,
                width * 4);
        }

        double cropSize = Math.Min(width, height) / zoom;

        double centerX = width / 2.0
            + (offsetX / 100.0) * (width - cropSize) / 2.0;

        double centerY = height / 2.0
            + (offsetY / 100.0) * (height - cropSize) / 2.0;

        double left = centerX - cropSize / 2.0;
        double top = centerY - cropSize / 2.0;

        int canvasSize = checked(target * supersample);
        int innerSize = (int)Math.Round(
            target * ProfileSize * supersample);

        int position = (int)Math.Round(
            target * ProfileX * supersample);

        int radius = (int)Math.Round(
            target * ProfileRadius * supersample);

        byte[] canvas = new byte[checked(canvasSize * canvasSize * 4)];

        for (int y = 0; y < innerSize; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (int x = 0; x < innerSize; x++)
            {
                if (!InsideRoundedRectangle(
                    x, y, innerSize, radius))
                    continue;

                double sourceX = left
                    + ((x + 0.5) / innerSize) * cropSize - 0.5;

                double sourceY = top
                    + ((y + 0.5) / innerSize) * cropSize - 0.5;

                SampleBicubic(
                    source, width, height,
                    sourceX, sourceY,
                    out byte r, out byte g,
                    out byte b, out byte a);

                if (a == 0)
                    continue;

                int index = (
                    (y + position) * canvasSize + x + position) * 4;

                canvas[index] = r;
                canvas[index + 1] = g;
                canvas[index + 2] = b;
                canvas[index + 3] = a;
            }
        }

        return ResizePremultiplied(
            canvas, canvasSize, target, cancellationToken);
    }

    private static bool InsideRoundedRectangle(
        int x, int y, int size, int radius)
    {
        double px = x + 0.5;
        double py = y + 0.5;

        double cx = Math.Clamp(px, radius, size - radius);
        double cy = Math.Clamp(py, radius, size - radius);

        double dx = px - cx;
        double dy = py - cy;

        return dx * dx + dy * dy <= radius * radius;
    }

    private static void SampleBicubic(
        byte[] image,
        int width,
        int height,
        double x,
        double y,
        out byte red,
        out byte green,
        out byte blue,
        out byte alpha)
    {
        int baseX = (int)Math.Floor(x);
        int baseY = (int)Math.Floor(y);

        double r = 0, g = 0, b = 0, a = 0;
        double total = 0;

        for (int j = -1; j <= 2; j++)
        {
            int sy = Math.Clamp(baseY + j, 0, height - 1);
            double wy = CubicWeight(y - (baseY + j));

            for (int i = -1; i <= 2; i++)
            {
                int sx = Math.Clamp(baseX + i, 0, width - 1);
                double weight = wy * CubicWeight(x - (baseX + i));

                int index = (sy * width + sx) * 4;

                double sampleAlpha = image[index + 3] / 255.0;
                r += image[index] * sampleAlpha * weight;
                g += image[index + 1] * sampleAlpha * weight;
                b += image[index + 2] * sampleAlpha * weight;
                a += image[index + 3] * weight;
                total += weight;
            }
        }

        if (Math.Abs(total) < 0.000001)
        {
            red = green = blue = alpha = 0;
            return;
        }

        double normalizedAlpha = Math.Clamp(a / total, 0, 255);
        alpha = ToByte(normalizedAlpha);

        if (normalizedAlpha <= 0.000001)
        {
            red = green = blue = 0;
            return;
        }

        double unpremultiply = 255.0 / normalizedAlpha;
        red = ToByte((r / total) * unpremultiply);
        green = ToByte((g / total) * unpremultiply);
        blue = ToByte((b / total) * unpremultiply);
    }

    private static double CubicWeight(double x)
    {
        x = Math.Abs(x);

        if (x < 1)
            return 1.5 * x * x * x - 2.5 * x * x + 1;

        if (x < 2)
            return -0.5 * x * x * x
                   + 2.5 * x * x
                   - 4 * x + 2;

        return 0;
    }

    private static byte[] ResizePremultiplied(
        byte[] source,
        int sourceSize,
        int target,
        CancellationToken cancellationToken)
    {
        byte[] result = new byte[checked(target * target * 4)];

        double scale = sourceSize / (double)target;

        for (int y = 0; y < target; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double sourceY = (y + 0.5) * scale - 0.5;
            int baseY = (int)Math.Floor(sourceY);

            for (int x = 0; x < target; x++)
            {
                double sourceX = (x + 0.5) * scale - 0.5;
                int baseX = (int)Math.Floor(sourceX);

                double r = 0, g = 0, b = 0, a = 0;
                double total = 0;

                for (int j = -2; j <= 3; j++)
                {
                    int sy = Math.Clamp(
                        baseY + j, 0, sourceSize - 1);

                    double wy = LanczosWeight(
                        sourceY - (baseY + j));

                    for (int i = -2; i <= 3; i++)
                    {
                        int sx = Math.Clamp(
                            baseX + i, 0, sourceSize - 1);

                        double weight = wy * LanczosWeight(
                            sourceX - (baseX + i));

                        int index = (sy * sourceSize + sx) * 4;

                        double alpha = source[index + 3] / 255.0;

                        r += source[index] * alpha * weight;
                        g += source[index + 1] * alpha * weight;
                        b += source[index + 2] * alpha * weight;
                        a += source[index + 3] * weight;
                        total += weight;
                    }
                }

                if (Math.Abs(total) < 0.000001)
                    continue;

                a /= total;

                if (a <= 0)
                    continue;

                r /= total;
                g /= total;
                b /= total;

                double divisor = Math.Max(a, 0.000001) / 255.0;

                int outputIndex = (y * target + x) * 4;

                result[outputIndex] = ToByte(r / divisor);
                result[outputIndex + 1] = ToByte(g / divisor);
                result[outputIndex + 2] = ToByte(b / divisor);
                result[outputIndex + 3] = ToByte(Math.Clamp(a, 0, 255));
            }
        }

        return result;
    }

    private static double LanczosWeight(double x)
    {
        x = Math.Abs(x);

        if (x < 0.000001)
            return 1;

        if (x >= 3)
            return 0;

        double p = Math.PI * x;

        return (Math.Sin(p) * Math.Sin(p / 3))
            / (p * p / 3);
    }

    private static byte[] EncodePng(
        byte[] pixels, int width, int height)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(
                width, height,
                SKColorType.Rgba8888,
                SKAlphaType.Unpremul));

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(
                pixels,
                y * width * 4,
                IntPtr.Add(bitmap.GetPixels(), y * bitmap.RowBytes),
                width * 4);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}
