// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Mapping;
using Ssf2Weasel.Core.Model;
using Ssf2Weasel.Infrastructure.Install;
using Ssf2Weasel.Infrastructure.Pipeline;
using Ssf2Weasel.Infrastructure.Reporting;

namespace Ssf2Weasel.Cli.Commands;

/// <summary>The convert command (§14.2): parse, map, write outputs, optionally install.</summary>
public static class ConvertCommand
{
    private static readonly string[] Flags = ["--install", "--force", "--no-deploy", "--json", "--verbose"];
    private static readonly string[] Valued = ["--output", "--layout", "--rime-dir", "--weasel-dir"];

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, IWeaselDeployer>? deployerFactory)
    {
        var options = CliOptions.Parse(args, Flags, Valued);
        var input = options.Positional ?? throw CliOptions.Usage("convert requires an input .ssf file.");
        var json = options.HasFlag("--json");
        var verbose = options.HasFlag("--verbose");
        var force = options.HasFlag("--force");

        var layout = options.GetValue("--layout") switch
        {
            null or "horizontal" => LayoutKind.Horizontal,
            "vertical" => LayoutKind.Vertical,
            var other => throw CliOptions.Usage($"Invalid --layout value '{other}'; use horizontal or vertical."),
        };

        var loaded = ConversionPipeline.Load(input, CancellationToken.None);
        var colorSchemeId = Core.Mapping.SkinIdGenerator.Generate(loaded.Skin.Metadata.Name, loaded.Sha256);

        var outputDirectory = options.GetValue("--output") ?? Path.Combine(Environment.CurrentDirectory, $"{colorSchemeId}-weasel");
        outputDirectory = Path.GetFullPath(outputDirectory);

        var yamlPath = Path.Combine(outputDirectory, "weasel.custom.yaml");
        var reportPath = Path.Combine(outputDirectory, "conversion-report.json");
        var previewPath = Path.Combine(outputDirectory, "preview.png");
        string[] outputs = [yamlPath, reportPath, previewPath];

        // Existing files are never overwritten without --force (§14.2 defaults, IT-007).
        if (!force)
        {
            var conflict = outputs.FirstOrDefault(File.Exists);
            if (conflict is not null)
            {
                throw new Ssf2WeaselException(
                    ExitCode.OutputConflict,
                    DiagnosticCodes.OutputConflict,
                    $"Output file already exists: {conflict}",
                    hint: "Use --force to overwrite, or choose another --output directory.");
            }
        }

        var artifacts = ConversionPipeline.Convert(loaded, layout, CliApplication.ToolVersion, outputs);

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(yamlPath, artifacts.YamlText, new UTF8Encoding(false));
        File.WriteAllText(reportPath, ReportWriter.Write(artifacts.Report), new UTF8Encoding(false));
        File.WriteAllBytes(previewPath, artifacts.PreviewPng);

        if (verbose)
        {
            foreach (var diagnostic in artifacts.Result.Diagnostics)
            {
                stderr.WriteLine($"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Code}: {diagnostic.Message}");
            }
        }

        InstallResult? installResult = null;
        if (options.HasFlag("--install"))
        {
            var installer = new WeaselInstaller(CliApplication.ToolVersion, deployerFactory);
            installResult = installer.Install(artifacts.Result.Theme, new InstallOptions(
                RimeDirectory: options.GetValue("--rime-dir"),
                WeaselDirectory: options.GetValue("--weasel-dir"),
                Force: force,
                NoDeploy: options.HasFlag("--no-deploy")));
        }

        var warningCount = artifacts.Result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        if (json)
        {
            stdout.WriteLine(ReportWriter.WriteAny(new
            {
                Ok = true,
                ColorSchemeId = artifacts.ColorSchemeId,
                SourceScheme = artifacts.Result.SourceScheme.ToString(),
                OutputDirectory = outputDirectory,
                Outputs = outputs,
                Warnings = warningCount,
                Install = installResult is null ? null : new
                {
                    installResult.CustomYamlPath,
                    installResult.BackupPath,
                    installResult.Deployed,
                    installResult.RolledBack,
                },
            }));
        }
        else
        {
            stdout.WriteLine($"converted '{loaded.FileName}'");
            stdout.WriteLine($"  scheme:   {artifacts.Result.SourceScheme} ({(layout == LayoutKind.Horizontal ? "horizontal" : "vertical")})");
            stdout.WriteLine($"  color id: {artifacts.ColorSchemeId}");
            stdout.WriteLine($"  output:   {outputDirectory}");
            stdout.WriteLine($"  warnings: {warningCount} (see conversion-report.json)");
            if (installResult is not null)
            {
                stdout.WriteLine($"  installed: {installResult.CustomYamlPath}");
                if (installResult.BackupPath is not null)
                {
                    stdout.WriteLine($"  backup:    {installResult.BackupPath}");
                }

                stdout.WriteLine($"  deployed:  {(installResult.Deployed ? "yes" : "skipped")}");
            }
        }

        return (int)ExitCode.Success;
    }
}
