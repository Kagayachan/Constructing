// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;
using Ssf2Weasel.Infrastructure.Install;
using Ssf2Weasel.Infrastructure.Reporting;

namespace Ssf2Weasel.Cli.Commands;

/// <summary>The restore command (§14.5).</summary>
public static class RestoreCommand
{
    private static readonly string[] Flags = ["--no-deploy", "--json", "--verbose"];
    private static readonly string[] Valued = ["--rime-dir", "--weasel-dir"];

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, IWeaselDeployer>? deployerFactory)
    {
        var options = CliOptions.Parse(args, Flags, Valued);
        var backupPath = options.Positional ?? throw CliOptions.Usage("restore requires a backup file.");
        var json = options.HasFlag("--json");

        var service = new RestoreService(CliApplication.ToolVersion, deployerFactory);
        var result = service.Restore(backupPath, new RestoreOptions(
            RimeDirectory: options.GetValue("--rime-dir"),
            WeaselDirectory: options.GetValue("--weasel-dir"),
            NoDeploy: options.HasFlag("--no-deploy")));

        if (json)
        {
            stdout.WriteLine(ReportWriter.WriteAny(new
            {
                Ok = true,
                result.CustomYamlPath,
                result.SafetyBackupPath,
                result.Deployed,
            }));
        }
        else
        {
            stdout.WriteLine($"restored: {result.CustomYamlPath}");
            if (result.SafetyBackupPath is not null)
            {
                stdout.WriteLine($"previous config backed up to: {result.SafetyBackupPath}");
            }

            stdout.WriteLine($"deployed: {(result.Deployed ? "yes" : "skipped")}");
        }

        return (int)ExitCode.Success;
    }
}
