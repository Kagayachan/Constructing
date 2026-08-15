// SPDX-License-Identifier: GPL-3.0-or-later
using System.Drawing.Text;
using Core.Mapping;

namespace Infrastructure.Imaging;

/// <summary>Checks installed font families via GDI+ (§12.3 font fallback rule).</summary>
public sealed class GdiFontChecker : IFontChecker
{
    private readonly Lazy<HashSet<string>> _families = new(() =>
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    public bool IsInstalled(string fontFamily) => _families.Value.Contains(fontFamily.Trim());
}
