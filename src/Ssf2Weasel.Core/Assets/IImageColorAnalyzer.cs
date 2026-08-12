// SPDX-License-Identifier: GPL-3.0-or-later
namespace Ssf2Weasel.Core.Assets;

/// <summary>
/// Colors derived from the source scheme's background image (§12.4), as
/// normalized "0x..." BGR/ABGR strings.
/// </summary>
public sealed record AnalyzedColors(
    string BackColor,
    string BorderColor,
    string? AccentColor);

/// <summary>Deterministic image color analysis; implemented in Infrastructure.</summary>
public interface IImageColorAnalyzer
{
    /// <summary>
    /// Returns null when the image cannot be decoded.
    /// <paramref name="transparentColor"/> is the scheme's declared chroma key
    /// (normalized "0x..." BGR); matching pixels are excluded from analysis.
    /// </summary>
    AnalyzedColors? Analyze(byte[] imageContent, byte[]? maskContent, string? transparentColor);
}
