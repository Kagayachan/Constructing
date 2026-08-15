// SPDX-License-Identifier: GPL-3.0-or-later
namespace Core.Mapping;

/// <summary>
/// The generated Weasel style: everything needed to emit weasel.custom.yaml (§13.1)
/// and to render the deterministic preview (§13.3).
/// </summary>
public sealed class WeaselTheme
{
    public required string ColorSchemeId { get; init; }

    public required string Name { get; init; }

    public string? Author { get; init; }

    public required bool Horizontal { get; init; }

    public required string FontFace { get; init; }

    public required string LabelFontFace { get; init; }

    public required string CommentFontFace { get; init; }

    public required int FontPoint { get; init; }

    public required int LabelFontPoint { get; init; }

    public required int CommentFontPoint { get; init; }

    /// <summary>Weasel color key → normalized "0x..." value, in stable emission order.</summary>
    public required IReadOnlyList<KeyValuePair<string, string>> Colors { get; init; }

    /// <summary>style/layout/* key → value, in stable emission order.</summary>
    public required IReadOnlyList<KeyValuePair<string, int>> Layout { get; init; }

    public string GetColor(string key) => Colors.First(c => c.Key == key).Value;

    public int GetLayout(string key) => Layout.First(l => l.Key == key).Value;
}

public sealed record ConversionOptions(Model.LayoutKind Layout);

public sealed record MappingRecord(string Source, string Target, string Value);

public sealed record ConversionResult(
    WeaselTheme Theme,
    Model.SkinSchemeKind SourceScheme,
    IReadOnlyList<Diagnostics.Diagnostic> Diagnostics,
    IReadOnlyList<MappingRecord> Mappings,
    IReadOnlyList<string> UnsupportedFeatures);
