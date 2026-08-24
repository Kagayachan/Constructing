// SPDX-License-Identifier: GPL-3.0-or-later
using Core;
using Infrastructure.Install;
using Infrastructure.Yaml;
using Ssf2Weasel.TestSupport;
using Xunit;
using YamlDotNet.RepresentationModel;
using static Ssf2Weasel.IntegrationTests.CliTestHarness;

namespace Ssf2Weasel.IntegrationTests;

public class InstallTests
{
    private sealed class StubDeployer(bool succeeds) : IWeaselDeployer
    {
        public int Calls { get; private set; }

        public bool Deploy()
        {
            Calls++;
            return succeeds;
        }
    }

    private const string ExistingCustomYaml = """
        patch:
          "style/color_scheme": macau
          "translator/dictionary": my_dict
        """;

    private static string WriteSkin(TempDirectory dir)
    {
        var path = dir.File("synthetic.ssf");
        File.WriteAllBytes(path, SyntheticSsf.CreateZipSkin());
        return path;
    }

    [Fact]
    public void Install_merges_existing_config_and_keeps_unrelated_keys()
    {
        // IT-008
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");
        Directory.CreateDirectory(rimeDir);
        var customYaml = Path.Combine(rimeDir, "weasel.custom.yaml");
        File.WriteAllText(customYaml, ExistingCustomYaml);

        var deployer = new StubDeployer(succeeds: true);
        var result = Run(
            ["convert", input, "--output", dir.File("out"), "--install", "--rime-dir", rimeDir, "--weasel-dir", rimeDir],
            _ => deployer);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, deployer.Calls);

        var merged = File.ReadAllText(customYaml);
        var root = WeaselYamlValidator.ParseRoot(merged, ExitCode.InstallError);
        var patch = (YamlMappingNode)root.Children[new YamlScalarNode("patch")];

        Assert.Equal("my_dict", ((YamlScalarNode)patch.Children[new YamlScalarNode("translator/dictionary")]).Value);
        Assert.NotEqual("macau", ((YamlScalarNode)patch.Children[new YamlScalarNode("style/color_scheme")]).Value);

        // A backup of the original file must exist.
        var backups = Directory.GetFiles(BackupManager.GetBackupDirectory(rimeDir), "weasel.custom.*.yaml");
        Assert.Single(backups);
        Assert.Equal(ExistingCustomYaml, File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Deploy_failure_restores_previous_config_and_exits_10()
    {
        // IT-009
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");
        Directory.CreateDirectory(rimeDir);
        var customYaml = Path.Combine(rimeDir, "weasel.custom.yaml");
        File.WriteAllText(customYaml, ExistingCustomYaml);

        var deployer = new StubDeployer(succeeds: false);
        var result = Run(
            ["convert", input, "--output", dir.File("out"), "--install", "--rime-dir", rimeDir, "--weasel-dir", rimeDir],
            _ => deployer);

        Assert.Equal(10, result.ExitCode);
        Assert.Equal(ExistingCustomYaml, File.ReadAllText(customYaml));
    }

    [Fact]
    public void Deploy_failure_without_previous_config_removes_written_file()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");
        Directory.CreateDirectory(rimeDir);
        var customYaml = Path.Combine(rimeDir, "weasel.custom.yaml");

        var result = Run(
            ["convert", input, "--output", dir.File("out"), "--install", "--rime-dir", rimeDir, "--weasel-dir", rimeDir],
            _ => new StubDeployer(succeeds: false));

        Assert.Equal(10, result.ExitCode);
        Assert.False(File.Exists(customYaml));
    }

    [Fact]
    public void No_deploy_writes_config_without_invoking_deployer()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");

        var deployer = new StubDeployer(succeeds: true);
        var result = Run(
            ["convert", input, "--output", dir.File("out"), "--install", "--no-deploy", "--rime-dir", rimeDir],
            _ => deployer);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, deployer.Calls);
        WeaselYamlValidator.ValidateCustomYaml(
            File.ReadAllText(Path.Combine(rimeDir, "weasel.custom.yaml")), ExitCode.InstallError);
    }

    [Fact]
    public void Reinstalling_same_skin_requires_force()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");

        Assert.Equal(0, Run(
            ["convert", input, "--output", dir.File("a"), "--install", "--no-deploy", "--rime-dir", rimeDir]).ExitCode);

        var second = Run(
            ["convert", input, "--output", dir.File("b"), "--install", "--no-deploy", "--rime-dir", rimeDir]);
        Assert.Equal(8, second.ExitCode);

        var forced = Run(
            ["convert", input, "--output", dir.File("c"), "--install", "--no-deploy", "--force", "--rime-dir", rimeDir]);
        Assert.Equal(0, forced.ExitCode);
    }

    [Fact]
    public void Install_never_touches_weasel_yaml()
    {
        // §15.2: only weasel.custom.yaml may be modified.
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");
        Directory.CreateDirectory(rimeDir);
        var deployedYaml = Path.Combine(rimeDir, "weasel.yaml");
        File.WriteAllText(deployedYaml, "# generated by deployer\n");

        Assert.Equal(0, Run(
            ["convert", input, "--output", dir.File("out"), "--install", "--no-deploy", "--rime-dir", rimeDir]).ExitCode);

        Assert.Equal("# generated by deployer\n", File.ReadAllText(deployedYaml));
    }

    [Fact]
    public void Restore_recovers_a_backup_and_backs_up_current_state()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");
        Directory.CreateDirectory(rimeDir);
        var customYaml = Path.Combine(rimeDir, "weasel.custom.yaml");
        File.WriteAllText(customYaml, ExistingCustomYaml);

        Assert.Equal(0, Run(
            ["convert", input, "--output", dir.File("out"), "--install", "--no-deploy", "--rime-dir", rimeDir]).ExitCode);
        Assert.NotEqual(ExistingCustomYaml, File.ReadAllText(customYaml));

        var backup = Directory.GetFiles(BackupManager.GetBackupDirectory(rimeDir), "weasel.custom.*.yaml").Single();
        var restore = Run(["restore", backup, "--rime-dir", rimeDir, "--no-deploy"]);

        Assert.Equal(0, restore.ExitCode);
        Assert.Equal(ExistingCustomYaml, File.ReadAllText(customYaml));

        // The pre-restore state was itself backed up.
        Assert.Equal(2, Directory.GetFiles(BackupManager.GetBackupDirectory(rimeDir), "weasel.custom.*.yaml").Length);
    }

    [Fact]
    public void Missing_weasel_installation_fails_before_touching_the_config()
    {
        // §15.3 step 2: the Weasel directory is resolved before any write, so a
        // missing installation must not leave the config modified but undeployed.
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir);
        var rimeDir = dir.File("Rime");
        Directory.CreateDirectory(rimeDir);
        var customYaml = Path.Combine(rimeDir, "weasel.custom.yaml");
        File.WriteAllText(customYaml, ExistingCustomYaml);

        var result = Run(
            ["convert", input, "--output", dir.File("out"), "--install", "--rime-dir", rimeDir,
             "--weasel-dir", dir.File("no-such-weasel")]);

        Assert.Equal(9, result.ExitCode);
        Assert.Equal(ExistingCustomYaml, File.ReadAllText(customYaml));
        Assert.False(Directory.Exists(BackupManager.GetBackupDirectory(rimeDir)));
    }

    [Fact]
    public void Restore_rejects_files_that_are_not_tool_backups()
    {
        using var dir = CreateTempDirectory();
        var foreign = dir.File("foreign.yaml");
        File.WriteAllText(foreign, "patch: {}\n");
        Assert.Equal(3, Run(["restore", foreign, "--rime-dir", dir.File("Rime"), "--no-deploy"]).ExitCode);
    }
}
