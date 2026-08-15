// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;
using Core;
using Core.Diagnostics;

namespace Infrastructure.Install;

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
                // Only trust the registry directory when it actually contains the
                // deployer; a stale entry pointing at a removed install would
                // otherwise shadow valid fallbacks (code review M-10).
                if (key?.GetValue("WeaselRoot") is string root &&
                    Directory.Exists(root) &&
                    File.Exists(Path.Combine(root, "WeaselDeployer.exe")))
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
                .Where(d => File.Exists(Path.Combine(d, "WeaselDeployer.exe")))
                // Order by parsed version so weasel-0.17.4 wins over weasel-0.9.0
                // (a lexicographic sort would pick 0.9.0). Malformed names sort last
                // but deterministically by name.
                .OrderByDescending(ParseWeaselVersion)
                .ThenByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Extracts the version from a "weasel-x.y.z" directory name; unparsable names sort lowest.</summary>
    internal static Version ParseWeaselVersion(string directory)
    {
        var name = Path.GetFileName(directory);
        const string prefix = "weasel-";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(name[prefix.Length..], out var version))
        {
            return version;
        }

        return new Version(0, 0);
    }

    public static string GetDeployerPath(string weaselDirectory)
    {
        var path = Path.Combine(weaselDirectory, "WeaselDeployer.exe");
        if (!File.Exists(path))
        {
            throw new ToolException(
                ExitCode.InstallError,
                DiagnosticCodes.WeaselNotFound,
                $"WeaselDeployer.exe was not found in '{weaselDirectory}'.",
                hint: "Use --weasel-dir to point at the Weasel installation directory.");
        }

        return path;
    }
}
