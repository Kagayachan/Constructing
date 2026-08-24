// SPDX-License-Identifier: GPL-3.0-or-later
namespace Core.Package;

/// <summary>Reads one SSF container format into a virtual package (§7.1).</summary>
public interface ISsfPackageReader
{
    SkinPackage Read(byte[] content);
}
