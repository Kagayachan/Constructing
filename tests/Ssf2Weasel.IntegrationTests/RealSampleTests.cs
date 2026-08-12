// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Xunit;
using static Ssf2Weasel.IntegrationTests.CliTestHarness;

namespace Ssf2Weasel.IntegrationTests;

/// <summary>
/// Acceptance tests against the three verified baseline samples (§19.2 IT-001..IT-005).
/// Samples are never committed (§19.3); they are located via SSF2WEASEL_SAMPLES_DIR or by
/// probing parent directories. When a sample is absent the test returns without asserting,
/// unless SSF2WEASEL_REQUIRE_SAMPLES=1 forces a failure.
/// </summary>
public class RealSampleTests
{
    public static TheoryData<string, string, int, string> Baseline => new()
    {
        // file name, container, file count, expected sha256 prefix
        { "半透明伊蕾娜v1.0（by巧味棉花糖）.ssf", "zip", 51, "b480644b79fd" },
        { "痛哭流涕.ssf", "legacy_encrypted", 13, "9472bf2cb852" },
        { "辐光光搜狗输入法皮肤.ssf", "zip", 53, "238a78cb20db" },
    };

    [Theory]
    [MemberData(nameof(Baseline))]
    public void Inspect_reports_documented_container_and_file_count(
        string fileName, string container, int fileCount, string shaPrefix)
    {
        var path = ResolveSample(fileName);
        if (path is null)
        {
            return;
        }

        var result = Run(["inspect", path, "--json"]);
        Assert.Equal(0, result.ExitCode);

        var report = JsonDocument.Parse(result.StdOut).RootElement.GetProperty("report");
        Assert.Equal(container, report.GetProperty("source").GetProperty("container").GetString());
        Assert.Equal(fileCount, report.GetProperty("file_count").GetInt32());
        Assert.StartsWith(shaPrefix, report.GetProperty("source").GetProperty("sha256").GetString());
        Assert.NotEmpty(report.GetProperty("available_schemes").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Baseline))]
    public void Horizontal_conversion_produces_valid_outputs(
        string fileName, string container, int fileCount, string shaPrefix)
    {
        _ = (container, fileCount, shaPrefix);
        AssertConversionSucceeds(fileName, "horizontal");
    }

    [Theory]
    [MemberData(nameof(Baseline))]
    public void Vertical_conversion_produces_valid_outputs(
        string fileName, string container, int fileCount, string shaPrefix)
    {
        _ = (container, fileCount, shaPrefix);
        AssertConversionSucceeds(fileName, "vertical");
    }

    private static void AssertConversionSucceeds(string fileName, string layout)
    {
        var path = ResolveSample(fileName);
        if (path is null)
        {
            return;
        }

        using var dir = CreateTempDirectory();
        var output = dir.File($"out-{layout}");
        var result = Run(["convert", path, "--output", output, "--layout", layout, "--json"]);

        Assert.Equal(0, result.ExitCode);
        var json = JsonDocument.Parse(result.StdOut).RootElement;
        string[] expectedSchemes = layout == "horizontal" ? ["H1", "H2"] : ["V1", "V2"];
        Assert.Contains(json.GetProperty("source_scheme").GetString(), expectedSchemes);

        var yamlPath = Path.Combine(output, "weasel.custom.yaml");
        Assert.True(File.Exists(yamlPath));
        Assert.True(File.Exists(Path.Combine(output, "conversion-report.json")));
        Assert.True(new FileInfo(Path.Combine(output, "preview.png")).Length > 0);

        // The generated YAML must be independently validatable.
        Assert.Equal(0, Run(["validate", yamlPath]).ExitCode);
    }

    [Theory]
    [InlineData("win7风格.ssf")]
    [InlineData("维尼熊.ssf")]
    public void Additional_local_samples_convert_without_crashing(string fileName)
    {
        var path = ResolveSample(fileName);
        if (path is null)
        {
            return;
        }

        using var dir = CreateTempDirectory();
        var result = Run(["convert", path, "--output", dir.File("out"), "--json"]);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [MemberData(nameof(Baseline))]
    public void Conversion_completes_within_five_seconds(
        string fileName, string container, int fileCount, string shaPrefix)
    {
        _ = (container, fileCount, shaPrefix);

        var path = ResolveSample(fileName);
        if (path is null)
        {
            return;
        }

        using var dir = CreateTempDirectory();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(0, Run(["convert", path, "--output", dir.File("out")]).ExitCode);
        stopwatch.Stop();

        // NFR-003
        Assert.True(stopwatch.Elapsed.TotalSeconds < 5, $"conversion took {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    [Theory]
    [MemberData(nameof(Baseline))]
    public void Conversion_is_reproducible_for_real_samples(
        string fileName, string container, int fileCount, string shaPrefix)
    {
        _ = (container, fileCount, shaPrefix);

        var path = ResolveSample(fileName);
        if (path is null)
        {
            return;
        }

        using var dir = CreateTempDirectory();
        Assert.Equal(0, Run(["convert", path, "--output", dir.File("a")]).ExitCode);
        Assert.Equal(0, Run(["convert", path, "--output", dir.File("b")]).ExitCode);

        // NFR-004: identical input and environment yield identical YAML and preview.
        Assert.Equal(
            File.ReadAllText(Path.Combine(dir.File("a"), "weasel.custom.yaml")),
            File.ReadAllText(Path.Combine(dir.File("b"), "weasel.custom.yaml")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(dir.File("a"), "preview.png")),
            File.ReadAllBytes(Path.Combine(dir.File("b"), "preview.png")));
    }
}
