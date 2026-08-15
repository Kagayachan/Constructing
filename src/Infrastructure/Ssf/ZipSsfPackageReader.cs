// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO.Compression;
using Core;
using Core.Diagnostics;
using Core.Limits;
using Core.Package;

namespace Infrastructure.Ssf;

/// <summary>Reads ZIP-based SSF files fully in memory (§8.2).</summary>
public sealed class ZipSsfPackageReader : ISsfPackageReader
{
    private readonly ResourceLimits _limits;

    public ZipSsfPackageReader(ResourceLimits? limits = null)
    {
        _limits = limits ?? ResourceLimits.Default;
    }

    public bool CanRead(ReadOnlySpan<byte> header)
        => header.Length >= 2 && header[0] == 0x50 && header[1] == 0x4B;

    public SkinPackage Read(byte[] content, CancellationToken cancellationToken)
    {
        var diagnostics = new List<Diagnostic>();
        var entries = new List<SkinPackageEntry>();
        long totalUncompressed = 0;

        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            var accepted = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue; // directory marker
                }

                if (!IsSafeEntryName(entry.FullName))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.SsfUnsafeEntryPath,
                        DiagnosticSeverity.Warning,
                        $"Entry '{entry.FullName}' uses an unsafe path and was rejected.",
                        Asset: entry.FullName));
                    continue;
                }

                if (++accepted > _limits.MaxEntryCount)
                {
                    throw ResourceLimit($"The archive contains more than {_limits.MaxEntryCount} entries.");
                }

                try
                {
                    // Copy through a bounded stream so a zip-bomb entry cannot expand
                    // without limit even though the central directory understates it.
                    var bytes = ReadEntryBounded(entry, ref totalUncompressed, cancellationToken);
                    entries.Add(new SkinPackageEntry(entry.FullName, bytes));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.SsfEntryUnreadable,
                        DiagnosticSeverity.Warning,
                        $"Entry '{entry.FullName}' could not be read: {ex.Message}",
                        Asset: entry.FullName));
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ToolException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfPackageStructureInvalid,
                "The ZIP container is corrupted and cannot be read.",
                hint: "The file may be truncated or not a valid skin package.",
                inner: ex);
        }

        return new SkinPackage(SsfContainerKind.Zip, entries, diagnostics);
    }

    private byte[] ReadEntryBounded(ZipArchiveEntry entry, ref long totalUncompressed, CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Write(chunk, 0, read);
            if (buffer.Length > _limits.MaxEntryBytes)
            {
                throw ResourceLimit(
                    $"Entry '{entry.FullName}' expands beyond the per-entry limit of {_limits.MaxEntryBytes} bytes.");
            }

            if (totalUncompressed + buffer.Length > _limits.MaxTotalUncompressedBytes)
            {
                throw ResourceLimit(
                    $"The archive expands beyond the cumulative limit of {_limits.MaxTotalUncompressedBytes} bytes.");
            }
        }

        totalUncompressed += buffer.Length;
        return buffer.ToArray();
    }

    private static ToolException ResourceLimit(string message) => new(
        ExitCode.PackageError,
        DiagnosticCodes.SsfResourceLimitExceeded,
        message,
        hint: "The skin file is unexpectedly large or malformed and was rejected to protect memory.");

    /// <summary>Rejects absolute paths, drive letters, UNC paths and '..' segments (§17, IT-012).</summary>
    internal static bool IsSafeEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.StartsWith('/') || name.StartsWith('\\') || name.Contains(':'))
        {
            return false;
        }

        var segments = name.Split(['/', '\\'], StringSplitOptions.None);
        return segments.All(s => s.Length > 0 && s != "." && s != "..");
    }
}
