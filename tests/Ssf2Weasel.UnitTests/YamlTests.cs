// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Mapping;
using Ssf2Weasel.Infrastructure.Yaml;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Ssf2Weasel.UnitTests;

public static class ThemeFactory
{
    public static WeaselTheme Create(string id = "test_skin", string name = "Test Skin", string? author = "someone")
        => new()
        {
            ColorSchemeId = id,
            Name = name,
            Author = author,
            Horizontal = true,
            FontFace = "宋体, Arial, Microsoft YaHei",
            LabelFontFace = "宋体",
            CommentFontFace = "宋体",
            FontPoint = 15,
            LabelFontPoint = 15,
            CommentFontPoint = 14,
            Colors =
            [
                new("text_color", "0xf6f6f6"),
                new("back_color", "0x282828"),
                new("border_color", "0x5a5a5a"),
                new("hilited_text_color", "0xf6f6f6"),
                new("hilited_back_color", "0x282828"),
                new("candidate_text_color", "0xe0e0e0"),
                new("candidate_back_color", "0x282828"),
                new("comment_text_color", "0x808080"),
                new("hilited_candidate_text_color", "0x3e9eff"),
                new("hilited_candidate_back_color", "0x1a1a1a"),
                new("hilited_comment_text_color", "0x808080"),
                new("shadow_color", "0x00000000"),
            ],
            Layout =
            [
                new("min_width", 160), new("min_height", 0), new("margin_x", 12), new("margin_y", 12),
                new("spacing", 10), new("candidate_spacing", 5), new("hilite_spacing", 4), new("hilite_padding", 2),
                new("border_width", 1), new("corner_radius", 4), new("shadow_radius", 0),
                new("shadow_offset_x", 4), new("shadow_offset_y", 4),
            ],
        };
}

public class YamlWriterTests
{
    [Fact]
    public void Generated_yaml_reparses_and_validates()
    {
        var yaml = WeaselYamlWriter.Write(ThemeFactory.Create(), "1.0.0-test");
        WeaselYamlValidator.ValidateCustomYaml(yaml, ExitCode.ConversionError);
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var a = WeaselYamlWriter.Write(ThemeFactory.Create(), "1.0.0");
        var b = WeaselYamlWriter.Write(ThemeFactory.Create(), "1.0.0");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Hostile_strings_are_escaped_and_survive_roundtrip()
    {
        var hostile = "皮\"肤: \\ \n#not-a-comment\t' [x]";
        var yaml = WeaselYamlWriter.Write(ThemeFactory.Create(name: hostile, author: hostile), "1.0.0");
        var root = WeaselYamlValidator.ParseRoot(yaml, ExitCode.ConversionError);

        var patch = (YamlMappingNode)root.Children[new YamlScalarNode("patch")];
        var scheme = (YamlMappingNode)patch.Children[new YamlScalarNode("preset_color_schemes/test_skin")];
        var name = ((YamlScalarNode)scheme.Children[new YamlScalarNode("name")]).Value;
        Assert.Equal(hostile, name);
    }

    [Fact]
    public void Newlines_are_consistent_lf(
    )
    {
        var yaml = WeaselYamlWriter.Write(ThemeFactory.Create(), "1.0.0");
        Assert.DoesNotContain("\r", yaml);
    }

    [Fact]
    public void Non_normalized_color_fails_validation()
    {
        var yaml = WeaselYamlWriter.Write(ThemeFactory.Create(), "1.0.0")
            .Replace("0xf6f6f6", "0xF6F6F6");
        var ex = Assert.Throws<Ssf2WeaselException>(
            () => WeaselYamlValidator.ValidateCustomYaml(yaml, ExitCode.ConversionError));
        Assert.Equal("YAML_INVALID", ex.Code);
    }
}

public class YamlMergerTests
{
    private const string ExistingYaml = """
        custom_label: "user data"
        patch:
          "style/color_scheme": macau
          "style/display_tray_icon": true
          "translator/dictionary": my_dict
          key_binder/bindings:
            - { when: always, accept: F4, toggle: ascii_mode }
        """;

    [Fact]
    public void Preserves_unrelated_keys_and_activates_new_scheme()
    {
        var merged = WeaselCustomMerger.Merge(ExistingYaml, ThemeFactory.Create(), force: false).MergedYaml;
        var root = WeaselYamlValidator.ParseRoot(merged, ExitCode.InstallError);
        var patch = (YamlMappingNode)root.Children[new YamlScalarNode("patch")];

        Assert.Equal("test_skin", ((YamlScalarNode)patch.Children[new YamlScalarNode("style/color_scheme")]).Value);
        Assert.Equal("true", ((YamlScalarNode)patch.Children[new YamlScalarNode("style/display_tray_icon")]).Value);
        Assert.Equal("my_dict", ((YamlScalarNode)patch.Children[new YamlScalarNode("translator/dictionary")]).Value);
        Assert.True(patch.Children.ContainsKey(new YamlScalarNode("key_binder/bindings")));
        Assert.True(root.Children.ContainsKey(new YamlScalarNode("custom_label")));
        Assert.True(patch.Children.ContainsKey(new YamlScalarNode("preset_color_schemes/test_skin")));

        WeaselYamlValidator.ValidateCustomYaml(merged, ExitCode.InstallError);
    }

    [Fact]
    public void Empty_or_missing_file_produces_fresh_patch()
    {
        var merged = WeaselCustomMerger.Merge(null, ThemeFactory.Create(), force: false).MergedYaml;
        WeaselYamlValidator.ValidateCustomYaml(merged, ExitCode.InstallError);
    }

    [Fact]
    public void Conflict_with_foreign_scheme_throws_even_with_force()
    {
        const string existing = """
            patch:
              "preset_color_schemes/test_skin":
                name: "hand made"
                text_color: 0x000000
            """;
        var ex = Assert.Throws<Ssf2WeaselException>(
            () => WeaselCustomMerger.Merge(existing, ThemeFactory.Create(), force: true));
        Assert.Equal(ExitCode.OutputConflict, ex.ExitCode);
    }

    [Fact]
    public void Conflict_with_managed_scheme_requires_force()
    {
        var first = WeaselCustomMerger.Merge(null, ThemeFactory.Create(), force: false).MergedYaml;

        var ex = Assert.Throws<Ssf2WeaselException>(
            () => WeaselCustomMerger.Merge(first, ThemeFactory.Create(), force: false));
        Assert.Equal(ExitCode.OutputConflict, ex.ExitCode);

        var replaced = WeaselCustomMerger.Merge(first, ThemeFactory.Create(), force: true);
        Assert.True(replaced.ReplacedExistingScheme);
        WeaselYamlValidator.ValidateCustomYaml(replaced.MergedYaml, ExitCode.InstallError);
    }

    [Fact]
    public void Nested_style_mapping_is_updated_in_place()
    {
        const string existing = """
            patch:
              style:
                color_scheme: macau
                inline_preedit: true
            """;
        var merged = WeaselCustomMerger.Merge(existing, ThemeFactory.Create(), force: false).MergedYaml;
        var root = WeaselYamlValidator.ParseRoot(merged, ExitCode.InstallError);
        var patch = (YamlMappingNode)root.Children[new YamlScalarNode("patch")];
        var style = (YamlMappingNode)patch.Children[new YamlScalarNode("style")];

        Assert.Equal("test_skin", ((YamlScalarNode)style.Children[new YamlScalarNode("color_scheme")]).Value);
        Assert.Equal("true", ((YamlScalarNode)style.Children[new YamlScalarNode("inline_preedit")]).Value);
        Assert.False(patch.Children.ContainsKey(new YamlScalarNode("style/color_scheme")));
    }
}
