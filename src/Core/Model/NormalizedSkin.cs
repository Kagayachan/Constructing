// SPDX-License-Identifier: GPL-3.0-or-later
using Core.Diagnostics;

namespace Core.Model;

public enum SkinSchemeKind
{
    H1,
    H2,
    V1,
    V2,
}

public enum LayoutKind
{
    Horizontal,
    Vertical,
}

public sealed record SkinMetadata(
    string Name,
    string? Version,
    string? Author,
    string? Email,
    string? CreatedAt,
    string? Description);

public sealed record SkinTypography(
    string? ChineseFont,
    string? LatinFont,
    int? FontSize);

/// <summary>Normalized "0x..." BGR color strings; null when absent or invalid in the source.</summary>
public sealed record SkinColors(
    string? Pinyin,
    string? FirstCandidate,
    string? OtherCandidate,
    string? CompositionHint,
    bool Glow = false);

public sealed record SkinScheme(
    SkinSchemeKind Kind,
    string? BackgroundAsset,
    string? BackgroundMaskAsset,
    string? PinyinBackgroundAsset,
    string? CandidateBackgroundAsset,
    IReadOnlyList<int?> HorizontalLayout,
    IReadOnlyList<int?> VerticalLayout,
    IReadOnlyList<int?> PinyinMargin,
    IReadOnlyList<int?> CandidateMargin,
    IReadOnlyList<string> Overlays,
    string? TransparentColor = null)
{
    /// <summary>The best asset for color analysis: whole background, else candidate area, else pinyin area.</summary>
    public string? PrimaryVisualAsset => BackgroundAsset ?? CandidateBackgroundAsset ?? PinyinBackgroundAsset;
}

public sealed record SkinAsset(
    string OriginalName,
    string MediaType,
    int? Width,
    int? Height,
    int? FrameCount,
    string Sha256);

/// <summary>Platform-independent skin model (§10) built before any Weasel-specific mapping.</summary>
public sealed record NormalizedSkin(
    SkinMetadata Metadata,
    SkinTypography Typography,
    SkinColors Colors,
    IReadOnlyDictionary<SkinSchemeKind, SkinScheme> Schemes,
    bool HasStatusBar,
    IReadOnlyDictionary<string, SkinAsset> Assets,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> UnknownSections);
