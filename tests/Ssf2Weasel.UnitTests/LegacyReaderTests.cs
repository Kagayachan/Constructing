// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using Ssf2Weasel.Core;
using Ssf2Weasel.Infrastructure.Ssf;
using Ssf2Weasel.TestSupport;
using Xunit;

namespace Ssf2Weasel.UnitTests;

public class LegacyReaderTests
{
    [Fact]
    public void Roundtrip_of_synthetic_legacy_skin_preserves_all_files()
    {
        var files = SyntheticSsf.DefaultFiles();
        files["中文名字.png"] = SyntheticSsf.SolidPng(8, 8, System.Drawing.Color.Red);

        var ssf = SyntheticSsf.CreateLegacySkin(files);
        var package = new LegacyEncryptedSsfPackageReader().Read(ssf, CancellationToken.None);

        Assert.Equal(files.Count, package.Entries.Count);
        foreach (var (name, content) in files)
        {
            Assert.True(package.TryGetEntry(name, out var entry), $"missing entry {name}");
            Assert.Equal(content, entry.Content);
        }
    }

    [Fact]
    public void Filename_lookup_is_case_insensitive()
    {
        var ssf = SyntheticSsf.CreateLegacySkin();
        var package = new LegacyEncryptedSsfPackageReader().Read(ssf, CancellationToken.None);
        Assert.True(package.TryGetEntry("SKIN.INI", out _));
    }

    [Fact]
    public void Truncated_file_fails_with_package_error()
    {
        var ssf = SyntheticSsf.CreateLegacySkin();
        var truncated = ssf[..(ssf.Length / 2 / 16 * 16 + 8)]; // keep block alignment, drop the tail

        var ex = Assert.Throws<Ssf2WeaselException>(
            () => new LegacyEncryptedSsfPackageReader().Read(truncated, CancellationToken.None));
        Assert.Equal(ExitCode.PackageError, ex.ExitCode);
    }

    [Fact]
    public void Misaligned_ciphertext_fails_with_package_error()
    {
        var ssf = SyntheticSsf.CreateLegacySkin();
        var misaligned = ssf[..^3];

        var ex = Assert.Throws<Ssf2WeaselException>(
            () => new LegacyEncryptedSsfPackageReader().Read(misaligned, CancellationToken.None));
        Assert.Equal(ExitCode.PackageError, ex.ExitCode);
    }

    [Fact]
    public void Declared_length_mismatch_is_detected()
    {
        // Build a valid pack, then corrupt the declared decompressed length before encryption.
        var pack = SyntheticSsf.BuildFilePack(SyntheticSsf.DefaultFiles());
        var wrongLength = (uint)pack.Length + 5;

        using var output = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(pack);
        }

        var payload = new byte[4 + output.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, wrongLength);
        output.ToArray().CopyTo(payload, 4);
        Array.Resize(ref payload, (payload.Length + 15) / 16 * 16);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = LegacyEncryptedSsfPackageReader.AesKey;
        var cipher = aes.EncryptCbc(payload, LegacyEncryptedSsfPackageReader.AesIv, System.Security.Cryptography.PaddingMode.None);
        byte[] ssf = [(byte)'S', (byte)'k', (byte)'i', (byte)'n', 0, 0, 0, 0, .. cipher];

        var ex = Assert.Throws<Ssf2WeaselException>(
            () => new LegacyEncryptedSsfPackageReader().Read(ssf, CancellationToken.None));
        Assert.Equal("SSF_DECOMPRESSED_LENGTH_MISMATCH", ex.Code);
    }

    [Fact]
    public void Odd_filename_length_is_rejected()
    {
        var pack = SyntheticSsf.BuildFilePack(new Dictionary<string, byte[]> { ["a.txt"] = [1, 2, 3] });
        // Corrupt the filename byte length at the first entry offset (8 + 4 = 12).
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan((int)offset), 7);

        var ex = Assert.Throws<Ssf2WeaselException>(
            () => LegacyEncryptedSsfPackageReader.ParseFilePack(pack, CancellationToken.None));
        Assert.Equal(ExitCode.PackageError, ex.ExitCode);
    }

    [Fact]
    public void Out_of_bounds_offset_is_rejected()
    {
        var pack = SyntheticSsf.BuildFilePack(new Dictionary<string, byte[]> { ["a.txt"] = [1, 2, 3] });
        BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan(8), (uint)pack.Length + 100);

        var ex = Assert.Throws<Ssf2WeaselException>(
            () => LegacyEncryptedSsfPackageReader.ParseFilePack(pack, CancellationToken.None));
        Assert.Equal(ExitCode.PackageError, ex.ExitCode);
    }

    [Fact]
    public void Filename_trailing_nul_is_removed()
    {
        var files = new Dictionary<string, byte[]> { ["name.png"] = [9] };
        var pack = SyntheticSsf.BuildFilePack(files);
        var entries = LegacyEncryptedSsfPackageReader.ParseFilePack(pack, CancellationToken.None);
        Assert.Equal("name.png", entries.Single().Name);
    }
}
