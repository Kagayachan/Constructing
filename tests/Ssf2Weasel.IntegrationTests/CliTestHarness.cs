// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Cli;
using Ssf2Weasel.Infrastructure.Install;

namespace Ssf2Weasel.IntegrationTests;

public static class CliTestHarness
{
    public sealed record CliResult(int ExitCode, string StdOut, string StdErr);

    public static CliResult Run(string[] args, Func<string, IWeaselDeployer>? deployerFactory = null)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliApplication.Run(args, stdout, stderr, deployerFactory);
        return new CliResult(code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Creates a unique temp working directory that is deleted with the returned handle.</summary>
    public static TempDirectory CreateTempDirectory(string? name = null)
        => new(Path.Combine(Path.GetTempPath(), "ssf2weasel-it", name ?? Guid.NewGuid().ToString("N")));

    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public string File(string relative) => System.IO.Path.Combine(Path, relative);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Resolves a real sample path, or null when it is not available locally (§19.3).
    /// Setting SSF2WEASEL_REQUIRE_SAMPLES=1 turns a missing sample into a failure so
    /// release verification cannot pass by silently skipping.
    /// </summary>
    public static string? ResolveSample(string fileName)
    {
        var directory = FindSampleDirectory();
        var path = directory is null ? null : System.IO.Path.Combine(directory, fileName);
        if (path is not null && File.Exists(path))
        {
            return path;
        }

        if (Environment.GetEnvironmentVariable("SSF2WEASEL_REQUIRE_SAMPLES") == "1")
        {
            throw new InvalidOperationException(
                $"Sample '{fileName}' was not found and SSF2WEASEL_REQUIRE_SAMPLES=1 is set.");
        }

        return null;
    }

    /// <summary>Locates the local real-sample directory (§19.3): env var first, then repo root probing.</summary>
    public static string? FindSampleDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SSF2WEASEL_SAMPLES_DIR");
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.ssf").Any())
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
