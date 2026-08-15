// SPDX-License-Identifier: GPL-3.0-or-later
using Core;

namespace Cli;

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
        var optionsEnded = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Explicit end-of-options marker: everything after "--" is positional,
            // which lets a literal option-shaped path be passed as a value.
            if (!optionsEnded && arg == "--")
            {
                optionsEnded = true;
                continue;
            }

            if (!optionsEnded && arg.StartsWith("--", StringComparison.Ordinal))
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

                    var next = args[i + 1];

                    // `--output -- --json` stores the literal directory name "--json".
                    if (next == "--")
                    {
                        if (i + 2 >= args.Length)
                        {
                            throw Usage($"Option '{arg}' requires a value.");
                        }

                        options._values[arg] = args[i + 2];
                        i += 2;
                        continue;
                    }

                    // A required value must not silently swallow the following option
                    // token (e.g. 'convert x --output --json'); reject it (M-06).
                    if (flagOptions.Contains(next) || valueOptions.Contains(next))
                    {
                        throw Usage(
                            $"Option '{arg}' requires a value but was followed by option '{next}'. " +
                            "Use '--' before a value that begins with '--'.");
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

    public static ToolException Usage(string message) => new(
        ExitCode.UsageError,
        "CLI_USAGE",
        message,
        hint: "Run 'ssf2weasel --help' for usage.");
}
