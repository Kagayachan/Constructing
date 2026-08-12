// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Model;
using Ssf2Weasel.Infrastructure.Pipeline;
using Ssf2Weasel.Infrastructure.Reporting;
using Ssf2Weasel.Infrastructure.Yaml;

namespace Ssf2Weasel.Cli.Commands;

/// <summary>The validate command (§14.4): checks a .ssf for convertibility or a .yaml for structure.</summary>
public static class ValidateCommand
{
    private static readonly string[] Flags = ["--json", "--verbose"];
    private static readonly string[] Valued = [];

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var options = CliOptions.Parse(args, Flags, Valued);
        var input = options.Positional ?? throw CliOptions.Usage("validate requires a .ssf or .yaml file.");
        var json = options.HasFlag("--json");

        string kind;
        if (input.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            kind = "yaml";
            string text;
            try
            {
                text = File.ReadAllText(input);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
            {
                throw new Ssf2WeaselException(
                    ExitCode.InputUnreadable,
                    "INPUT_UNREADABLE",
                    $"File could not be read: {ex.Message}",
                    inner: ex);
            }

            WeaselYamlValidator.ValidateCustomYaml(text, ExitCode.ConversionError);
        }
        else
        {
            kind = "ssf";
            // Full dry run: load, select and map, but write nothing (§14.4).
            var loaded = ConversionPipeline.Load(input, CancellationToken.None);
            ConversionPipeline.Convert(loaded, LayoutKind.Horizontal, CliApplication.ToolVersion, []);
        }

        if (json)
        {
            stdout.WriteLine(ReportWriter.WriteAny(new { Ok = true, Kind = kind, Path = Path.GetFullPath(input) }));
        }
        else
        {
            stdout.WriteLine($"valid {kind}: {input}");
        }

        return (int)ExitCode.Success;
    }
}
