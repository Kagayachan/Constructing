// SPDX-License-Identifier: GPL-3.0-or-later
namespace Core;

/// <summary>
/// Controlled failure carrying a stable diagnostic code, a documented exit code
/// and an optional user-facing hint (requirements §16.2).
/// </summary>
public sealed class ToolException : Exception
{
    public ToolException(ExitCode exitCode, string code, string message, string? hint = null, Exception? inner = null)
        : base(message, inner)
    {
        ExitCode = exitCode;
        Code = code;
        Hint = hint;
    }

    public ExitCode ExitCode { get; }

    public string Code { get; }

    public string? Hint { get; }
}
