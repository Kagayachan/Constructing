// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;

namespace Ssf2Weasel.Cli;

/// <summary>Minimal deterministic option parser: exact long options, one positional argument.</summary>
public sealed class CliOptions
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public string? Positional { get; private set; }

    public static CliOptions Parse(
        string[] args,
        IReadOnlyCollection<string> flagOptions,
        IReadOnlyCollection<string> valueOptions)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (flagOptions.Contains(arg))
                {
                    options._values[arg] = null;
                }
                else if (valueOptions.Contains(arg))
                {
                    if (i + 1 >= args.Length)
                    {
                        throw Usage($"Option '{arg}' requires a value.");
                    }

                    options._values[arg] = args[++i];
                }
                else
                {
                    throw Usage($"Unknown option '{arg}'.");
                }
            }
            else if (options.Positional is null)
            {
                options.Positional = arg;
            }
            else
            {
                throw Usage($"Unexpected argument '{arg}'.");
            }
        }

        return options;
    }

    public bool HasFlag(string name) => _values.ContainsKey(name);

    public string? GetValue(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public static Ssf2WeaselException Usage(string message) => new(
        ExitCode.UsageError,
        "CLI_USAGE",
        message,
        hint: "Run 'ssf2weasel --help' for usage.");
}
