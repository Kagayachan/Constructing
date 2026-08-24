// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using Core.Assets;
using Core.Colors;
using Core.Diagnostics;
using Core.Ini;
using Core.Package;

namespace Core.Model;

/// <summary>Builds the platform-independent skin model (§10) from a package and its parsed skin.ini.</summary>
public static class NormalizedSkinBuilder
{
    public static NormalizedSkin Build(
        SkinPackage package,
        SkinIniDocument ini,
        string fallbackName,
        IImageMetadataReader imageReader,
        IEnumerable<Diagnostic> parseDiagnostics)
    {
        var diagnostics = new List<Diagnostic>(parseDiagnostics);
        diagnostics.AddRange(package.Diagnostics);

        var general = ini.GetSection("General");
        var display = ini.GetSection("Display");

        var metadata = new SkinMetadata(
            Name: FirstNonEmpty(general?.Get("skin_name"), fallbackName)!,
            Version: general?.Get("skin_version"),
            Author: general?.Get("skin_author"),
            Email: general?.Get("skin_email"),
            CreatedAt: general?.Get("skin_time"),
            Description: general?.Get("skin_info"));

        var typography = new SkinTypography(
            ChineseFont: FirstNonEmpty(display?.Get("font_ch")),
            LatinFont: FirstNonEmpty(display?.Get("font_en")),
            FontSize: display?.GetInt("font_size"));

        var colors = new SkinColors(
            Pinyin: NormalizeColor(display, general, "pinyin_color", diagnostics),
            FirstCandidate: NormalizeColor(display, general, "zhongwen_first_color", diagnostics),
            OtherCandidate: NormalizeColor(display, general, "zhongwen_color", diagnostics),
            CompositionHint: NormalizeColor(display, general, "comphint_color", diagnostics),
            Glow: display?.GetInt("glow") == 1);

        var schemes = new Dictionary<SkinSchemeKind, SkinScheme>();
        foreach (var kind in Enum.GetValues<SkinSchemeKind>())
        {
            var section = ini.GetSection($"Scheme_{kind}");
            if (section is null)
            {
                continue;
            }

            schemes[kind] = BuildScheme(kind, section);
        }

        // The Sogou status bar has no Weasel equivalent, so only its presence is
        // modelled; it is reported as an expected degradation and nothing more.
        var hasStatusBar = ini.GetSection("StatusBar") is not null;

        var assets = BuildAssets(package, imageReader, diagnostics);

        var unknownSections = ini.Sections
            .Where(s => s.Name.Length > 0 && !SkinIniParser.KnownSections.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .Select(s => s.Name)
            .ToArray();

        return new NormalizedSkin(
            metadata, typography, colors, schemes, hasStatusBar, assets, diagnostics, unknownSections);
    }

    private static SkinScheme BuildScheme(SkinSchemeKind kind, IniSection section)
    {
        var overlays = new List<string>();
        foreach (var entry in section.Entries)
        {
            if (entry.Key.StartsWith("custom", StringComparison.OrdinalIgnoreCase) &&
                !entry.Key.EndsWith("_display", StringComparison.OrdinalIgnoreCase) &&
                entry.Value.Length > 0)
            {
                overlays.Add(entry.Value);
            }
        }

        // Older BMP-based skins declare a chroma key instead of using an alpha channel.
        var transparentColor = section.GetInt("transparent_color_enable") == 1
            ? ColorNormalizer.Normalize(section.Get("transparent_color"))
            : null;

        return new SkinScheme(
            Kind: kind,
            BackgroundAsset: FirstNonEmpty(section.Get("pic")),
            BackgroundMaskAsset: FirstNonEmpty(section.Get("pic_mask")),
            PinyinBackgroundAsset: FirstNonEmpty(section.Get("pinyin_pic")),
            CandidateBackgroundAsset: FirstNonEmpty(section.Get("zhongwen_pic")),
            HorizontalLayout: ParseIntList(section, "layout_horizontal"),
            VerticalLayout: ParseIntList(section, "layout_vertical"),
            PinyinMargin: ParseIntList(section, "pinyin_marge"),
            CandidateMargin: ParseIntList(section, "zhongwen_marge"),
            Overlays: overlays,
            TransparentColor: transparentColor);
    }

    private static Dictionary<string, SkinAsset> BuildAssets(
        SkinPackage package,
        IImageMetadataReader imageReader,
        List<Diagnostic> diagnostics)
    {
        var assets = new Dictionary<string, SkinAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in package.Entries)
        {
            var mediaType = MediaTypeDetector.Detect(entry.Content);
            int? width = null, height = null, frameCount = null;

            if (MediaTypeDetector.IsImage(mediaType))
            {
                var meta = imageReader.TryRead(entry.Content);
                if (meta is null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.AssetUndecodable,
                        DiagnosticSeverity.Warning,
                        $"Image '{entry.Name}' could not be decoded; only raw metadata is reported.",
                        Asset: entry.Name));
                }
                else
                {
                    width = meta.Width;
                    height = meta.Height;
                    frameCount = meta.FrameCount;
                }
            }

            assets[entry.Name] = new SkinAsset(
                OriginalName: entry.Name,
                MediaType: mediaType,
                Width: width,
                Height: height,
                FrameCount: frameCount,
                Sha256: Convert.ToHexStringLower(SHA256.HashData(entry.Content)));
        }

        return assets;
    }

    private static string? NormalizeColor(
        IniSection? display,
        IniSection? general,
        string key,
        List<Diagnostic> diagnostics)
    {
        var raw = display?.Get(key) ?? general?.Get(key);
        if (raw is null)
        {
            return null;
        }

        var normalized = ColorNormalizer.Normalize(raw);
        if (normalized is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ColorInvalid,
                DiagnosticSeverity.Warning,
                $"Color value '{raw}' for '{key}' is not valid hex; a fallback will be used.",
                SourceSection: display?.Contains(key) == true ? "Display" : "General",
                SourceKey: key));
        }

        return normalized;
    }

    private static IReadOnlyList<int?> ParseIntList(IniSection section, string key)
    {
        var parts = section.GetList(key);
        var result = new List<int?>(parts.Count);
        foreach (var part in parts)
        {
            // Preserve positions: an unparsable token becomes null rather than being
            // dropped, so downstream positional mapping cannot silently shift values
            // and misreport "no layout fallback" (code review M-08).
            if (int.TryParse(part, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                result.Add(value);
            }
            else
            {
                result.Add(null);
            }
        }

        return result;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
