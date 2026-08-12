// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;

namespace Ssf2Weasel.Infrastructure.Install;

/// <summary>Locates the Rime user directory and the Weasel installation (§14.2 defaults, §15.3 step 2).</summary>
public static class WeaselEnvironment
{
    public static string GetRimeUserDirectory(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Rime");
    }

    /// <summary>Finds the Weasel install directory: registry first, then default install locations.</summary>
    public static string? FindWeaselDirectory(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var full = Path.GetFullPath(overridePath);
            return Directory.Exists(full) ? full : null;
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Rime\Weasel");
                if (key?.GetValue("WeaselRoot") is string root && Directory.Exists(root))
                {
                    return root;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
            {
                // Registry unavailable; fall through to directory probing.
            }
        }

        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrEmpty(programFiles))
            {
                continue;
            }

            var rimeRoot = Path.Combine(programFiles, "Rime");
            if (!Directory.Exists(rimeRoot))
            {
                continue;
            }

            var candidate = Directory.EnumerateDirectories(rimeRoot, "weasel-*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "WeaselDeployer.exe")));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    public static string GetDeployerPath(string weaselDirectory)
    {
        var path = Path.Combine(weaselDirectory, "WeaselDeployer.exe");
        if (!File.Exists(path))
        {
            throw new Ssf2WeaselException(
                ExitCode.InstallError,
                DiagnosticCodes.WeaselNotFound,
                $"WeaselDeployer.exe was not found in '{weaselDirectory}'.",
                hint: "Use --weasel-dir to point at the Weasel installation directory.");
        }

        return path;
    }
}
