// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace Core.Colors;

/// <summary>
/// Normalizes Sogou color values (§12.4). Sogou and Weasel both use BGR/ABGR
/// hex order (verified against ssf2fcitx and sample pixels), so digits are
/// zero-padded and passed through without channel swapping.
/// </summary>
public static class ColorNormalizer
{
    /// <summary>
    /// Returns a normalized lowercase color like "0x00a1b2" (6 digits) or
    /// "0x80a1b2c3" (8 digits), or null when the value is not valid hex.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        if (s.Length is < 1 or > 8 || !s.All(Uri.IsHexDigit))
        {
            return null;
        }

        var width = s.Length <= 6 ? 6 : 8;
        return "0x" + s.PadLeft(width, '0').ToLowerInvariant();
    }

    /// <summary>Parses a normalized "0x..." string into (r, g, b, a) assuming ABGR/BGR digit order.</summary>
    public static (byte R, byte G, byte B, byte A) ToRgba(string normalized)
    {
        var digits = normalized[2..];
        var value = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = (byte)((value >> 16) & 0xFF);
        var g = (byte)((value >> 8) & 0xFF);
        var r = (byte)(value & 0xFF);
        // 6-digit colors are fully opaque; 8-digit colors carry alpha in the top byte.
        var a = digits.Length == 8 ? (byte)((value >> 24) & 0xFF) : (byte)0xFF;
        return (r, g, b, a);
    }

    /// <summary>Builds a normalized 6-digit BGR string from RGB components.</summary>
    public static string FromRgb(byte r, byte g, byte b)
        => $"0x{b:x2}{g:x2}{r:x2}";

    /// <summary>Builds a normalized 8-digit ABGR string from RGBA components.</summary>
    public static string FromRgba(byte r, byte g, byte b, byte a)
        => $"0x{a:x2}{b:x2}{g:x2}{r:x2}";
}
