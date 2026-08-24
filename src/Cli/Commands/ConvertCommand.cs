// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Core;
using Core.Diagnostics;
using Core.Mapping;
using Core.Model;
using Infrastructure.Install;
using Infrastructure.Pipeline;
using Infrastructure.Reporting;

namespace Cli.Commands;

/// <summary>The convert command (§14.2): parse, map, write outputs, optionally install.</summary>
public static class ConvertCommand
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
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

        var loaded = ConversionPipeline.Load(input);
        var colorSchemeId = SkinIdGenerator.Generate(loaded.Skin.Metadata.Name, loaded.Sha256);

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
                throw new ToolException(
                    ExitCode.OutputConflict,
                    DiagnosticCodes.OutputConflict,
                    $"Output file already exists: {conflict}",
                    hint: "Use --force to overwrite, or choose another --output directory.");
            }
        }

        var artifacts = ConversionPipeline.Convert(loaded, layout, CliApplication.ToolVersion, outputs);

        WriteOutputsTransactionally(
            outputDirectory,
            [
                (yamlPath, Utf8NoBom.GetBytes(artifacts.YamlText)),
                (reportPath, Utf8NoBom.GetBytes(ReportWriter.WriteAny(artifacts.Report))),
                (previewPath, artifacts.PreviewPng),
            ],
            force);

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

    /// <summary>
    /// Writes all conversion outputs as one transaction (code review M-05): every
    /// file is staged to a temp sibling first, then committed with create-new
    /// semantics (unless --force). If any commit fails, already-committed files are
    /// rolled back so a partial or mixed output set is never left behind.
    /// </summary>
    private static void WriteOutputsTransactionally(
        string outputDirectory,
        IReadOnlyList<(string Path, byte[] Content)> files,
        bool force)
    {
        Directory.CreateDirectory(outputDirectory);

        var staged = new List<(string Temp, string Target)>();
        var committed = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var (path, content) in files)
            {
                var temp = Path.Combine(outputDirectory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(temp, content);
                staged.Add((temp, path));
            }

            foreach (var (temp, target) in staged)
            {
                string? backup = null;
                if (File.Exists(target))
                {
                    if (!force)
                    {
                        throw new ToolException(
                            ExitCode.OutputConflict,
                            DiagnosticCodes.OutputConflict,
                            $"Output file already exists: {target}",
                            hint: "Use --force to overwrite, or choose another --output directory.");
                    }

                    backup = Path.Combine(outputDirectory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.bak");
                    File.Move(target, backup);
                }

                File.Move(temp, target);
                committed.Add((target, backup));
            }

            // Success: discard the backups of any overwritten files.
            foreach (var (_, backup) in committed)
            {
                TryDelete(backup);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ToolException)
        {
            // Roll back: restore overwritten files and remove anything we committed.
            foreach (var (target, backup) in committed)
            {
                TryDelete(target);
                if (backup is not null && File.Exists(backup))
                {
                    try
                    {
                        File.Move(backup, target);
                    }
                    catch (Exception restoreEx) when (restoreEx is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            foreach (var (temp, _) in staged)
            {
                TryDelete(temp);
            }

            if (ex is ToolException)
            {
                throw;
            }

            throw new ToolException(
                ExitCode.ConversionError,
                DiagnosticCodes.OutputConflict,
                $"Failed to write conversion outputs: {ex.Message}",
                inner: ex);
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
