// SPDX-License-Identifier: GPL-3.0-or-later
//
// The legacy container layout, AES key and IV originate from the public
// GPLv3 ssf2fcitx implementation by VOID001 (https://github.com/VOID001/ssf2fcitx),
// re-implemented here in managed code. See THIRD_PARTY_NOTICES.md.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Package;

namespace Ssf2Weasel.Infrastructure.Ssf;

/// <summary>Reads legacy 'Skin' encrypted SSF files (§8.3).</summary>
public sealed class LegacyEncryptedSsfPackageReader : ISsfPackageReader
{
    internal static readonly byte[] AesKey =
    [
        0x52, 0x36, 0x46, 0x1A, 0xD3, 0x85, 0x03, 0x66, 0x90, 0x45, 0x16, 0x28, 0x79, 0x03, 0x36, 0x23,
        0xDD, 0xBE, 0x6F, 0x03, 0xFF, 0x04, 0xE3, 0xCA, 0xD5, 0x7F, 0xFC, 0xA3, 0x50, 0xE4, 0x9E, 0xD9,
    ];

    internal static readonly byte[] AesIv =
    [
        0xE0, 0x7A, 0xAD, 0x35, 0xE0, 0x90, 0xAA, 0x03, 0x8A, 0x51, 0xFD, 0x05, 0xDF, 0x8C, 0x5D, 0x0F,
    ];

    public bool CanRead(ReadOnlySpan<byte> header)
        => header.Length >= 4 &&
           header[0] == (byte)'S' && header[1] == (byte)'k' &&
           header[2] == (byte)'i' && header[3] == (byte)'n';

    public SkinPackage Read(byte[] content, CancellationToken cancellationToken)
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
            throw new Ssf2WeaselException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfDecryptFailed,
                "AES decryption of the legacy container failed.",
                hint: "The file may be truncated or use an unknown key.",
                inner: ex);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Decrypted stream: UInt32 LE expected decompressed length + zlib data (§8.3.2).
        if (plain.Length < 4)
        {
            throw Structure("The decrypted payload is too small.");
        }

        var expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(plain);
        byte[] decompressed;
        try
        {
            using var input = new MemoryStream(plain, 4, plain.Length - 4);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            decompressed = output.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new Ssf2WeaselException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfPackageStructureInvalid,
                "The zlib stream inside the legacy container is corrupted.",
                inner: ex);
        }

        if (decompressed.Length != expectedLength)
        {
            throw new Ssf2WeaselException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfDecompressedLengthMismatch,
                $"Decompressed length {decompressed.Length} does not match the declared length {expectedLength}.");
        }

        var entries = ParseFilePack(decompressed, cancellationToken);
        return new SkinPackage(SsfContainerKind.LegacyEncrypted, entries, diagnostics);
    }

    /// <summary>Parses the decompressed file pack with full bounds checking (§8.3.3).</summary>
    internal static List<SkinPackageEntry> ParseFilePack(byte[] pack, CancellationToken cancellationToken)
    {
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

        if (headerSize % 4 != 0 || 8 + headerSize > (uint)span.Length)
        {
            throw Structure($"Offset table size {headerSize} is invalid.");
        }

        var offsetCount = (int)(headerSize / 4);
        var entries = new List<SkinPackageEntry>(offsetCount);

        for (var i = 0; i < offsetCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

    private static Ssf2WeaselException Structure(string message) => new(
        ExitCode.PackageError,
        DiagnosticCodes.SsfPackageStructureInvalid,
        message,
        hint: "The legacy skin package appears to be corrupted.");
}
