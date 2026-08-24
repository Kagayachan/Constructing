// SPDX-License-Identifier: GPL-3.0-or-later
using Core;
using Core.Diagnostics;
using Core.Mapping;
using Infrastructure.Yaml;

namespace Infrastructure.Install;

public sealed record InstallOptions(
    string? RimeDirectory,
    string? WeaselDirectory,
    bool Force,
    bool NoDeploy);

public sealed record InstallResult(
    string CustomYamlPath,
    string? BackupPath,
    bool Deployed,
    bool RolledBack);

/// <summary>
/// Installs a generated theme into the user's Rime directory following the
/// fourteen steps of §15.3: merge, backup, atomic write, deploy, verify or roll back.
/// Only %AppData%\Rime\weasel.custom.yaml is ever modified (§15.2).
/// </summary>
public sealed class WeaselInstaller
{
    private readonly string _toolVersion;
    private readonly Func<string, IWeaselDeployer> _deployerFactory;

    /// <summary>
    /// <paramref name="deployerFactory"/> receives the resolved Weasel installation
    /// directory; the default implementation runs WeaselDeployer.exe from it.
    /// </summary>
    public WeaselInstaller(string toolVersion, Func<string, IWeaselDeployer>? deployerFactory = null)
    {
        _toolVersion = toolVersion;
        _deployerFactory = deployerFactory
            ?? (directory => new ProcessWeaselDeployer(WeaselEnvironment.GetDeployerPath(directory)));
    }

    public InstallResult Install(WeaselTheme theme, InstallOptions options)
    {
        var rimeDirectory = WeaselEnvironment.GetRimeUserDirectory(options.RimeDirectory);

        // Serialize the whole transaction against other install/restore processes so
        // a concurrent operation cannot roll back over a committed update (M-01).
        using var _lock = RimeDirectoryLock.TryAcquire(rimeDirectory)
            ?? throw new ToolException(
                ExitCode.InstallError,
                DiagnosticCodes.InstallLocked,
                "Another ssf2weasel install or restore is in progress for this Rime directory.",
                hint: "Wait for the other operation to finish and try again.");

        // Step 2: resolve the deployer before touching any file, so a missing Weasel
        // installation cannot leave the configuration modified but undeployed.
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

        Directory.CreateDirectory(rimeDirectory);
        var customYamlPath = Path.Combine(rimeDirectory, "weasel.custom.yaml");

        // Steps 3–5: read existing config and merge; conflicts throw exit code 8.
        string? existingYaml = null;
        if (File.Exists(customYamlPath))
        {
            existingYaml = File.ReadAllText(customYamlPath);
        }

        var mergedYaml = WeaselCustomMerger.Merge(existingYaml, theme, options.Force);
        WeaselYamlValidator.ValidateCustomYaml(mergedYaml, ExitCode.InstallError);

        // Step 6: back up the current file before any write.
        string? backupPath = null;
        if (existingYaml is not null)
        {
            backupPath = BackupManager.CreateBackup(customYamlPath, rimeDirectory, _toolVersion, DateTimeOffset.UtcNow);
        }

        // Steps 7–10: temp file, re-validate from disk, atomic replace.
        try
        {
            AtomicFileWriter.WriteAtomic(
                customYamlPath,
                mergedYaml,
                verifyTempFile: tempPath =>
                    WeaselYamlValidator.ValidateCustomYaml(File.ReadAllText(tempPath), ExitCode.InstallError));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolException(
                ExitCode.InstallError,
                DiagnosticCodes.InstallFailed,
                $"Failed to write weasel.custom.yaml: {ex.Message}",
                inner: ex);
        }

        // Steps 11–13: deploy, and on failure restore the backup and redeploy it.
        if (deployer is null)
        {
            return new InstallResult(customYamlPath, backupPath, Deployed: false, RolledBack: false);
        }

        if (DeploymentSteps.SafeDeploy(deployer))
        {
            return new InstallResult(customYamlPath, backupPath, Deployed: true, RolledBack: false);
        }

        // Deployment failed: restore the previous configuration.
        if (DeploymentSteps.TryRollback(customYamlPath, backupPath))
        {
            // Best effort: redeploy the restored configuration (§15.3 step 13);
            // its result must not mask the successful rollback (H-03).
            DeploymentSteps.SafeDeploy(deployer);
            throw new ToolException(
                ExitCode.DeployFailedRolledBack,
                DiagnosticCodes.DeployFailed,
                "Weasel deployment failed; the previous configuration was restored." +
                (backupPath is null ? string.Empty : $" Backup: {backupPath}"));
        }

        throw new ToolException(
            ExitCode.DeployAndRollbackFailed,
            DiagnosticCodes.RollbackFailed,
            "Weasel deployment failed and the previous configuration could not be restored." +
            (backupPath is null ? string.Empty : $" Restore manually from: {backupPath}"));
    }
}
