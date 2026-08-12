// SPDX-License-Identifier: GPL-3.0-or-later
namespace Ssf2Weasel.Core.Mapping;

/// <summary>Checks whether a font family is installed on this system (§12.3).</summary>
public interface IFontChecker
{
    bool IsInstalled(string fontFamily);
}
