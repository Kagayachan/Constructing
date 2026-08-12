// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Mapping;

namespace Ssf2Weasel.Core.Report;

/// <summary>Machine-readable conversion report (§13.2). Serialized with snake_case names.</summary>
public sealed record ConversionReport(
    string SchemaVersion,
    string ToolVersion,
    ReportSource Source,
    ReportSkin Skin,
    ReportSelection? Selection,
    IReadOnlyList<MappingRecord> Mappings,
    IReadOnlyList<Diagnostic> Warnings,
    IReadOnlyList<string> UnsupportedFeatures,
    IReadOnlyList<string> UnknownSections,
    IReadOnlyList<ReportAsset> Assets,
    IReadOnlyList<string> Outputs)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record ReportSource(string FileName, long Size, string Sha256, string Container);

public sealed record ReportSkin(string Name, string? Version, string? Author, string? Email, string? CreatedAt, string? Description);

public sealed record ReportSelection(string RequestedLayout, string SourceScheme, string ColorSchemeId);

public sealed record ReportAsset(string Name, string MediaType, int? Width, int? Height, int? FrameCount, string Sha256);
