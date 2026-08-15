// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Core.Diagnostics;

namespace Core.Ini;

/// <summary>Parses skin.ini bytes: encoding detection per §9.1, line rules per §9.2.</summary>
public static class SkinIniParser
{
    private static readonly string[] KnownSections =
    [
        "General", "Display", "Scheme_H1", "Scheme_H2", "Scheme_V1", "Scheme_V2", "StatusBar",
    ];

    static SkinIniParser()
    {
        // Code page 936 (GBK) is not built into .NET; register the provider once.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static SkinIniDocument Parse(ReadOnlySpan<byte> content, ICollection<Diagnostic> diagnostics)
    {
        var text = DecodeText(content, diagnostics);
        var document = new SkinIniDocument();
        IniSection? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                if (close > 1)
                {
                    var name = line[1..close].Trim();
                    current = document.GetOrAddSection(name);
                    if (!KnownSections.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.IniUnknownSection,
                            DiagnosticSeverity.Info,
                            $"Unknown section '[{name}]' preserved for reporting.",
                            SourceSection: name));
                    }

                    continue;
                }

                ReportGarbage(line, current, diagnostics);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                ReportGarbage(line, current, diagnostics);
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (current is null)
            {
                // Key before any section header: tolerate under an implicit section.
                current = document.GetOrAddSection(string.Empty);
            }

            if (current.Set(key, value))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.IniDuplicateKey,
                    DiagnosticSeverity.Warning,
                    $"Duplicate key '{key}' in section '[{current.Name}]'; the last value is used.",
                    SourceSection: current.Name,
                    SourceKey: key));
            }
        }

        return document;
    }

    private static void ReportGarbage(string line, IniSection? section, ICollection<Diagnostic> diagnostics)
    {
        var preview = line.Length > 40 ? line[..40] + "..." : line;
        diagnostics.Add(new Diagnostic(
            DiagnosticCodes.IniTrailingGarbage,
            DiagnosticSeverity.Warning,
            $"Unrecognized line ignored: '{preview}'.",
            SourceSection: section?.Name));
    }

    /// <summary>
    /// Encoding detection per §9.1: UTF-16LE BOM, UTF-16LE heuristic, then strict UTF-8.
    /// Older skins store skin.ini in the Chinese ANSI code page instead, so GBK is
    /// tried before giving up; that path reports INI_ENCODING_LEGACY_ANSI.
    /// </summary>
    internal static string DecodeText(ReadOnlySpan<byte> content, ICollection<Diagnostic> diagnostics)
    {
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content[2..]);
        }

        if (LooksLikeUtf16Le(content))
        {
            return Encoding.Unicode.GetString(content);
        }

        // The odd-zero heuristic underestimates CJK-heavy UTF-16LE (e.g. a long
        // Chinese value near the start), so before accepting UTF-8 try a strict
        // UTF-16LE decode and keep it only when it yields plausible INI text (M-02).
        if (content.Length >= 4 && content.Length % 2 == 0 && ContainsNulByte(content) &&
            TryDecodeUtf16LeStrict(content, out var utf16Text) && LooksLikeIni(utf16Text))
        {
            return utf16Text;
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = strictUtf8.GetString(content);
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
        catch (DecoderFallbackException utf8Failure)
        {
            if (TryDecodeGbk(content, out var gbkText))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.IniEncodingLegacyAnsi,
                    DiagnosticSeverity.Warning,
                    "skin.ini is encoded in the Chinese ANSI code page (GBK) instead of UTF-16LE or UTF-8; " +
                    "it was decoded as GBK.",
                    Fallback: "gbk"));
                return gbkText;
            }

            throw new ToolException(
                ExitCode.IniError,
                DiagnosticCodes.SsfIniEncodingUnsupported,
                "skin.ini is not UTF-16LE, UTF-8 or GBK.",
                hint: "The skin file may be corrupted or use an unsupported encoding.",
                inner: utf8Failure);
        }
    }

    private static bool TryDecodeGbk(ReadOnlySpan<byte> content, out string text)
    {
        try
        {
            var gbk = Encoding.GetEncoding(
                936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            text = gbk.GetString(content);
            return true;
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException or NotSupportedException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool ContainsNulByte(ReadOnlySpan<byte> content)
    {
        foreach (var b in content)
        {
            if (b == 0x00)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeUtf16LeStrict(ReadOnlySpan<byte> content, out string text)
    {
        try
        {
            var strict = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
            text = strict.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Cheap structural check that a decode produced real INI text: at least one
    /// section header or key/value line and no stray NUL characters (which would
    /// mean the bytes were decoded with the wrong width).
    /// </summary>
    private static bool LooksLikeIni(string text)
    {
        if (text.IndexOf('\0') >= 0)
        {
            return false;
        }

        return text.Contains('[') || text.Contains('=');
    }

    private static bool LooksLikeUtf16Le(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4)
        {
            return false;
        }

        var sample = content.Length > 512 ? content[..512] : content;
        int oddZero = 0, oddTotal = 0;
        for (var i = 1; i < sample.Length; i += 2)
        {
            oddTotal++;
            if (sample[i] == 0x00)
            {
                oddZero++;
            }
        }

        // Mostly-ASCII UTF-16LE text has NUL at nearly every odd offset.
        return oddTotal > 0 && oddZero >= oddTotal * 0.6;
    }
}
