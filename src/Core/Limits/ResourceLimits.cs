// SPDX-License-Identifier: GPL-3.0-or-later
namespace Core.Limits;

/// <summary>
/// Hard upper bounds applied to untrusted skin input so a small malicious or
/// corrupt file cannot exhaust memory during inspect, validate or convert
/// (code review H-01/H-02). All limits are generous relative to real skins
/// (which are a few MB at most) but reject the pathological cases.
/// </summary>
public sealed record ResourceLimits(
    long MaxInputBytes,
    int MaxEntryCount,
    long MaxEntryBytes,
    long MaxTotalUncompressedBytes,
    long MaxLegacyDeclaredBytes,
    long MaxImagePixels,
    int MaxImageDimension)
{
    public static readonly ResourceLimits Default = new(
        MaxInputBytes: 64L * 1024 * 1024,
        MaxEntryCount: 4096,
        MaxEntryBytes: 64L * 1024 * 1024,
        MaxTotalUncompressedBytes: 256L * 1024 * 1024,
        MaxLegacyDeclaredBytes: 256L * 1024 * 1024,
        MaxImagePixels: 40L * 1024 * 1024,
        MaxImageDimension: 16384);
}
