// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Infrastructure.Ssf;

namespace Ssf2Weasel.TestSupport;

/// <summary>
/// Builds copyright-free synthetic SSF fixtures for both container formats
/// (§19.3). The legacy packer is the inverse of LegacyEncryptedSsfPackageReader.
/// </summary>
public static class SyntheticSsf
{
    public const string DefaultSkinIni = """
        [General]
        skin_name=Synthetic Test Skin
        skin_version=1.0
        skin_author=fixture
        skin_email=fixture@example.invalid

        [Display]
        font_size=14
        font_ch=Microsoft YaHei
        font_en=Arial
        pinyin_color=0xf6f6f6
        zhongwen_first_color=0x3e9eff
        zhongwen_color=0xe0e0e0
        comphint_color=0x808080

        [Scheme_H1]
        pic=back.png
        layout_horizontal=0,10,12
        layout_vertical=0,8,9
        pinyin_marge=6,5,10,10
        zhongwen_marge=4,6

        [Scheme_V1]
        pic=back.png
        layout_horizontal=0,10,12
        layout_vertical=0,8,9
        pinyin_marge=6,5,10,10
        zhongwen_marge=4,6

        [StatusBar]
        pic=bar.png
        """;

    /// <summary>skin.ini encoded as UTF-16LE with BOM, the dominant real-world encoding (§9.1).</summary>
    public static byte[] EncodeUtf16Le(string text, bool withBom = true)
    {
        var body = Encoding.Unicode.GetBytes(text.ReplaceLineEndings("\r\n"));
        if (!withBom)
        {
            return body;
        }

        return [0xFF, 0xFE, .. body];
    }

    /// <summary>A small solid-color PNG with a distinct 1px border, for color analysis tests.</summary>
    public static byte[] SolidPng(int width, int height, Color fill, Color? border = null)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(fill);
            if (border is { } b)
            {
                using var pen = new Pen(b, 1);
                g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public static Dictionary<string, byte[]> DefaultFiles() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["skin.ini"] = EncodeUtf16Le(DefaultSkinIni),
        ["back.png"] = SolidPng(120, 48, Color.FromArgb(255, 40, 40, 48), Color.FromArgb(255, 90, 90, 100)),
        ["bar.png"] = SolidPng(64, 24, Color.FromArgb(255, 40, 40, 48)),
    };

    public static byte[] CreateZipSkin(IDictionary<string, byte[]>? files = null)
    {
        files ??= DefaultFiles();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Builds a legacy encrypted SSF: file pack → zlib → length prefix → AES-256-CBC → 'Skin' header (§8.3).</summary>
    public static byte[] CreateLegacySkin(IDictionary<string, byte[]>? files = null)
    {
        files ??= DefaultFiles();
        var pack = BuildFilePack(files);

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(pack);
            }

            compressed = output.ToArray();
        }

        var payload = new byte[4 + compressed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)pack.Length);
        compressed.CopyTo(payload, 4);

        // Pad to the AES block size; trailing zeros after the zlib stream are ignored by the reader.
        var paddedLength = (payload.Length + 15) / 16 * 16;
        Array.Resize(ref payload, paddedLength);

        using var aes = Aes.Create();
        aes.Key = LegacyEncryptedSsfPackageReader.AesKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        var cipher = aes.EncryptCbc(payload, LegacyEncryptedSsfPackageReader.AesIv, PaddingMode.None);

        return [(byte)'S', (byte)'k', (byte)'i', (byte)'n', 0, 0, 0, 0, .. cipher];
    }

    /// <summary>Builds the §8.3.3 file pack: sizes, offset table, then UTF-16LE named blobs.</summary>
    public static byte[] BuildFilePack(IDictionary<string, byte[]> files)
    {
        var names = files.Keys.ToArray();
        var headerSize = names.Length * 4;
        var blobs = new List<byte[]>(names.Length);

        foreach (var name in names)
        {
            var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
            var content = files[name];
            var blob = new byte[4 + nameBytes.Length + 4 + content.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(blob, (uint)nameBytes.Length);
            nameBytes.CopyTo(blob, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(4 + nameBytes.Length), (uint)content.Length);
            content.CopyTo(blob, 4 + nameBytes.Length + 4);
            blobs.Add(blob);
        }

        var totalSize = 8 + headerSize + blobs.Sum(b => b.Length);
        var pack = new byte[totalSize];
        BinaryPrimitives.WriteUInt32LittleEndian(pack, (uint)totalSize);
        BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan(4), (uint)headerSize);

        var offset = 8 + headerSize;
        for (var i = 0; i < blobs.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan(8 + i * 4), (uint)offset);
            blobs[i].CopyTo(pack, offset);
            offset += blobs[i].Length;
        }

        return pack;
    }
}
