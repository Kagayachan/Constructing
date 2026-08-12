// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core;
using Ssf2Weasel.Infrastructure.Install;
using Xunit;

namespace Ssf2Weasel.UnitTests;

public class AtomicWriteAndBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ssf2weasel-tests", Guid.NewGuid().ToString("N"));

    public AtomicWriteAndBackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Writes_new_file_atomically()
    {
        var target = Path.Combine(_dir, "a.yaml");
        AtomicFileWriter.WriteAtomic(target, "patch: {}\n");
        Assert.Equal("patch: {}\n", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Replaces_existing_file_atomically()
    {
        var target = Path.Combine(_dir, "a.yaml");
        File.WriteAllText(target, "old");
        AtomicFileWriter.WriteAtomic(target, "new");
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void Failed_verification_leaves_original_untouched()
    {
        // Simulates the §15.3 step-9 validation failing mid-install (IT-010).
        var target = Path.Combine(_dir, "a.yaml");
        File.WriteAllText(target, "original");

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFileWriter.WriteAtomic(target, "half-written", _ => throw new InvalidOperationException("verify failed")));

        Assert.Equal("original", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Backup_roundtrip_validates()
    {
        var source = Path.Combine(_dir, "weasel.custom.yaml");
        File.WriteAllText(source, "patch: {}\n");

        var backup = BackupManager.CreateBackup(source, _dir, "1.0.0", DateTimeOffset.UtcNow);
        Assert.True(File.Exists(backup));
        Assert.True(File.Exists(backup + ".meta.json"));

        var metadata = BackupManager.ValidateBackup(backup);
        Assert.Equal("ssf2weasel", metadata.Tool);
    }

    [Fact]
    public void Tampered_backup_is_rejected()
    {
        var source = Path.Combine(_dir, "weasel.custom.yaml");
        File.WriteAllText(source, "patch: {}\n");
        var backup = BackupManager.CreateBackup(source, _dir, "1.0.0", DateTimeOffset.UtcNow);

        File.AppendAllText(backup, "# tampered");
        var ex = Assert.Throws<Ssf2WeaselException>(() => BackupManager.ValidateBackup(backup));
        Assert.Equal("BACKUP_INVALID", ex.Code);
    }

    [Fact]
    public void Foreign_files_are_not_accepted_as_backups()
    {
        var file = Path.Combine(_dir, "random.yaml");
        File.WriteAllText(file, "patch: {}\n");
        var ex = Assert.Throws<Ssf2WeaselException>(() => BackupManager.ValidateBackup(file));
        Assert.Equal("BACKUP_INVALID", ex.Code);
    }

    [Fact]
    public void Backup_file_name_contains_timestamp_and_hash()
    {
        var source = Path.Combine(_dir, "weasel.custom.yaml");
        File.WriteAllText(source, "patch: {}\n");
        var backup = BackupManager.CreateBackup(
            source, _dir, "1.0.0", new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        Assert.Matches(@"weasel\.custom\.20260810T120000Z\.[0-9a-f]{8}\.yaml$", backup);
    }
}
