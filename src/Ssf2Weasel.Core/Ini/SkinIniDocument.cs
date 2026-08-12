// SPDX-License-Identifier: GPL-3.0-or-later
namespace Ssf2Weasel.Core.Ini;

/// <summary>One INI section. Key lookup is case-insensitive; original casing is preserved (§9.2).</summary>
public sealed class IniSection
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyValuePair<string, string>> _entries = [];

    public IniSection(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>All entries in file order, with original key casing.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries;

    /// <summary>True when the key already existed (duplicate; last value wins per §9.2).</summary>
    public bool Set(string key, string value)
    {
        var duplicate = _values.ContainsKey(key);
        _values[key] = value;
        _entries.Add(new KeyValuePair<string, string>(key, value));
        return duplicate;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public bool Contains(string key) => _values.ContainsKey(key);

    /// <summary>Splits a comma-separated value and trims whitespace (§9.2).</summary>
    public IReadOnlyList<string> GetList(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split(',').Select(p => p.Trim()).ToArray();
    }

    public int? GetInt(string key)
    {
        var raw = Get(key);
        if (raw is null)
        {
            return null;
        }

        return int.TryParse(raw.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}

/// <summary>Parsed skin.ini with case-insensitive section lookup (§9.2).</summary>
public sealed class SkinIniDocument
{
    private readonly Dictionary<string, IniSection> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IniSection> _ordered = [];

    public IReadOnlyList<IniSection> Sections => _ordered;

    public IniSection? GetSection(string name) => _sections.TryGetValue(name, out var s) ? s : null;

    public IniSection GetOrAddSection(string name)
    {
        if (!_sections.TryGetValue(name, out var section))
        {
            section = new IniSection(name);
            _sections[name] = section;
            _ordered.Add(section);
        }

        return section;
    }
}
