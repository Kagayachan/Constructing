// SPDX-License-Identifier: GPL-3.0-or-later
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Ssf2Weasel.Core.Colors;
using Ssf2Weasel.Core.Mapping;

namespace Ssf2Weasel.Infrastructure.Imaging;

/// <summary>
/// Renders the deterministic preview (§13.3): the approximated Weasel style with
/// fixed test content, not the original Sogou bitmap skin.
/// </summary>
public static class PreviewRenderer
{
    private const string Preedit = "xiaolanghao";
    private static readonly (string Label, string Text)[] Candidates =
    [
        ("1", "小狼毫"),
        ("2", "小狼嚎"),
        ("3", "小浪号"),
    ];

    public static byte[] RenderPng(WeaselTheme theme)
    {
        var marginX = theme.GetLayout("margin_x");
        var marginY = theme.GetLayout("margin_y");
        var spacing = theme.GetLayout("spacing");
        var candidateSpacing = theme.GetLayout("candidate_spacing");
        var hilitePadding = theme.GetLayout("hilite_padding");
        var cornerRadius = theme.GetLayout("corner_radius");
        var borderWidth = Math.Max(theme.GetLayout("border_width"), 1);
        var minWidth = theme.GetLayout("min_width");

        using var fontMain = CreateFont(theme.FontFace, theme.FontPoint);
        using var fontLabel = CreateFont(theme.LabelFontFace, theme.LabelFontPoint);

        // Measure with a scratch surface.
        using var scratch = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(scratch);
        measure.PageUnit = GraphicsUnit.Pixel;

        var preeditSize = measure.MeasureString(Preedit, fontMain);
        var candidateSizes = Candidates
            .Select(c => measure.MeasureString($"{c.Label}. {c.Text}", fontMain))
            .ToArray();

        float contentWidth, contentHeight;
        var rowHeight = Math.Max(preeditSize.Height, candidateSizes.Max(s => s.Height)) + hilitePadding * 2;
        if (theme.Horizontal)
        {
            var candidatesWidth = candidateSizes.Sum(s => s.Width + hilitePadding * 2) +
                                  candidateSpacing * (Candidates.Length - 1);
            contentWidth = Math.Max(preeditSize.Width, candidatesWidth);
            contentHeight = rowHeight * 2 + spacing;
        }
        else
        {
            contentWidth = Math.Max(preeditSize.Width, candidateSizes.Max(s => s.Width) + hilitePadding * 2);
            contentHeight = rowHeight * (Candidates.Length + 1) + spacing + candidateSpacing * (Candidates.Length - 1);
        }

        var panelWidth = Math.Max((int)Math.Ceiling(contentWidth) + marginX * 2, minWidth);
        var panelHeight = (int)Math.Ceiling(contentHeight) + marginY * 2;
        const int canvasPadding = 8;

        using var bitmap = new Bitmap(panelWidth + canvasPadding * 2, panelHeight + canvasPadding * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var panel = new Rectangle(canvasPadding, canvasPadding, panelWidth, panelHeight);
        using (var panelPath = RoundedRect(panel, cornerRadius))
        {
            using var back = new SolidBrush(ToColor(theme.GetColor("back_color")));
            g.FillPath(back, panelPath);
            using var border = new Pen(ToColor(theme.GetColor("border_color")), borderWidth);
            g.DrawPath(border, panelPath);
        }

        var x = (float)panel.X + marginX;
        var y = (float)panel.Y + marginY;

        using (var textBrush = new SolidBrush(ToColor(theme.GetColor("text_color"))))
        {
            g.DrawString(Preedit, fontMain, textBrush, x, y + hilitePadding);
        }

        y += rowHeight + spacing;

        using var candidateBrush = new SolidBrush(ToColor(theme.GetColor("candidate_text_color")));
        using var hilitedBrush = new SolidBrush(ToColor(theme.GetColor("hilited_candidate_text_color")));
        using var hilitedBack = new SolidBrush(ToColor(theme.GetColor("hilited_candidate_back_color")));

        var cx = x;
        for (var i = 0; i < Candidates.Length; i++)
        {
            var text = $"{Candidates[i].Label}. {Candidates[i].Text}";
            var size = candidateSizes[i];
            var cellWidth = size.Width + hilitePadding * 2;

            if (i == 0)
            {
                var highlight = new RectangleF(cx, y, cellWidth, rowHeight);
                using var highlightPath = RoundedRect(Rectangle.Round(highlight), Math.Max(cornerRadius - 1, 0));
                g.FillPath(hilitedBack, highlightPath);
                g.DrawString(text, fontMain, hilitedBrush, cx + hilitePadding, y + hilitePadding);
            }
            else
            {
                g.DrawString(text, fontMain, candidateBrush, cx + hilitePadding, y + hilitePadding);
            }

            if (theme.Horizontal)
            {
                cx += cellWidth + candidateSpacing;
            }
            else
            {
                y += rowHeight + candidateSpacing;
            }
        }

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static Font CreateFont(string faceList, int pointSize)
    {
        foreach (var candidate in faceList.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0))
        {
            try
            {
                var font = new Font(candidate, pointSize, FontStyle.Regular, GraphicsUnit.Point);
                if (font.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }

                font.Dispose();
            }
            catch (ArgumentException)
            {
                // Try the next candidate face.
            }
        }

        return new Font(WeaselMapper.FallbackFont, pointSize, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static Color ToColor(string normalized)
    {
        var (r, g, b, a) = ColorNormalizer.ToRgba(normalized);
        return Color.FromArgb(a, r, g, b);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
