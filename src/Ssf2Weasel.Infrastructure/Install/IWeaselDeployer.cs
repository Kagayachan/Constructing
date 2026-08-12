// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;

namespace Ssf2Weasel.Infrastructure.Install;

/// <summary>Triggers a Weasel redeployment; abstracted so tests can inject failures (IT-009).</summary>
public interface IWeaselDeployer
{
    /// <summary>Returns true when deployment succeeded.</summary>
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
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = _deployerPath,
            Arguments = "/deploy",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process is null)
        {
            return false;
        }

        if (!process.WaitForExit(120_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return false;
        }

        return process.ExitCode == 0;
    }
}
