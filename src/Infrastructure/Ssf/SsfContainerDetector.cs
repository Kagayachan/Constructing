// SPDX-License-Identifier: GPL-3.0-or-later
using Core;
using Core.Diagnostics;
using Core.Limits;
using Core.Package;

namespace Infrastructure.Ssf;

/// <summary>Detects the SSF container format by file signature, never by extension (§8.1).</summary>
public static class SsfContainerDetector
{
    public static SsfContainerKind Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B &&
            ((header[2] == 0x03 && header[3] == 0x04) ||
             (header[2] == 0x05 && header[3] == 0x06) ||
             (header[2] == 0x07 && header[3] == 0x08)))
        {
            return SsfContainerKind.Zip;
        }

        if (header.Length >= 4 &&
            header[0] == (byte)'S' && header[1] == (byte)'k' &&
            header[2] == (byte)'i' && header[3] == (byte)'n')
        {
            return SsfContainerKind.LegacyEncrypted;
        }

        throw new ToolException(
            ExitCode.UnsupportedContainer,
            DiagnosticCodes.SsfUnsupportedContainer,
            "The input is neither a ZIP-based nor a legacy 'Skin' encrypted SSF file.",
            hint: "Verify that the input is a Sogou skin file.");
    }

    public static ISsfPackageReader CreateReader(SsfContainerKind kind, ResourceLimits? limits = null) => kind switch
    {
        SsfContainerKind.Zip => new ZipSsfPackageReader(limits),
        SsfContainerKind.LegacyEncrypted => new LegacyEncryptedSsfPackageReader(limits),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
