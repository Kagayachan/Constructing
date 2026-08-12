// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO.Compression;
using System.Text.Json;
using Ssf2Weasel.TestSupport;
using Xunit;
using static Ssf2Weasel.IntegrationTests.CliTestHarness;

namespace Ssf2Weasel.IntegrationTests;

public class SyntheticCliTests
{
    private static string WriteSkin(TempDirectory dir, byte[] content, string name = "synthetic.ssf")
    {
        var path = dir.File(name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Convert_zip_skin_produces_three_output_files()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin());
        var output = dir.File("out");

        var result = Run(["convert", input, "--output", output]);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(output, "weasel.custom.yaml")));
        Assert.True(File.Exists(Path.Combine(output, "conversion-report.json")));
        Assert.True(File.Exists(Path.Combine(output, "preview.png")));
    }

    [Fact]
    public void Convert_legacy_skin_succeeds()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, SyntheticSsf.CreateLegacySkin());
        var result = Run(["convert", input, "--output", dir.File("out")]);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Convert_is_deterministic_for_identical_input()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin());

        Assert.Equal(0, Run(["convert", input, "--output", dir.File("a")]).ExitCode);
        Assert.Equal(0, Run(["convert", input, "--output", dir.File("b")]).ExitCode);

        Assert.Equal(
            File.ReadAllText(Path.Combine(dir.File("a"), "weasel.custom.yaml")),
            File.ReadAllText(Path.Combine(dir.File("b"), "weasel.custom.yaml")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(dir.File("a"), "preview.png")),
            File.ReadAllBytes(Path.Combine(dir.File("b"), "preview.png")));
    }

    [Fact]
    public void Existing_output_without_force_exits_8()
    {
        // IT-007
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin());
        var output = dir.File("out");

        Assert.Equal(0, Run(["convert", input, "--output", output]).ExitCode);
        var second = Run(["convert", input, "--output", output]);
        Assert.Equal(8, second.ExitCode);

        var third = Run(["convert", input, "--output", output, "--force"]);
        Assert.Equal(0, third.ExitCode);
    }

    [Fact]
    public void Unicode_input_and_output_paths_work()
    {
        // IT-006
        using var dir = CreateTempDirectory("样本（测试）目录");
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin(), "半透明·皮肤 v1.0（测试）.ssf");
        var output = dir.File("输出目录（横排）");

        var result = Run(["convert", input, "--output", output]);
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(output, "conversion-report.json")));

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "conversion-report.json")));
        Assert.Equal("半透明·皮肤 v1.0（测试）.ssf", report.RootElement.GetProperty("source").GetProperty("file_name").GetString());
    }

    [Fact]
    public void Zip_with_path_escape_entries_is_read_without_them()
    {
        // IT-012: hostile entry names must be rejected without directory escape.
        using var dir = CreateTempDirectory();
        byte[] hostileZip;
        using (var stream = new MemoryStream())
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in SyntheticSsf.DefaultFiles())
                {
                    using var s = archive.CreateEntry(name).Open();
                    s.Write(content);
                }

                using (var evil = archive.CreateEntry("../evil.png").Open())
                {
                    evil.Write([1, 2, 3]);
                }

                using (var evil2 = archive.CreateEntry(@"C:\Windows\evil.dll").Open())
                {
                    evil2.Write([1, 2, 3]);
                }
            }

            hostileZip = stream.ToArray();
        }

        var input = WriteSkin(dir, hostileZip);
        var result = Run(["inspect", input, "--json"]);

        Assert.Equal(0, result.ExitCode);
        var json = JsonDocument.Parse(result.StdOut);
        var warnings = json.RootElement.GetProperty("report").GetProperty("warnings").EnumerateArray().ToArray();
        Assert.Contains(warnings, w => w.GetProperty("code").GetString() == "SSF_UNSAFE_ENTRY_PATH");

        // Only the three safe files remain.
        Assert.Equal(3, json.RootElement.GetProperty("report").GetProperty("file_count").GetInt32());
    }

    [Fact]
    public void Truncated_legacy_skin_fails_controlled_with_exit_5()
    {
        // IT-011
        using var dir = CreateTempDirectory();
        var full = SyntheticSsf.CreateLegacySkin();
        var input = WriteSkin(dir, full[..(full.Length / 3)]);

        var result = Run(["convert", input, "--output", dir.File("out")]);
        Assert.Equal(5, result.ExitCode);
        Assert.Contains("error", result.StdErr);
    }

    [Fact]
    public void Json_mode_emits_single_document_on_success_and_failure()
    {
        // IT-013
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin());

        var ok = Run(["convert", input, "--output", dir.File("out"), "--json"]);
        Assert.Equal(0, ok.ExitCode);
        var okDoc = JsonDocument.Parse(ok.StdOut); // throws if not exactly one valid JSON document
        Assert.True(okDoc.RootElement.GetProperty("ok").GetBoolean());

        var notFound = Run(["convert", dir.File("missing.ssf"), "--json"]);
        Assert.Equal(3, notFound.ExitCode);
        var errDoc = JsonDocument.Parse(notFound.StdOut);
        Assert.False(errDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotNull(errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Unsupported_container_exits_4()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, "this is not a skin file at all"u8.ToArray());
        Assert.Equal(4, Run(["convert", input]).ExitCode);
    }

    [Fact]
    public void Zip_without_skin_ini_exits_6()
    {
        using var dir = CreateTempDirectory();
        var files = new Dictionary<string, byte[]> { ["readme.txt"] = [1] };
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin(files));
        Assert.Equal(6, Run(["convert", input]).ExitCode);
    }

    [Fact]
    public void Usage_errors_exit_2()
    {
        Assert.Equal(2, Run(["convert"]).ExitCode);
        Assert.Equal(2, Run(["convert", "x.ssf", "--layout", "diagonal"]).ExitCode);
        Assert.Equal(2, Run(["frobnicate"]).ExitCode);
        Assert.Equal(2, Run(["convert", "x.ssf", "--unknown-option"]).ExitCode);
    }

    [Fact]
    public void Version_and_help_exit_0()
    {
        Assert.Equal(0, Run(["--version"]).ExitCode);
        Assert.Equal(0, Run(["--help"]).ExitCode);
    }

    [Fact]
    public void Vertical_layout_selects_v1()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin());
        var result = Run(["convert", input, "--output", dir.File("out"), "--layout", "vertical", "--json"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("V1", JsonDocument.Parse(result.StdOut).RootElement.GetProperty("source_scheme").GetString());
    }

    [Fact]
    public void Validate_accepts_generated_yaml_and_rejects_broken_yaml()
    {
        using var dir = CreateTempDirectory();
        var input = WriteSkin(dir, SyntheticSsf.CreateZipSkin());
        var output = dir.File("out");
        Assert.Equal(0, Run(["convert", input, "--output", output]).ExitCode);

        var yamlPath = Path.Combine(output, "weasel.custom.yaml");
        Assert.Equal(0, Run(["validate", yamlPath]).ExitCode);

        var broken = dir.File("broken.yaml");
        File.WriteAllText(broken, "patch: [unclosed");
        Assert.Equal(7, Run(["validate", broken]).ExitCode);
    }
}
