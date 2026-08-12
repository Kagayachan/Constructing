// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Ssf2Weasel.Infrastructure.Install;

/// <summary>
/// Writes files via a temporary sibling plus atomic replace so an interrupted
/// process never leaves a half-written configuration (§15.3 steps 7–10, §17).
/// </summary>
public static class AtomicFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes text to a temp file in the target directory, optionally lets the
    /// caller verify the temp file, then atomically moves it over the target.
    /// </summary>
    public static void WriteAtomic(string targetPath, string content, Action<string>? verifyTempFile = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, content, Utf8NoBom);
            verifyTempFile?.Invoke(tempPath);

            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // A leftover temp file is harmless; it never shadows the real config.
                }
            }
        }
    }

    public static void WriteBytesAtomic(string targetPath, byte[] content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllBytes(tempPath, content);
            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
