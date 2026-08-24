// SPDX-License-Identifier: GPL-3.0-or-later
//
// The legacy container layout, AES key and IV originate from the public
// GPLv3 ssf2fcitx implementation by VOID001 (https://github.com/VOID001/ssf2fcitx),
// re-implemented here in managed code. See THIRD_PARTY_NOTICES.md.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Core;
using Core.Diagnostics;
using Core.Limits;
using Core.Package;

namespace Infrastructure.Ssf;

/// <summary>Reads legacy 'Skin' encrypted SSF files (§8.3).</summary>
public sealed class LegacyEncryptedSsfPackageReader : ISsfPackageReader
{
    private readonly ResourceLimits _limits;

    public LegacyEncryptedSsfPackageReader(ResourceLimits? limits = null)
    {
        _limits = limits ?? ResourceLimits.Default;
    }

    internal static readonly byte[] AesKey =
    [
        0x52, 0x36, 0x46, 0x1A, 0xD3, 0x85, 0x03, 0x66, 0x90, 0x45, 0x16, 0x28, 0x79, 0x03, 0x36, 0x23,
        0xDD, 0xBE, 0x6F, 0x03, 0xFF, 0x04, 0xE3, 0xCA, 0xD5, 0x7F, 0xFC, 0xA3, 0x50, 0xE4, 0x9E, 0xD9,
    ];

    internal static readonly byte[] AesIv =
    [
        0xE0, 0x7A, 0xAD, 0x35, 0xE0, 0x90, 0xAA, 0x03, 0x8A, 0x51, 0xFD, 0x05, 0xDF, 0x8C, 0x5D, 0x0F,
    ];

    public SkinPackage Read(byte[] content)
    {
        if (content.Length < 8 + 16)
        {
            throw Structure("The file is too small to contain an encrypted payload.");
        }

        // Bytes 4..7: version/reserved field, read and recorded but not constrained (§8.3.1).
        var reserved = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(4, 4));
        var diagnostics = new List<Diagnostic>
        {
            new(
                "SSF_LEGACY_HEADER",
                DiagnosticSeverity.Info,
                $"Legacy container header field: 0x{reserved:x8}."),
        };

        var cipher = content.AsSpan(8);
        if (cipher.Length % 16 != 0)
        {
            throw Structure($"Ciphertext length {cipher.Length} is not a multiple of the AES block size.");
        }

        byte[] plain;
        try
        {
            using var aes = Aes.Create();
            aes.Key = AesKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            plain = aes.DecryptCbc(cipher, AesIv, PaddingMode.None);
        }
        catch (CryptographicException ex)
        {
            throw new ToolException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfDecryptFailed,
                "AES decryption of the legacy container failed.",
                hint: "The file may be truncated or use an unknown key.",
                inner: ex);
        }

        // Decrypted stream: UInt32 LE expected decompressed length + zlib data (§8.3.2).
        if (plain.Length < 4)
        {
            throw Structure("The decrypted payload is too small.");
        }

        var expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(plain);
        if (expectedLength > _limits.MaxLegacyDeclaredBytes)
        {
            // Reject an oversized declared length before allocating anything for it.
            throw ResourceLimit(
                $"Declared decompressed length {expectedLength} exceeds the limit of {_limits.MaxLegacyDeclaredBytes} bytes.");
        }

        byte[] decompressed;
        try
        {
            using var input = new MemoryStream(plain, 4, plain.Length - 4);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            // Stop after one byte beyond the declared length so a corrupt/hostile
            // stream cannot expand without bound (code review H-01).
            decompressed = ReadBounded(zlib, expectedLength, _limits.MaxTotalUncompressedBytes);
        }
        catch (InvalidDataException ex)
        {
            throw new ToolException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfPackageStructureInvalid,
                "The zlib stream inside the legacy container is corrupted.",
                inner: ex);
        }

        if (decompressed.Length != expectedLength)
        {
            throw new ToolException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfDecompressedLengthMismatch,
                $"Decompressed length {decompressed.Length} does not match the declared length {expectedLength}.");
        }

        var entries = ParseFilePack(decompressed, _limits);
        return new SkinPackage(entries, diagnostics);
    }

    /// <summary>
    /// Reads the stream into a buffer, aborting if it produces more than
    /// <paramref name="expected"/> bytes (a hard cap of expected + 1 is enough to
    /// detect the overrun) or exceeds the absolute uncompressed cap.
    /// </summary>
    private static byte[] ReadBounded(Stream stream, long expected, long absoluteCap)
    {
        var hardCap = Math.Min(absoluteCap, expected) + 1;
        using var output = new MemoryStream(expected > 0 && expected <= int.MaxValue ? (int)expected : 0);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > hardCap)
            {
                throw new ToolException(
                    ExitCode.PackageError,
                    DiagnosticCodes.SsfDecompressedLengthMismatch,
                    $"The zlib stream expanded beyond the declared length {expected}.");
            }
        }

        return output.ToArray();
    }

    /// <summary>Parses the decompressed file pack with full bounds checking (§8.3.3).</summary>
    internal static List<SkinPackageEntry> ParseFilePack(byte[] pack, ResourceLimits? limits = null)
    {
        limits ??= ResourceLimits.Default;
        var span = pack.AsSpan();
        if (span.Length < 8)
        {
            throw Structure("The file pack is too small for its fixed header.");
        }

        var totalSize = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        if (totalSize > (uint)span.Length)
        {
            throw Structure($"Declared total size {totalSize} exceeds the pack size {span.Length}.");
        }

        // Subtraction-safe comparison: '8 + headerSize' would wrap in uint arithmetic
        // and let a value like 0xFFFFFFFC pass, driving a multi-GB allocation (H-02).
        if (headerSize % 4 != 0 || headerSize > (uint)span.Length - 8)
        {
            throw Structure($"Offset table size {headerSize} is invalid.");
        }

        var offsetCount = (int)(headerSize / 4);
        if (offsetCount > limits.MaxEntryCount)
        {
            throw ResourceLimit($"Offset table declares {offsetCount} entries, exceeding the limit of {limits.MaxEntryCount}.");
        }

        var entries = new List<SkinPackageEntry>(offsetCount);

        for (var i = 0; i < offsetCount; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(8 + i * 4)..]);
            if (offset > (uint)span.Length - 4)
            {
                throw Structure($"Entry offset {offset} is outside the pack.");
            }

            var pos = (int)offset;
            var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
            if (nameLength % 2 != 0)
            {
                throw Structure($"Filename byte length {nameLength} at offset {offset} is not even.");
            }

            pos += 4;
            if (nameLength > (uint)(span.Length - pos))
            {
                throw Structure($"Filename at offset {offset} extends past the end of the pack.");
            }

            var name = Encoding.Unicode.GetString(span.Slice(pos, (int)nameLength)).TrimEnd('\0');
            pos += (int)nameLength;

            if (pos + 4 > span.Length)
            {
                throw Structure($"Entry '{name}' is missing its content length field.");
            }

            var contentLength = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
            pos += 4;
            if (contentLength > (uint)(span.Length - pos))
            {
                throw Structure($"Content of entry '{name}' extends past the end of the pack.");
            }

            entries.Add(new SkinPackageEntry(name, span.Slice(pos, (int)contentLength).ToArray()));
        }

        return entries;
    }

    private static ToolException Structure(string message) => new(
        ExitCode.PackageError,
        DiagnosticCodes.SsfPackageStructureInvalid,
        message,
        hint: "The legacy skin package appears to be corrupted.");

    private static ToolException ResourceLimit(string message) => new(
        ExitCode.PackageError,
        DiagnosticCodes.SsfResourceLimitExceeded,
        message,
        hint: "The skin file is unexpectedly large or malformed and was rejected to protect memory.");
}
