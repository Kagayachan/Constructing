// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Mapping;
using Ssf2Weasel.Infrastructure.Yaml;

namespace Ssf2Weasel.Infrastructure.Install;

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

        // Step 2: resolve the deployer before touching any file, so a missing Weasel
        // installation cannot leave the configuration modified but undeployed.
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

        Directory.CreateDirectory(rimeDirectory);
        var customYamlPath = Path.Combine(rimeDirectory, "weasel.custom.yaml");

        // Steps 3–5: read existing config and merge; conflicts throw exit code 8.
        string? existingYaml = null;
        if (File.Exists(customYamlPath))
        {
            existingYaml = File.ReadAllText(customYamlPath);
        }

        var merged = WeaselCustomMerger.Merge(existingYaml, theme, options.Force);
        WeaselYamlValidator.ValidateCustomYaml(merged.MergedYaml, ExitCode.InstallError);

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
                merged.MergedYaml,
                verifyTempFile: tempPath =>
                    WeaselYamlValidator.ValidateCustomYaml(File.ReadAllText(tempPath), ExitCode.InstallError));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Ssf2WeaselException(
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

        if (deployer.Deploy())
        {
            return new InstallResult(customYamlPath, backupPath, Deployed: true, RolledBack: false);
        }

        // Deployment failed: restore the previous configuration.
        var rollbackOk = TryRollback(customYamlPath, backupPath);
        if (rollbackOk)
        {
            // Best effort: redeploy the restored configuration (§15.3 step 13).
            deployer.Deploy();
            throw new Ssf2WeaselException(
                ExitCode.DeployFailedRolledBack,
                DiagnosticCodes.DeployFailed,
                "Weasel deployment failed; the previous configuration was restored." +
                (backupPath is null ? string.Empty : $" Backup: {backupPath}"));
        }

        throw new Ssf2WeaselException(
            ExitCode.DeployAndRollbackFailed,
            DiagnosticCodes.RollbackFailed,
            "Weasel deployment failed and the previous configuration could not be restored." +
            (backupPath is null ? string.Empty : $" Restore manually from: {backupPath}"));
    }

    private static bool TryRollback(string customYamlPath, string? backupPath)
    {
        try
        {
            if (backupPath is null)
            {
                // There was no previous file; rolling back means removing the new one.
                File.Delete(customYamlPath);
            }
            else
            {
                AtomicFileWriter.WriteBytesAtomic(customYamlPath, File.ReadAllBytes(backupPath));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
