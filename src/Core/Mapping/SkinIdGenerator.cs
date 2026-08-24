// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;

namespace Core.Mapping;

/// <summary>Generates the Weasel color scheme id from the skin name (§12.2).</summary>
public static partial class SkinIdGenerator
{
    [GeneratedRegex("^[a-z][a-z0-9_]{2,63}$")]
    private static partial Regex ValidId();

    public static string Generate(string skinName, string sourceSha256)
    {
        var normalized = skinName.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var lastWasUnderscore = false;

        foreach (var ch in normalized)
        {
            char mapped;
            if (ch is >= 'A' and <= 'Z')
            {
                mapped = char.ToLowerInvariant(ch);
            }
            else if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                mapped = ch;
            }
            else if (ch == '_' || char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                mapped = '_';
            }
            else
            {
                // Non-ASCII letters (e.g. CJK) cannot appear in the id and are dropped.
                continue;
            }

            if (mapped == '_')
            {
                if (lastWasUnderscore || builder.Length == 0)
                {
                    continue;
                }

                lastWasUnderscore = true;
            }
            else
            {
                lastWasUnderscore = false;
            }

            builder.Append(mapped);
        }

        var candidate = builder.ToString().Trim('_');
        if (candidate.Length > 64)
        {
            candidate = candidate[..64].Trim('_');
        }

        if (ValidId().IsMatch(candidate))
        {
            return candidate;
        }

        return "ssf_" + sourceSha256[..12].ToLowerInvariant();
    }
}
