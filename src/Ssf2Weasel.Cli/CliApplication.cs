// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using Ssf2Weasel.Cli.Commands;
using Ssf2Weasel.Core;
using Ssf2Weasel.Infrastructure.Install;
using Ssf2Weasel.Infrastructure.Reporting;

namespace Ssf2Weasel.Cli;

/// <summary>
/// Command dispatch and the error output protocol of §16.2. In --json mode
/// stdout carries exactly one JSON document; all logs go to stderr.
/// </summary>
public static class CliApplication
{
    public static string ToolVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "1.0.0";

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, IWeaselDeployer>? deployerFactory = null)
    {
        var json = args.Contains("--json");
        var verbose = args.Contains("--verbose");

        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                stdout.WriteLine(HelpText);
                return (int)ExitCode.Success;
            }

            if (args[0] is "--version" or "-v")
            {
                stdout.WriteLine(ToolVersion);
                return (int)ExitCode.Success;
            }

            var commandArgs = args[1..];
            return args[0] switch
            {
                "convert" => ConvertCommand.Run(commandArgs, stdout, stderr, deployerFactory),
                "inspect" => InspectCommand.Run(commandArgs, stdout, stderr),
                "validate" => ValidateCommand.Run(commandArgs, stdout, stderr),
                "restore" => RestoreCommand.Run(commandArgs, stdout, stderr, deployerFactory),
                _ => throw CliOptions.Usage($"Unknown command '{args[0]}'."),
            };
        }
        catch (Ssf2WeaselException ex)
        {
            WriteError(ex.Code, ex.Message, ex.Hint, json, verbose, ex, stdout, stderr);
            return (int)ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            WriteError("CANCELLED", "The operation was cancelled by the user.", null, json, verbose, null, stdout, stderr);
            return (int)ExitCode.Cancelled;
        }
        catch (Exception ex)
        {
            WriteError("INTERNAL_ERROR", "An unhandled internal error occurred.", null, json, verbose, ex, stdout, stderr);
            return (int)ExitCode.InternalError;
        }
    }

    private static void WriteError(
        string code,
        string message,
        string? hint,
        bool json,
        bool verbose,
        Exception? exception,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (json)
        {
            stdout.WriteLine(ReportWriter.WriteAny(new
            {
                Ok = false,
                Error = new { Code = code, Message = message, Hint = hint },
            }));
        }
        else
        {
            stderr.WriteLine($"error {code}: {message}");
            if (hint is not null)
            {
                stderr.WriteLine($"hint: {hint}");
            }
        }

        // Full stack traces only with --verbose (§16.2), always on stderr.
        if (verbose && exception is not null)
        {
            stderr.WriteLine(exception.ToString());
        }
    }

    private const string HelpText = """
        ssf2weasel - convert Sogou .ssf skins to Weasel (Rime) configuration

        Usage:
          ssf2weasel convert <input.ssf> [--output <dir>] [--layout horizontal|vertical]
                                         [--install] [--force] [--no-deploy]
                                         [--rime-dir <dir>] [--weasel-dir <dir>]
                                         [--json] [--verbose]
          ssf2weasel inspect <input.ssf> [--json] [--verbose]
          ssf2weasel validate <path>     [--json] [--verbose]
          ssf2weasel restore <backup>    [--rime-dir <dir>] [--weasel-dir <dir>]
                                         [--no-deploy] [--json] [--verbose]
          ssf2weasel --version
          ssf2weasel --help

        Commands:
          convert   Convert a skin and write weasel.custom.yaml, a JSON report and a preview.
          inspect   Read-only analysis of a skin file.
          validate  Validate a .ssf file or a generated .yaml file.
          restore   Restore a configuration backup created by this tool.

        The tool never modifies your Rime configuration unless --install is given.
        """;
}
