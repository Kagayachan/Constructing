// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO.Compression;
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Package;

namespace Ssf2Weasel.Infrastructure.Ssf;

/// <summary>Reads ZIP-based SSF files fully in memory (§8.2).</summary>
public sealed class ZipSsfPackageReader : ISsfPackageReader
{
    public bool CanRead(ReadOnlySpan<byte> header)
        => header.Length >= 2 && header[0] == 0x50 && header[1] == 0x4B;

    public SkinPackage Read(byte[] content, CancellationToken cancellationToken)
    {
        var diagnostics = new List<Diagnostic>();
        var entries = new List<SkinPackageEntry>();

        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
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

                try
                {
                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    entries.Add(new SkinPackageEntry(entry.FullName, buffer.ToArray()));
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
            throw new Ssf2WeaselException(
                ExitCode.PackageError,
                DiagnosticCodes.SsfPackageStructureInvalid,
                "The ZIP container is corrupted and cannot be read.",
                hint: "The file may be truncated or not a valid skin package.",
                inner: ex);
        }

        return new SkinPackage(SsfContainerKind.Zip, entries, diagnostics);
    }

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
