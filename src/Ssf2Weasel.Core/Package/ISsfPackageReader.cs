// SPDX-License-Identifier: GPL-3.0-or-later
namespace Ssf2Weasel.Core.Package;

/// <summary>Reads one SSF container format into a virtual package (§7.1).</summary>
public interface ISsfPackageReader
{
    bool CanRead(ReadOnlySpan<byte> header);

    SkinPackage Read(byte[] content, CancellationToken cancellationToken);
}
