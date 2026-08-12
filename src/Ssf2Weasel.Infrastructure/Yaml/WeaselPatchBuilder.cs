// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core.Mapping;

namespace Ssf2Weasel.Infrastructure.Yaml;

/// <summary>Abstract YAML values so the writer and the merger share one source of truth.</summary>
public abstract record YamlValue;

/// <summary>A string emitted with double-quoted escaping.</summary>
public sealed record YamlString(string Value) : YamlValue;

/// <summary>A plain scalar emitted verbatim: numbers, booleans, 0x colors.</summary>
public sealed record YamlRaw(string Value) : YamlValue;

public sealed record YamlMap(IReadOnlyList<KeyValuePair<string, YamlValue>> Entries) : YamlValue;

/// <summary>
/// Builds the ordered set of patch entries this tool manages (§13.1, §15.4).
/// Keys are Rime patch paths relative to the "patch" root mapping.
/// </summary>
public static class WeaselPatchBuilder
{
    /// <summary>Marker key that identifies a color scheme as managed by this tool (§15.4 force rule).</summary>
    public const string ManagedMarkerKey = "ssf2weasel_managed";

    public static IReadOnlyList<KeyValuePair<string, YamlValue>> Build(WeaselTheme theme)
    {
        var entries = new List<KeyValuePair<string, YamlValue>>
        {
            new("style/color_scheme", new YamlString(theme.ColorSchemeId)),
            new("style/horizontal", new YamlRaw(theme.Horizontal ? "true" : "false")),
            new("style/font_face", new YamlString(theme.FontFace)),
            new("style/label_font_face", new YamlString(theme.LabelFontFace)),
            new("style/comment_font_face", new YamlString(theme.CommentFontFace)),
            new("style/font_point", new YamlRaw(theme.FontPoint.ToString())),
            new("style/label_font_point", new YamlRaw(theme.LabelFontPoint.ToString())),
            new("style/comment_font_point", new YamlRaw(theme.CommentFontPoint.ToString())),
        };

        foreach (var (key, value) in theme.Layout)
        {
            entries.Add(new($"style/layout/{key}", new YamlRaw(value.ToString())));
        }

        var scheme = new List<KeyValuePair<string, YamlValue>>
        {
            new("name", new YamlString(theme.Name)),
        };
        if (!string.IsNullOrWhiteSpace(theme.Author))
        {
            scheme.Add(new("author", new YamlString(theme.Author)));
        }

        scheme.Add(new(ManagedMarkerKey, new YamlRaw("true")));
        foreach (var (key, value) in theme.Colors)
        {
            scheme.Add(new(key, new YamlRaw(value)));
        }

        entries.Add(new($"preset_color_schemes/{theme.ColorSchemeId}", new YamlMap(scheme)));
        return entries;
    }

    /// <summary>Patch paths that this tool owns and may overwrite during a merge (§15.4).</summary>
    public static IReadOnlyList<string> ManagedStylePaths(WeaselTheme theme)
        => Build(theme).Select(e => e.Key).Where(k => k.StartsWith("style/", StringComparison.Ordinal)).ToArray();
}
