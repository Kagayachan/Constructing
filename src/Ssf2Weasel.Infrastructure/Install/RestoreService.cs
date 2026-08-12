// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;

namespace Ssf2Weasel.Infrastructure.Install;

public sealed record RestoreOptions(string? RimeDirectory, string? WeaselDirectory, bool NoDeploy);

public sealed record RestoreResult(string CustomYamlPath, string? SafetyBackupPath, bool Deployed);

/// <summary>Restores a backup created by this tool (§14.5): validate, re-backup, atomic replace, redeploy.</summary>
public sealed class RestoreService
{
    private readonly string _toolVersion;
    private readonly Func<string, IWeaselDeployer> _deployerFactory;

    /// <summary><paramref name="deployerFactory"/> receives the resolved Weasel installation directory.</summary>
    public RestoreService(string toolVersion, Func<string, IWeaselDeployer>? deployerFactory = null)
    {
        _toolVersion = toolVersion;
        _deployerFactory = deployerFactory
            ?? (directory => new ProcessWeaselDeployer(WeaselEnvironment.GetDeployerPath(directory)));
    }

    public RestoreResult Restore(string backupPath, RestoreOptions options)
    {
        BackupManager.ValidateBackup(backupPath);

        var rimeDirectory = WeaselEnvironment.GetRimeUserDirectory(options.RimeDirectory);
        var customYamlPath = Path.Combine(rimeDirectory, "weasel.custom.yaml");

        // Resolve the deployer before writing, so a missing installation does not
        // leave the configuration restored but undeployed.
        IWeaselDeployer? deployer = null;
        if (!options.NoDeploy)
        {
            var weaselDirectory = WeaselEnvironment.FindWeaselDirectory(options.WeaselDirectory)
                ?? throw new Ssf2WeaselException(
                    ExitCode.InstallError,
                    DiagnosticCodes.WeaselNotFound,
                    "The Weasel installation directory could not be located.",
                    hint: "Use --weasel-dir to specify it, or --no-deploy to skip deployment.");
            deployer = _deployerFactory(weaselDirectory);
        }

        // Back up the current configuration before restoring over it (§14.5).
        string? safetyBackup = null;
        if (File.Exists(customYamlPath))
        {
            safetyBackup = BackupManager.CreateBackup(customYamlPath, rimeDirectory, _toolVersion, DateTimeOffset.UtcNow);
        }

        AtomicFileWriter.WriteBytesAtomic(customYamlPath, File.ReadAllBytes(backupPath));

        if (deployer is null)
        {
            return new RestoreResult(customYamlPath, safetyBackup, Deployed: false);
        }

        if (!deployer.Deploy())
        {
            throw new Ssf2WeaselException(
                ExitCode.DeployFailedRolledBack,
                DiagnosticCodes.DeployFailed,
                "The restored configuration was written, but Weasel deployment failed.");
        }

        return new RestoreResult(customYamlPath, safetyBackup, Deployed: true);
    }
}
