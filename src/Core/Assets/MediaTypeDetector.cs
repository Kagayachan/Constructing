// SPDX-License-Identifier: GPL-3.0-or-later
namespace Core.Assets;

/// <summary>Signature-based media type detection for skin assets (§4.1: PNG, BMP, GIF).</summary>
public static class MediaTypeDetector
{
    public static string Detect(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 &&
            content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return "image/png";
        }

        if (content.Length >= 6 &&
            content[0] == (byte)'G' && content[1] == (byte)'I' && content[2] == (byte)'F' && content[3] == (byte)'8')
        {
            return "image/gif";
        }

        if (content.Length >= 2 && content[0] == (byte)'B' && content[1] == (byte)'M')
        {
            return "image/bmp";
        }

        if (content.Length >= 4 &&
            content[0] == 0xFF && content[1] == 0xFE)
        {
            return "text/plain; charset=utf-16le";
        }

        return "application/octet-stream";
    }

    public static bool IsImage(string mediaType) => mediaType.StartsWith("image/", StringComparison.Ordinal);
}
