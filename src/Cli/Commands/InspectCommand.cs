// SPDX-License-Identifier: GPL-3.0-or-later
using Core;
using Core.Diagnostics;
using Core.Report;
using Infrastructure.Pipeline;
using Infrastructure.Reporting;

namespace Cli.Commands;

/// <summary>The read-only inspect command (§14.3).</summary>
public static class InspectCommand
{
    private static readonly string[] Flags = ["--json", "--verbose"];
    private static readonly string[] Valued = [];

    public static int Run(string[] args, TextWriter stdout)
    {
        var options = CliOptions.Parse(args, Flags, Valued);
        var input = options.Positional ?? throw CliOptions.Usage("inspect requires an input .ssf file.");
        var json = options.HasFlag("--json");

        var loaded = ConversionPipeline.Load(input);

        var degradations = new List<string>();
        foreach (var (kind, scheme) in loaded.Skin.Schemes.OrderBy(s => s.Key))
        {
            if (scheme.BackgroundMaskAsset is not null)
            {
                degradations.Add($"Scheme_{kind}: mask '{scheme.BackgroundMaskAsset}' will not be rendered");
            }

            foreach (var overlay in scheme.Overlays)
            {
                degradations.Add($"Scheme_{kind}: overlay '{overlay}' will not be rendered");
            }
        }

        if (loaded.Skin.HasStatusBar)
        {
            degradations.Add("StatusBar: not representable in Weasel");
        }

        foreach (var asset in loaded.Skin.Assets.Values.Where(a => a.FrameCount is > 1))
        {
            degradations.Add($"Animated asset '{asset.OriginalName}' ({asset.FrameCount} frames): only first frame used");
        }

        var report = new InspectReport(
            SchemaVersion: ConversionReport.CurrentSchemaVersion,
            ToolVersion: CliApplication.ToolVersion,
            Source: ConversionPipeline.BuildSource(loaded),
            Skin: ConversionPipeline.BuildSkin(loaded),
            FileCount: loaded.Package.Entries.Count,
            AvailableSchemes: loaded.Skin.Schemes.Keys.OrderBy(k => k).Select(k => k.ToString()).ToArray(),
            Assets: ConversionPipeline.BuildAssets(loaded),
            Warnings: loaded.Skin.Diagnostics,
            UnknownSections: loaded.Skin.UnknownSections,
            ExpectedDegradations: degradations);

        if (json)
        {
            stdout.WriteLine(ReportWriter.WriteAny(new { Ok = true, Report = report }));
        }
        else
        {
            stdout.WriteLine($"file:      {report.Source.FileName} ({report.Source.Size} bytes)");
            stdout.WriteLine($"container: {report.Source.Container}");
            stdout.WriteLine($"sha256:    {report.Source.Sha256}");
            stdout.WriteLine($"skin:      {report.Skin.Name} {report.Skin.Version} by {report.Skin.Author ?? "(unknown)"}");
            stdout.WriteLine($"files:     {report.FileCount}");
            stdout.WriteLine($"schemes:   {string.Join(", ", report.AvailableSchemes)}");
            stdout.WriteLine($"assets:    {report.Assets.Count(a => a.MediaType.StartsWith("image/"))} images");
            foreach (var asset in report.Assets.Where(a => a.MediaType.StartsWith("image/")))
            {
                var frames = asset.FrameCount is > 1 ? $", {asset.FrameCount} frames" : string.Empty;
                stdout.WriteLine($"  {asset.Name}  {asset.MediaType}  {asset.Width}x{asset.Height}{frames}");
            }

            if (report.UnknownSections.Count > 0)
            {
                stdout.WriteLine($"unknown sections: {string.Join(", ", report.UnknownSections)}");
            }

            if (report.ExpectedDegradations.Count > 0)
            {
                stdout.WriteLine("expected degradations:");
                foreach (var item in report.ExpectedDegradations)
                {
                    stdout.WriteLine($"  - {item}");
                }
            }

            var warnings = report.Warnings.Where(w => w.Severity != DiagnosticSeverity.Info).ToArray();
            if (warnings.Length > 0)
            {
                stdout.WriteLine("warnings:");
                foreach (var warning in warnings)
                {
                    stdout.WriteLine($"  {warning.Code}: {warning.Message}");
                }
            }
        }

        return (int)ExitCode.Success;
    }
}
