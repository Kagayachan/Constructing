// SPDX-License-Identifier: GPL-3.0-or-later
using System.Drawing;
using System.Drawing.Imaging;
using Ssf2Weasel.Core.Colors;
using Ssf2Weasel.Infrastructure.Imaging;
using Ssf2Weasel.TestSupport;
using Xunit;

namespace Ssf2Weasel.UnitTests;

public class ImageAnalyzerTests
{
    private static readonly GdiImageColorAnalyzer Analyzer = new();

    private static byte[] Png(Action<Graphics, int, int> draw, int width = 64, int height = 32, bool withAlpha = true)
    {
        using var bitmap = new Bitmap(width, height, withAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            draw(g, width, height);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    [Fact]
    public void Dominant_fill_becomes_back_color()
    {
        var png = SyntheticSsf.SolidPng(64, 32, Color.FromArgb(255, 40, 40, 48));
        var result = Analyzer.Analyze(png, null, null);

        Assert.NotNull(result);
        Assert.Equal(ColorNormalizer.FromRgb(40, 40, 48), result!.BackColor);
    }

    [Fact]
    public void Edge_ring_becomes_border_color()
    {
        var png = SyntheticSsf.SolidPng(64, 32, Color.FromArgb(255, 40, 40, 48), Color.FromArgb(255, 200, 10, 10));
        var result = Analyzer.Analyze(png, null, null);

        Assert.Equal(ColorNormalizer.FromRgb(200, 10, 10), result!.BorderColor);
        Assert.Equal(ColorNormalizer.FromRgb(40, 40, 48), result.BackColor);
    }

    [Fact]
    public void Saturated_region_becomes_accent_color()
    {
        var png = Png((g, w, h) =>
        {
            g.Clear(Color.FromArgb(255, 40, 40, 48));
            g.FillRectangle(new SolidBrush(Color.FromArgb(255, 255, 158, 62)), 0, 0, w / 3, h);
        });

        var result = Analyzer.Analyze(png, null, null);
        Assert.Equal(ColorNormalizer.FromRgb(255, 158, 62), result!.AccentColor);
    }

    [Fact]
    public void Tiny_noise_regions_do_not_become_accent()
    {
        var png = Png((g, w, h) =>
        {
            g.Clear(Color.FromArgb(255, 40, 40, 48));
            g.FillRectangle(new SolidBrush(Color.FromArgb(255, 255, 0, 0)), 0, 0, 2, 2);
        });

        Assert.Null(Analyzer.Analyze(png, null, null)!.AccentColor);
    }

    [Fact]
    public void Fully_transparent_pixels_are_ignored()
    {
        var png = Png((g, w, h) =>
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(new SolidBrush(Color.FromArgb(255, 40, 40, 48)), w / 4, h / 4, w / 2, h / 2);
        });

        var result = Analyzer.Analyze(png, null, null);
        Assert.Equal(ColorNormalizer.FromRgb(40, 40, 48), result!.BackColor);
    }

    [Fact]
    public void Declared_chroma_key_is_excluded()
    {
        // Mirrors 维尼熊.ssf: a 24bpp BMP whose transparent area is pure chroma green.
        var chroma = Color.FromArgb(255, 0, 255, 30);
        var png = Png(
            (g, w, h) =>
            {
                g.Clear(chroma);
                g.FillRectangle(new SolidBrush(Color.FromArgb(255, 250, 210, 80)), w / 4, h / 4, w / 2, h / 2);
            },
            withAlpha: false);

        var withoutKey = Analyzer.Analyze(png, null, null);
        Assert.Equal(ColorNormalizer.FromRgb(0, 255, 30), withoutKey!.BackColor);

        var key = ColorNormalizer.FromRgb(0, 255, 30);
        var withKey = Analyzer.Analyze(png, null, key);
        Assert.Equal(ColorNormalizer.FromRgb(250, 210, 80), withKey!.BackColor);
    }

    [Fact]
    public void Mask_restricts_analysis_to_visible_area()
    {
        var png = Png((g, w, h) =>
        {
            g.Clear(Color.FromArgb(255, 10, 200, 10));
            g.FillRectangle(new SolidBrush(Color.FromArgb(255, 40, 40, 48)), 0, 0, w / 4, h);
        });

        // White = visible, black = hidden. Only the left quarter stays visible.
        var mask = Png((g, w, h) =>
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 0, 0, w / 4, h);
        });

        var result = Analyzer.Analyze(png, mask, null);
        Assert.Equal(ColorNormalizer.FromRgb(40, 40, 48), result!.BackColor);
    }

    [Fact]
    public void Analysis_is_deterministic()
    {
        var png = SyntheticSsf.SolidPng(64, 32, Color.FromArgb(255, 40, 40, 48), Color.FromArgb(255, 90, 90, 100));
        var first = Analyzer.Analyze(png, null, null);
        var second = Analyzer.Analyze(png, null, null);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Undecodable_content_returns_null()
        => Assert.Null(Analyzer.Analyze("not an image"u8.ToArray(), null, null));

    [Fact]
    public void Fully_transparent_image_returns_null()
    {
        var png = Png((g, w, h) => g.Clear(Color.Transparent));
        Assert.Null(Analyzer.Analyze(png, null, null));
    }
}
