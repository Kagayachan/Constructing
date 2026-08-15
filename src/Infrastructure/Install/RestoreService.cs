// SPDX-License-Identifier: GPL-3.0-or-later
using Core;
using Core.Diagnostics;

namespace Infrastructure.Install;

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

        // Serialize against concurrent install/restore for the same directory (M-01).
        using var _lock = RimeDirectoryLock.TryAcquire(rimeDirectory)
            ?? throw new ToolException(
                ExitCode.InstallError,
                DiagnosticCodes.InstallLocked,
                "Another ssf2weasel install or restore is in progress for this Rime directory.",
                hint: "Wait for the other operation to finish and try again.");

        var customYamlPath = Path.Combine(rimeDirectory, "weasel.custom.yaml");

        // Resolve the deployer before writing, so a missing installation does not
        // leave the configuration restored but undeployed.
        IWeaselDeployer? deployer = null;
        if (!options.NoDeploy)
        {
            var weaselDirectory = WeaselEnvironment.FindWeaselDirectory(options.WeaselDirectory)
                ?? throw new ToolException(
                    ExitCode.InstallError,
                    DiagnosticCodes.WeaselNotFound,
                    "The Weasel installation directory could not be located.",
                    hint: "Use --weasel-dir to specify it, or --no-deploy to skip deployment.");
            deployer = _deployerFactory(weaselDirectory);
        }

        // Back up the current configuration before restoring over it (§14.5).
        var hadPreviousConfig = File.Exists(customYamlPath);
        string? safetyBackup = null;
        if (hadPreviousConfig)
        {
            safetyBackup = BackupManager.CreateBackup(customYamlPath, rimeDirectory, _toolVersion, DateTimeOffset.UtcNow);
        }

        AtomicFileWriter.WriteBytesAtomic(customYamlPath, File.ReadAllBytes(backupPath));

        if (deployer is null)
        {
            return new RestoreResult(customYamlPath, safetyBackup, Deployed: false);
        }

        if (SafeDeploy(deployer))
        {
            return new RestoreResult(customYamlPath, safetyBackup, Deployed: true);
        }

        // Deployment of the restored configuration failed. Roll the pre-restore
        // state back so exit code 10 truthfully means "previous state recovered"
        // (code review H-04); only then attempt a best-effort redeploy.
        if (TryRollback(customYamlPath, safetyBackup, hadPreviousConfig))
        {
            SafeDeploy(deployer);
            throw new ToolException(
                ExitCode.DeployFailedRolledBack,
                DiagnosticCodes.DeployFailed,
                "Weasel deployment of the restored configuration failed; the previous configuration was recovered." +
                (safetyBackup is null ? string.Empty : $" Backup: {safetyBackup}"));
        }

        throw new ToolException(
            ExitCode.DeployAndRollbackFailed,
            DiagnosticCodes.RollbackFailed,
            "Weasel deployment failed and the previous configuration could not be recovered." +
            (safetyBackup is null ? string.Empty : $" Restore manually from: {safetyBackup}"));
    }

    private static bool TryRollback(string customYamlPath, string? safetyBackup, bool hadPreviousConfig)
    {
        try
        {
            if (hadPreviousConfig && safetyBackup is not null)
            {
                AtomicFileWriter.WriteBytesAtomic(customYamlPath, File.ReadAllBytes(safetyBackup));
            }
            else
            {
                // There was no configuration before the restore; remove the file we wrote.
                if (File.Exists(customYamlPath))
                {
                    File.Delete(customYamlPath);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SafeDeploy(IWeaselDeployer deployer)
    {
        try
        {
            return deployer.Deploy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A deployer that throws is treated as a failed deployment (H-03).
            return false;
        }
    }
}
