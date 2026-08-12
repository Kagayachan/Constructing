// SPDX-License-Identifier: GPL-3.0-or-later
namespace Ssf2Weasel.Core;

/// <summary>Documented process exit codes (requirements §16.1).</summary>
public enum ExitCode
{
    Success = 0,
    UsageError = 2,
    InputUnreadable = 3,
    UnsupportedContainer = 4,
    PackageError = 5,
    IniError = 6,
    ConversionError = 7,
    OutputConflict = 8,
    InstallError = 9,
    DeployFailedRolledBack = 10,
    DeployAndRollbackFailed = 11,
    Cancelled = 12,
    InternalError = 70,
}
