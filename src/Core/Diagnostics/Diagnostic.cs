// SPDX-License-Identifier: GPL-3.0-or-later
namespace Core.Diagnostics;

public enum DiagnosticSeverity
{
    Info,
    Warning,
}

/// <summary>
/// A structured diagnostic entry as defined by requirements §13.2.
/// Codes are a stable API (§18.4) and must not change with message wording.
/// </summary>
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string? SourceSection = null,
    string? SourceKey = null,
    string? Asset = null,
    string? Fallback = null);
