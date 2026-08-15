// SPDX-License-Identifier: GPL-3.0-or-later
using Core.Assets;
using Core.Colors;
using Core.Diagnostics;
using Core.Model;

namespace Core.Mapping;

/// <summary>
/// Maps the normalized skin model to a Weasel theme (§12). All rules are pure
/// and deterministic; image-derived colors are injected via <see cref="AnalyzedColors"/>.
/// </summary>
public static class WeaselMapper
{
    public const string FallbackFont = "Microsoft YaHei";

    // First-release layout fallbacks (§12.6).
    private const int FallbackMinWidth = 160;
    private const int FallbackMarginX = 12;
    private const int FallbackMarginY = 12;
    private const int FallbackSpacing = 10;
    private const int FallbackCandidateSpacing = 5;

    public static ConversionResult Map(
        NormalizedSkin skin,
        ConversionOptions options,
        SkinSchemeKind sourceSchemeKind,
        AnalyzedColors? analyzed,
        string colorSchemeId,
        IFontChecker fontChecker)
    {
        var diagnostics = new List<Diagnostic>();
        var mappings = new List<MappingRecord>();
        var scheme = skin.Schemes[sourceSchemeKind];

        // ---- Fonts (§12.3) --------------------------------------------------
        var fontFaces = new List<string>();
        var installedChineseFont = AddFontIfUsable(skin.Typography.ChineseFont, "Display/font_ch", fontChecker, fontFaces, diagnostics);
        AddFontIfUsable(skin.Typography.LatinFont, "Display/font_en", fontChecker, fontFaces, diagnostics);
        if (!fontFaces.Contains(FallbackFont, StringComparer.OrdinalIgnoreCase))
        {
            fontFaces.Add(FallbackFont);
        }

        var fontFace = string.Join(", ", fontFaces);

        // Labels and comments carry Chinese glyphs, so they must use the usable
        // Chinese font (or the CJK fallback) rather than whatever font happens to
        // be first — a Latin-only font such as Arial would drop CJK (§12.3, M-07).
        var cjkFont = installedChineseFont ?? FallbackFont;

        var fontPoint = skin.Typography.FontSize is > 0 and <= 96
            ? skin.Typography.FontSize.Value
            : Fallback(diagnostics, "Display/font_size", skin.Typography.FontSize?.ToString(), 14);
        var commentFontPoint = Math.Max(fontPoint - 1, 8);

        mappings.Add(new MappingRecord("Display/font_size", "style/font_point", fontPoint.ToString()));
        mappings.Add(new MappingRecord("Display/font_ch,font_en", "style/font_face", fontFace));

        // ---- Text colors (§12.4 table) --------------------------------------
        var textColor = skin.Colors.Pinyin ?? "0x000000";
        var candidateTextColor = skin.Colors.OtherCandidate ?? "0x000000";
        var hilitedCandidateTextColor = skin.Colors.FirstCandidate ?? candidateTextColor;
        var commentTextColor = skin.Colors.CompositionHint ?? candidateTextColor;
        var hilitedCommentTextColor = skin.Colors.CompositionHint ?? hilitedCandidateTextColor;

        // ---- Image-derived colors (§12.4) ------------------------------------
        var backColor = analyzed?.BackColor ?? "0xffffff";
        var borderColor = analyzed?.BorderColor ?? "0xcccccc";
        var hilitedCandidateBackColor = analyzed?.AccentColor
            ?? DeriveContrastingBack(backColor, hilitedCandidateTextColor);
        var shadowColor = skin.Colors.Glow ? WithAlpha(borderColor, 0x40) : "0x00000000";

        // Guarantee readability of the highlighted candidate (§20.2 minimum bar).
        if (!HasMinimumContrast(hilitedCandidateBackColor, hilitedCandidateTextColor))
        {
            hilitedCandidateBackColor = DeriveContrastingBack(backColor, hilitedCandidateTextColor);
        }

        var colors = new List<KeyValuePair<string, string>>
        {
            new("text_color", textColor),
            new("back_color", backColor),
            new("border_color", borderColor),
            new("hilited_text_color", textColor),
            new("hilited_back_color", backColor),
            new("candidate_text_color", candidateTextColor),
            new("candidate_back_color", backColor),
            new("comment_text_color", commentTextColor),
            new("hilited_candidate_text_color", hilitedCandidateTextColor),
            new("hilited_candidate_back_color", hilitedCandidateBackColor),
            new("hilited_comment_text_color", hilitedCommentTextColor),
            new("shadow_color", shadowColor),
        };

        mappings.Add(new MappingRecord("Display/pinyin_color", "text_color", textColor));
        mappings.Add(new MappingRecord("Display/zhongwen_color", "candidate_text_color", candidateTextColor));
        mappings.Add(new MappingRecord("Display/zhongwen_first_color", "hilited_candidate_text_color", hilitedCandidateTextColor));
        mappings.Add(new MappingRecord("Display/comphint_color", "comment_text_color", commentTextColor));
        mappings.Add(new MappingRecord($"Scheme_{sourceSchemeKind}/{(scheme.BackgroundAsset is null ? "(analysis)" : "pic")}", "back_color", backColor));

        // ---- Layout (§12.6) ---------------------------------------------------
        var marginX = EstimateMargin(scheme.HorizontalLayout, diagnostics, "layout_horizontal", FallbackMarginX);
        var marginY = EstimateMargin(scheme.VerticalLayout, diagnostics, "layout_vertical", FallbackMarginY);
        var spacing = EstimateNonNegative(scheme.PinyinMargin, index: 1, max: 32, diagnostics, "pinyin_marge", FallbackSpacing);
        var candidateSpacing = EstimateNonNegative(scheme.CandidateMargin, index: 0, max: 24, diagnostics, "zhongwen_marge", FallbackCandidateSpacing);

        var minWidth = FallbackMinWidth;
        if (scheme.PrimaryVisualAsset is not null &&
            skin.Assets.TryGetValue(scheme.PrimaryVisualAsset, out var bgAsset) &&
            bgAsset.Width is > 0)
        {
            // Background bitmaps are drawn at fixed size in Sogou; Weasel windows
            // auto-size, so half the bitmap width is used as a conservative floor.
            minWidth = Math.Clamp(bgAsset.Width.Value / 2, 120, 480);
            mappings.Add(new MappingRecord(
                $"Scheme_{sourceSchemeKind}/{scheme.PrimaryVisualAsset}",
                "style/layout/min_width",
                minWidth.ToString()));
        }

        var layout = new List<KeyValuePair<string, int>>
        {
            new("min_width", minWidth),
            new("min_height", 0),
            new("margin_x", marginX),
            new("margin_y", marginY),
            new("spacing", spacing),
            new("candidate_spacing", candidateSpacing),
            new("hilite_spacing", 4),
            new("hilite_padding", 2),
            new("border_width", 1),
            new("corner_radius", 4),
            new("shadow_radius", skin.Colors.Glow ? 6 : 0),
            new("shadow_offset_x", 4),
            new("shadow_offset_y", 4),
        };

        var theme = new WeaselTheme
        {
            ColorSchemeId = colorSchemeId,
            Name = skin.Metadata.Name,
            Author = skin.Metadata.Author,
            Horizontal = options.Layout == LayoutKind.Horizontal,
            FontFace = fontFace,
            LabelFontFace = cjkFont,
            CommentFontFace = cjkFont,
            FontPoint = fontPoint,
            LabelFontPoint = fontPoint,
            CommentFontPoint = commentFontPoint,
            Colors = colors,
            Layout = layout,
        };

        var unsupported = CollectUnsupportedFeatures(skin, diagnostics);

        return new ConversionResult(theme, sourceSchemeKind, diagnostics, mappings, unsupported);
    }

    /// <summary>Adds the font to the fallback chain when usable and returns it, else null.</summary>
    private static string? AddFontIfUsable(
        string? font,
        string sourceKey,
        IFontChecker fontChecker,
        List<string> target,
        List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(font))
        {
            return null;
        }

        if (!fontChecker.IsInstalled(font))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.FontNotInstalled,
                DiagnosticSeverity.Warning,
                $"Font '{font}' is not installed; '{FallbackFont}' will be used instead.",
                SourceKey: sourceKey,
                Fallback: FallbackFont));
            return null;
        }

        if (!target.Contains(font, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(font);
        }

        return font;
    }

    /// <summary>
    /// Largest source inset still treated as text padding. Sogou insets are measured
    /// from the edges of a fixed-size background bitmap, so decorative artwork inflates
    /// them far beyond anything meaningful as Weasel window padding.
    /// </summary>
    private const int MaxReasonableMargin = 32;

    private static int EstimateMargin(
        IReadOnlyList<int?> layoutValues,
        List<Diagnostic> diagnostics,
        string sourceKey,
        int fallback)
    {
        // An absent key is a normal case and falls back silently; a present but
        // truncated list is corrupt and is reported (M-08).
        if (layoutValues.Count == 0)
        {
            return fallback;
        }

        // layout_horizontal / layout_vertical: index 1 and 2 carry the two edge
        // insets (format documented by the ssf2fcitx reference implementation).
        if (layoutValues.Count < 3 || layoutValues[1] is not { } a || layoutValues[2] is not { } b)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutValueInvalid,
                DiagnosticSeverity.Warning,
                $"'{sourceKey}' is malformed or too short to read the edge insets; fallback {fallback} is used.",
                SourceKey: sourceKey,
                Fallback: fallback.ToString()));
            return fallback;
        }

        var average = (a + b) / 2;

        if (a < 0 || b < 0 || average > MaxReasonableMargin)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutValueInvalid,
                DiagnosticSeverity.Warning,
                $"Insets '{a},{b}' from '{sourceKey}' do not translate to candidate window padding " +
                $"(they include background artwork); fallback {fallback} is used.",
                SourceKey: sourceKey,
                Fallback: fallback.ToString()));
            return fallback;
        }

        return Math.Max(average, 2);
    }

    private static int EstimateNonNegative(
        IReadOnlyList<int?> values,
        int index,
        int max,
        List<Diagnostic> diagnostics,
        string sourceKey,
        int fallback)
    {
        // Absent key: silent fallback. Present but shorter than needed: report it.
        if (values.Count == 0)
        {
            return fallback;
        }

        if (values.Count <= index || values[index] is not { } v)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutValueInvalid,
                DiagnosticSeverity.Warning,
                $"'{sourceKey}' is malformed or too short at position {index}; fallback {fallback} is used.",
                SourceKey: sourceKey,
                Fallback: fallback.ToString()));
            return fallback;
        }

        if (v < 0 || v > max)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutValueInvalid,
                DiagnosticSeverity.Warning,
                $"Value '{v}' from '{sourceKey}' is out of range; fallback {fallback} is used.",
                SourceKey: sourceKey,
                Fallback: fallback.ToString()));
            return fallback;
        }

        return v;
    }

    private static int Fallback(List<Diagnostic> diagnostics, string sourceKey, string? raw, int fallback)
    {
        diagnostics.Add(new Diagnostic(
            DiagnosticCodes.LayoutValueInvalid,
            DiagnosticSeverity.Warning,
            $"Value '{raw ?? "(missing)"}' for '{sourceKey}' is invalid; fallback {fallback} is used.",
            SourceKey: sourceKey,
            Fallback: fallback.ToString()));
        return fallback;
    }

    /// <summary>Derives a highlight background with sufficient contrast to the highlight text (§12.4).</summary>
    internal static string DeriveContrastingBack(string backColor, string textColor)
    {
        var (tr, tg, tb, _) = ColorNormalizer.ToRgba(textColor);
        var textLuminance = Luminance(tr, tg, tb);
        var (br, bg, bb, _) = ColorNormalizer.ToRgba(backColor);

        // Light text needs a darker plate, dark text a lighter one.
        var factor = textLuminance > 0.5 ? 0.55 : 1.8;
        byte Adjust(byte c) => (byte)Math.Clamp((int)Math.Round(c * factor), 0, 255);
        var candidate = ColorNormalizer.FromRgb(Adjust(br), Adjust(bg), Adjust(bb));

        if (HasMinimumContrast(candidate, textColor))
        {
            return candidate;
        }

        return textLuminance > 0.5 ? "0x333333" : "0xe8e8e8";
    }

    internal static bool HasMinimumContrast(string backColor, string textColor)
    {
        var (br, bg, bb, _) = ColorNormalizer.ToRgba(backColor);
        var (tr, tg, tb, _) = ColorNormalizer.ToRgba(textColor);
        var lb = Luminance(br, bg, bb);
        var lt = Luminance(tr, tg, tb);
        var ratio = (Math.Max(lb, lt) + 0.05) / (Math.Min(lb, lt) + 0.05);
        return ratio >= 1.8;
    }

    private static double Luminance(byte r, byte g, byte b)
        => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;

    private static string WithAlpha(string sixDigitColor, byte alpha)
    {
        var (r, g, b, _) = ColorNormalizer.ToRgba(sixDigitColor);
        return ColorNormalizer.FromRgba(r, g, b, alpha);
    }

    private static List<string> CollectUnsupportedFeatures(NormalizedSkin skin, List<Diagnostic> diagnostics)
    {
        var unsupported = new List<string>();

        void Add(string feature)
        {
            unsupported.Add(feature);
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnsupportedFeature,
                DiagnosticSeverity.Info,
                $"Source feature cannot be expressed by Weasel configuration: {feature}"));
        }

        foreach (var (kind, scheme) in skin.Schemes.OrderBy(s => s.Key))
        {
            if (scheme.BackgroundMaskAsset is not null)
            {
                Add($"Scheme_{kind}: background mask '{scheme.BackgroundMaskAsset}' (runtime masking)");
            }

            foreach (var overlay in scheme.Overlays)
            {
                Add($"Scheme_{kind}: overlay image '{overlay}'");
            }

            if (scheme.BackgroundAsset is null && (scheme.PinyinBackgroundAsset ?? scheme.CandidateBackgroundAsset) is not null)
            {
                Add($"Scheme_{kind}: split pinyin/candidate backgrounds (runtime composition)");
            }

            if (scheme.BackgroundAsset is not null &&
                skin.Assets.TryGetValue(scheme.BackgroundAsset, out var bg))
            {
                Add($"Scheme_{kind}: bitmap background '{scheme.BackgroundAsset}' (rendered by approximated colors only)");
                if (bg.FrameCount is > 1)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.AnimatedAssetDegraded,
                        DiagnosticSeverity.Warning,
                        $"Animated background '{scheme.BackgroundAsset}' ({bg.FrameCount} frames); only the first frame is analyzed.",
                        Asset: scheme.BackgroundAsset));
                    Add($"Scheme_{kind}: animated background '{scheme.BackgroundAsset}'");
                }
            }
        }

        if (skin.StatusBar is not null)
        {
            Add("StatusBar: Sogou status bar background and buttons");
        }

        return unsupported;
    }
}
