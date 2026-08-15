// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Diagnostics;

namespace Infrastructure.Install;

/// <summary>Triggers a Weasel redeployment; abstracted so tests can inject failures (IT-009).</summary>
public interface IWeaselDeployer
{
    /// <summary>
    /// Returns true when deployment succeeded. Implementations must not throw for
    /// expected operational failures (missing executable, launch/wait errors);
    /// such conditions are reported as a false result so the caller can roll back
    /// (code review H-03).
    /// </summary>
    bool Deploy();
}

/// <summary>Runs the installed WeaselDeployer.exe with the /deploy switch (§15.3 step 11).</summary>
public sealed class ProcessWeaselDeployer : IWeaselDeployer
{
    private readonly string _deployerPath;

    public ProcessWeaselDeployer(string deployerPath)
    {
        _deployerPath = deployerPath;
    }

    public bool Deploy()
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = _deployerPath,
                Arguments = "/deploy",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            // The deployer became inaccessible or non-executable after discovery.
            // Treat this as a deployment failure so the caller rolls back (H-03).
            return false;
        }

        if (process is null)
        {
            return false;
        }

        using (process)
        {
            try
            {
                if (!process.WaitForExit(120_000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                    {
                    }

                    return false;
                }

                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or SystemException)
            {
                return false;
            }
        }
    }
}
