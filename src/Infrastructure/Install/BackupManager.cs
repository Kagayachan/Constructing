// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Core;
using Core.Diagnostics;

namespace Infrastructure.Install;

/// <summary>
/// Creates and validates configuration backups (§15.3 step 6). The backup file is a
/// byte-identical copy of the original; tool metadata lives in a JSON sidecar so
/// the backup itself stays pristine for rollback.
/// </summary>
public static class BackupManager
{
    public sealed record BackupMetadata(
        string Tool,
        string ToolVersion,
        string OriginalPath,
        string CreatedUtc,
        string Sha256);

    public static string GetBackupDirectory(string rimeUserDirectory)
        => Path.Combine(rimeUserDirectory, "backups", "ssf2weasel");

    /// <summary>Creates a timestamped backup and its metadata sidecar; returns the backup path.</summary>
    public static string CreateBackup(string sourceFile, string rimeUserDirectory, string toolVersion, DateTimeOffset nowUtc)
    {
        var content = File.ReadAllBytes(sourceFile);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var directory = GetBackupDirectory(rimeUserDirectory);
        Directory.CreateDirectory(directory);

        var fileName = $"weasel.custom.{nowUtc:yyyyMMddTHHmmssZ}.{sha256[..8]}.yaml";
        var backupPath = Path.Combine(directory, fileName);
        File.WriteAllBytes(backupPath, content);

        var metadata = new BackupMetadata(
            Tool: "ssf2weasel",
            ToolVersion: toolVersion,
            OriginalPath: Path.GetFullPath(sourceFile),
            CreatedUtc: nowUtc.UtcDateTime.ToString("O"),
            Sha256: sha256);
        File.WriteAllText(backupPath + ".meta.json", JsonSerializer.Serialize(metadata, MetaOptions));

        return backupPath;
    }

    /// <summary>Validates that a file is a backup created by this tool with intact content (§14.5).</summary>
    public static BackupMetadata ValidateBackup(string backupPath)
    {
        var metaPath = backupPath + ".meta.json";
        if (!File.Exists(backupPath) || !File.Exists(metaPath))
        {
            throw new ToolException(
                ExitCode.InputUnreadable,
                DiagnosticCodes.BackupInvalid,
                "The backup file or its .meta.json sidecar does not exist.",
                hint: "Only backups created by ssf2weasel can be restored.");
        }

        BackupMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<BackupMetadata>(File.ReadAllText(metaPath), MetaOptions);
        }
        catch (JsonException ex)
        {
            throw new ToolException(
                ExitCode.InputUnreadable,
                DiagnosticCodes.BackupInvalid,
                "The backup metadata file is not valid JSON.",
                inner: ex);
        }

        if (metadata is null || metadata.Tool != "ssf2weasel")
        {
            throw new ToolException(
                ExitCode.InputUnreadable,
                DiagnosticCodes.BackupInvalid,
                "The backup metadata was not created by ssf2weasel.");
        }

        var actualSha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(backupPath)));
        if (!actualSha.Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolException(
                ExitCode.InputUnreadable,
                DiagnosticCodes.BackupInvalid,
                "The backup content does not match its recorded SHA-256; the file may have been modified.");
        }

        return metadata;
    }

    private static readonly JsonSerializerOptions MetaOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}
