// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Assets;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Mapping;
using Ssf2Weasel.Core.Model;
using Xunit;

namespace Ssf2Weasel.UnitTests;

public class SchemeSelectorTests
{
    [Theory]
    [InlineData(LayoutKind.Horizontal, new[] { SkinSchemeKind.H1, SkinSchemeKind.V1 }, SkinSchemeKind.H1)]
    [InlineData(LayoutKind.Horizontal, new[] { SkinSchemeKind.H2, SkinSchemeKind.V1 }, SkinSchemeKind.H2)]
    [InlineData(LayoutKind.Horizontal, new[] { SkinSchemeKind.V1, SkinSchemeKind.V2 }, SkinSchemeKind.V1)]
    [InlineData(LayoutKind.Horizontal, new[] { SkinSchemeKind.V2 }, SkinSchemeKind.V2)]
    [InlineData(LayoutKind.Vertical, new[] { SkinSchemeKind.H1, SkinSchemeKind.V1 }, SkinSchemeKind.V1)]
    [InlineData(LayoutKind.Vertical, new[] { SkinSchemeKind.V2, SkinSchemeKind.H1 }, SkinSchemeKind.V2)]
    [InlineData(LayoutKind.Vertical, new[] { SkinSchemeKind.H1, SkinSchemeKind.H2 }, SkinSchemeKind.H1)]
    public void Selects_and_falls_back_per_documented_rules(LayoutKind layout, SkinSchemeKind[] available, SkinSchemeKind expected)
    {
        var diagnostics = new List<Diagnostic>();
        Assert.Equal(expected, SchemeSelector.Select(layout, available, diagnostics));
    }

    [Fact]
    public void Cross_orientation_fallback_produces_warning()
    {
        var diagnostics = new List<Diagnostic>();
        SchemeSelector.Select(LayoutKind.Horizontal, [SkinSchemeKind.V1], diagnostics);
        Assert.Contains(diagnostics, d => d.Code == "SCHEME_FALLBACK" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void No_schemes_at_all_throws_conversion_error()
    {
        var ex = Assert.Throws<Ssf2WeaselException>(
            () => SchemeSelector.Select(LayoutKind.Horizontal, [], new List<Diagnostic>()));
        Assert.Equal(ExitCode.ConversionError, ex.ExitCode);
    }
}

public class WeaselMapperTests
{
    private sealed class FakeFontChecker(params string[] installed) : IFontChecker
    {
        public bool IsInstalled(string fontFamily)
            => installed.Contains(fontFamily, StringComparer.OrdinalIgnoreCase);
    }

    private static NormalizedSkin MakeSkin(
        SkinColors? colors = null,
        SkinTypography? typography = null,
        IReadOnlyList<int>? horizontalLayout = null)
    {
        var scheme = new SkinScheme(
            SkinSchemeKind.H1,
            BackgroundAsset: "back.png",
            BackgroundMaskAsset: null,
            PinyinBackgroundAsset: null,
            CandidateBackgroundAsset: null,
            HorizontalLayout: horizontalLayout ?? [0, 10, 12],
            VerticalLayout: [0, 8, 9],
            PinyinMargin: [6, 5, 10, 10],
            CandidateMargin: [4, 6],
            Overlays: []);

        return new NormalizedSkin(
            new SkinMetadata(null, "Test Skin", "1.0", "author", null, null, null),
            typography ?? new SkinTypography("宋体", "Arial", 15),
            colors ?? new SkinColors("0xf6f6f6", "0x3e9eff", "0xe0e0e0", "0x808080"),
            new Dictionary<SkinSchemeKind, SkinScheme> { [SkinSchemeKind.H1] = scheme },
            StatusBar: null,
            Assets: new Dictionary<string, SkinAsset>(StringComparer.OrdinalIgnoreCase)
            {
                ["back.png"] = new SkinAsset("back.png", "back.png", "image/png", 400, 120, 1, new string('0', 64)),
            },
            Diagnostics: [],
            UnknownSections: []);
    }

    private static readonly AnalyzedColors DefaultAnalyzed = new("0x282828", "0x5a5a5a", "0x2010c0");

    private static ConversionResult MapDefault(
        NormalizedSkin skin,
        IFontChecker? fonts = null)
        => Map(skin, DefaultAnalyzed, fonts);

    private static ConversionResult Map(
        NormalizedSkin skin,
        AnalyzedColors? analyzed,
        IFontChecker? fonts = null)
        => WeaselMapper.Map(
            skin,
            new ConversionOptions(LayoutKind.Horizontal),
            SkinSchemeKind.H1,
            analyzed,
            "test_skin",
            fonts ?? new FakeFontChecker("宋体", "Arial", "Microsoft YaHei"));

    [Fact]
    public void Maps_text_colors_per_documented_table()
    {
        var result = MapDefault(MakeSkin());
        Assert.Equal("0xf6f6f6", result.Theme.GetColor("text_color"));
        Assert.Equal("0xf6f6f6", result.Theme.GetColor("hilited_text_color"));
        Assert.Equal("0xe0e0e0", result.Theme.GetColor("candidate_text_color"));
        Assert.Equal("0x3e9eff", result.Theme.GetColor("hilited_candidate_text_color"));
        Assert.Equal("0x808080", result.Theme.GetColor("comment_text_color"));
    }

    [Fact]
    public void Missing_colors_fall_back_per_documented_table()
    {
        var result = MapDefault(MakeSkin(colors: new SkinColors(null, null, null, null)));
        Assert.Equal("0x000000", result.Theme.GetColor("text_color"));
        Assert.Equal("0x000000", result.Theme.GetColor("candidate_text_color"));
        // hilited candidate falls back to candidate text color
        Assert.Equal("0x000000", result.Theme.GetColor("hilited_candidate_text_color"));
    }

    [Fact]
    public void Uninstalled_fonts_fall_back_to_yahei_with_warning()
    {
        var result = MapDefault(MakeSkin(), new FakeFontChecker("Arial"));
        Assert.StartsWith("Arial", result.Theme.FontFace);
        Assert.EndsWith("Microsoft YaHei", result.Theme.FontFace);
        Assert.DoesNotContain("宋体", result.Theme.FontFace);
        Assert.Contains(result.Diagnostics, d => d.Code == "FONT_NOT_INSTALLED");
    }

    [Fact]
    public void Font_points_follow_source_size()
    {
        var result = MapDefault(MakeSkin());
        Assert.Equal(15, result.Theme.FontPoint);
        Assert.Equal(15, result.Theme.LabelFontPoint);
        Assert.Equal(14, result.Theme.CommentFontPoint);
    }

    [Fact]
    public void Comment_font_point_never_drops_below_8()
    {
        var result = MapDefault(MakeSkin(typography: new SkinTypography(null, null, 8)));
        Assert.Equal(8, result.Theme.CommentFontPoint);
    }

    [Fact]
    public void Layout_margins_come_from_scheme_values()
    {
        var result = MapDefault(MakeSkin());
        Assert.Equal(11, result.Theme.GetLayout("margin_x")); // (10+12)/2
        Assert.Equal(8, result.Theme.GetLayout("margin_y"));  // (8+9)/2
        Assert.Equal(5, result.Theme.GetLayout("spacing"));   // pinyin_marge[1]
        Assert.Equal(4, result.Theme.GetLayout("candidate_spacing")); // zhongwen_marge[0]
    }

    [Theory]
    [InlineData(new[] { 0, -5, 500 })]   // negative inset
    [InlineData(new[] { 0, 235, 97 })]   // real 辐光光 H1 insets: include background artwork
    [InlineData(new[] { 0, 55, 73 })]    // real 辐光光 V1 insets: average 64 is not text padding
    public void Unreasonable_layout_values_fall_back_with_warning(int[] layout)
    {
        var result = MapDefault(MakeSkin(horizontalLayout: layout));
        Assert.Equal(12, result.Theme.GetLayout("margin_x"));
        Assert.Contains(result.Diagnostics, d => d.Code == "LAYOUT_VALUE_INVALID");
    }

    [Fact]
    public void Plausible_insets_are_used_directly()
    {
        // Real 伊蕾娜 H1 insets, which are genuine text padding.
        var result = MapDefault(MakeSkin(horizontalLayout: [0, 11, 10]));
        Assert.Equal(10, result.Theme.GetLayout("margin_x"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "LAYOUT_VALUE_INVALID" && d.SourceKey == "layout_horizontal");
    }

    [Fact]
    public void Min_width_derives_from_background_image()
    {
        var result = MapDefault(MakeSkin());
        Assert.Equal(200, result.Theme.GetLayout("min_width")); // clamp(400/2)
    }

    [Fact]
    public void Analyzed_colors_flow_into_back_and_accent()
    {
        var result = MapDefault(MakeSkin());
        Assert.Equal("0x282828", result.Theme.GetColor("back_color"));
        Assert.Equal("0x5a5a5a", result.Theme.GetColor("border_color"));
        // Dark blue accent contrasts with the light highlight text, so it is kept as-is.
        Assert.Equal("0x2010c0", result.Theme.GetColor("hilited_candidate_back_color"));
    }

    [Fact]
    public void Low_contrast_accent_is_replaced_by_derived_color()
    {
        // Accent identical to the highlighted text color would be unreadable.
        var skin = MakeSkin(colors: new SkinColors("0xf6f6f6", "0x3e9eff", "0xe0e0e0", null));
        var result = Map(skin, new AnalyzedColors("0x282828", "0x5a5a5a", "0x3e9eff"));

        var back = result.Theme.GetColor("hilited_candidate_back_color");
        Assert.NotEqual("0x3e9eff", back);
        Assert.True(WeaselMapper.HasMinimumContrast(back, "0x3e9eff"));
    }

    [Fact]
    public void No_analysis_produces_documented_defaults()
    {
        var result = Map(MakeSkin(), analyzed: null);
        Assert.Equal("0xffffff", result.Theme.GetColor("back_color"));
        Assert.Equal("0xcccccc", result.Theme.GetColor("border_color"));
    }

    [Fact]
    public void Unsupported_features_include_statusbar_and_masks()
    {
        var scheme = new SkinScheme(
            SkinSchemeKind.H1, "back.png", "mask.png", null, null,
            [0, 1, 2], [0, 1, 2], [1, 2, 3, 4], [1, 2], ["overlay.png"]);
        var skin = MakeSkin() with
        {
            Schemes = new Dictionary<SkinSchemeKind, SkinScheme> { [SkinSchemeKind.H1] = scheme },
            StatusBar = new StatusBarDefinition("bar.png", ["bar.png"]),
        };

        var result = MapDefault(skin);
        Assert.Contains(result.UnsupportedFeatures, f => f.Contains("mask"));
        Assert.Contains(result.UnsupportedFeatures, f => f.Contains("overlay"));
        Assert.Contains(result.UnsupportedFeatures, f => f.Contains("StatusBar"));
    }
}
