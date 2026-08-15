// SPDX-License-Identifier: GPL-3.0-or-later
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Core.Assets;
using Core.Colors;
using Core.Limits;

namespace Infrastructure.Imaging;

/// <summary>
/// Deterministic color extraction from the scheme background image (§12.4):
/// - back color: most frequent quantized color among visible pixels;
/// - border color: per-channel median of the outermost pixel ring;
/// - accent color: most frequent high-saturation color sufficiently far from the back color.
/// The algorithm uses only integer bucketing and stable ordering, so identical
/// input always yields identical output (§12.4 determinism requirement).
/// </summary>
public sealed class GdiImageColorAnalyzer : IImageColorAnalyzer
{
    private const byte MinVisibleAlpha = 32;

    private readonly ResourceLimits _limits;

    public GdiImageColorAnalyzer(ResourceLimits? limits = null)
    {
        _limits = limits ?? ResourceLimits.Default;
    }

    public AnalyzedColors? Analyze(byte[] imageContent, byte[]? maskContent, string? transparentColor)
    {
        using var bitmap = TryLoad32bpp(imageContent);
        if (bitmap is null)
        {
            return null;
        }

        (byte R, byte G, byte B)? chromaKey = null;
        if (transparentColor is not null)
        {
            var (kr, kg, kb, _) = ColorNormalizer.ToRgba(transparentColor);
            chromaKey = (kr, kg, kb);
        }

        using var mask = maskContent is null ? null : TryLoad32bpp(maskContent);
        var pixels = ReadPixels(bitmap);
        var maskPixels = mask is not null && mask.Width == bitmap.Width && mask.Height == bitmap.Height
            ? ReadPixels(mask)
            : null;

        int width = bitmap.Width, height = bitmap.Height;
        var visible = new List<(byte R, byte G, byte B)>(pixels.Length / 4);
        var ring = new List<(byte R, byte G, byte B)>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var a = pixels[i + 3];
                if (a < MinVisibleAlpha)
                {
                    continue;
                }

                if (maskPixels is not null)
                {
                    // Mask semantics: bright mask pixel = visible area (§12.5).
                    var luminance = (maskPixels[i + 2] * 299 + maskPixels[i + 1] * 587 + maskPixels[i] * 114) / 1000;
                    if (luminance < 128)
                    {
                        continue;
                    }
                }

                var rgb = (R: pixels[i + 2], G: pixels[i + 1], B: pixels[i]);
                if (chromaKey is not null && ChannelDistance(rgb, chromaKey.Value) <= 12)
                {
                    continue;
                }

                visible.Add(rgb);
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    ring.Add(rgb);
                }
            }
        }

        if (visible.Count == 0)
        {
            return null;
        }

        var back = DominantColor(visible, excludeNear: null);
        var border = ring.Count > 0 ? MedianColor(ring) : back;
        var accent = AccentColor(visible, back);

        return new AnalyzedColors(
            BackColor: ColorNormalizer.FromRgb(back.R, back.G, back.B),
            BorderColor: ColorNormalizer.FromRgb(border.R, border.G, border.B),
            AccentColor: accent is null ? null : ColorNormalizer.FromRgb(accent.Value.R, accent.Value.G, accent.Value.B));
    }

    private Bitmap? TryLoad32bpp(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var raw = new Bitmap(stream);

            // Reject pathological dimensions before cloning, which allocates width*height*4
            // bytes of managed memory (code review H-01).
            if (raw.Width > _limits.MaxImageDimension ||
                raw.Height > _limits.MaxImageDimension ||
                (long)raw.Width * raw.Height > _limits.MaxImagePixels)
            {
                return null;
            }

            // Clone to 32bppArgb so indexed formats (GIF first frame, 8-bit BMP) read uniformly.
            return raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException or IOException)
        {
            return null;
        }
    }

    private static byte[] ReadPixels(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            if (data.Stride == bitmap.Width * 4)
            {
                return buffer;
            }

            // Compact rows when stride includes padding.
            var compact = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Array.Copy(buffer, y * data.Stride, compact, y * bitmap.Width * 4, bitmap.Width * 4);
            }

            return compact;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static (byte R, byte G, byte B) DominantColor(
        List<(byte R, byte G, byte B)> pixels,
        (byte R, byte G, byte B)? excludeNear)
    {
        // Quantize to 4 bits per channel, then average the winning bucket.
        var buckets = new Dictionary<int, (long R, long G, long B, int Count)>();
        foreach (var p in pixels)
        {
            if (excludeNear is not null && ChannelDistance(p, excludeNear.Value) < 96)
            {
                continue;
            }

            var key = ((p.R >> 4) << 8) | ((p.G >> 4) << 4) | (p.B >> 4);
            buckets.TryGetValue(key, out var acc);
            buckets[key] = (acc.R + p.R, acc.G + p.G, acc.B + p.B, acc.Count + 1);
        }

        if (buckets.Count == 0)
        {
            var f = pixels[0];
            return (f.R, f.G, f.B);
        }

        var best = buckets.OrderByDescending(b => b.Value.Count).ThenBy(b => b.Key).First().Value;
        return ((byte)(best.R / best.Count), (byte)(best.G / best.Count), (byte)(best.B / best.Count));
    }

    private static (byte R, byte G, byte B) MedianColor(List<(byte R, byte G, byte B)> pixels)
    {
        var rs = pixels.Select(p => p.R).Order().ToArray();
        var gs = pixels.Select(p => p.G).Order().ToArray();
        var bs = pixels.Select(p => p.B).Order().ToArray();
        var mid = pixels.Count / 2;
        return (rs[mid], gs[mid], bs[mid]);
    }

    private static (byte R, byte G, byte B)? AccentColor(
        List<(byte R, byte G, byte B)> pixels,
        (byte R, byte G, byte B) back)
    {
        var saturated = pixels.Where(p =>
        {
            int max = Math.Max(p.R, Math.Max(p.G, p.B));
            int min = Math.Min(p.R, Math.Min(p.G, p.B));
            if (max == 0)
            {
                return false;
            }

            var saturation = (max - min) / (double)max;
            var value = max / 255.0;
            return saturation >= 0.45 && value >= 0.35 && ChannelDistance(p, back) >= 96;
        }).ToList();

        // Require the accent to cover a meaningful area to avoid noise pixels.
        var minimumCount = Math.Max(16, pixels.Count / 200);
        if (saturated.Count < minimumCount)
        {
            return null;
        }

        return DominantColor(saturated, excludeNear: null);
    }

    private static int ChannelDistance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
        => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
}
