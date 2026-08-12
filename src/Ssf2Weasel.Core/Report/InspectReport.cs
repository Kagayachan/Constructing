// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core.Diagnostics;

namespace Ssf2Weasel.Core.Report;

/// <summary>Read-only inspection result (§14.3).</summary>
public sealed record InspectReport(
    string SchemaVersion,
    string ToolVersion,
    ReportSource Source,
    ReportSkin Skin,
    int FileCount,
    IReadOnlyList<string> AvailableSchemes,
    IReadOnlyList<ReportAsset> Assets,
    IReadOnlyList<Diagnostic> Warnings,
    IReadOnlyList<string> UnknownSections,
    IReadOnlyList<string> ExpectedDegradations);
