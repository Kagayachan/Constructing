// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core.Diagnostics;

namespace Ssf2Weasel.Core.Package;

public enum SsfContainerKind
{
    Zip,
    LegacyEncrypted,
}

/// <summary>A file stored inside an SSF container, held fully in memory.</summary>
public sealed record SkinPackageEntry(string Name, byte[] Content);

/// <summary>
/// A virtual, container-independent view of an SSF package.
/// Original entry names are preserved; lookup is case-insensitive (§8.2, §8.3.3).
/// </summary>
public sealed class SkinPackage
{
    private readonly Dictionary<string, SkinPackageEntry> _index;
    private readonly List<SkinPackageEntry> _entries;
    private readonly List<Diagnostic> _diagnostics;

    public SkinPackage(SsfContainerKind container, IEnumerable<SkinPackageEntry> entries, IEnumerable<Diagnostic>? diagnostics = null)
    {
        Container = container;
        _entries = [];
        _index = new Dictionary<string, SkinPackageEntry>(StringComparer.OrdinalIgnoreCase);
        _diagnostics = diagnostics is null ? [] : [.. diagnostics];

        foreach (var entry in entries)
        {
            if (_index.ContainsKey(entry.Name))
            {
                // First occurrence wins (§8.2 rule 5).
                _diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.SsfDuplicateEntry,
                    DiagnosticSeverity.Warning,
                    $"Duplicate entry '{entry.Name}' ignored; the first occurrence is used.",
                    Asset: entry.Name));
                continue;
            }

            _entries.Add(entry);
            _index[entry.Name] = entry;
        }
    }

    public SsfContainerKind Container { get; }

    public IReadOnlyList<SkinPackageEntry> Entries => _entries;

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public bool TryGetEntry(string name, out SkinPackageEntry entry)
    {
        if (_index.TryGetValue(name, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>Locates skin.ini case-insensitively, including entries nested one folder deep.</summary>
    public SkinPackageEntry? FindSkinIni()
    {
        if (TryGetEntry("skin.ini", out var direct))
        {
            return direct;
        }

        return _entries.FirstOrDefault(e =>
            e.Name.EndsWith("skin.ini", StringComparison.OrdinalIgnoreCase) &&
            (e.Name.Length == "skin.ini".Length ||
             e.Name[^("skin.ini".Length + 1)] is '/' or '\\'));
    }
}
