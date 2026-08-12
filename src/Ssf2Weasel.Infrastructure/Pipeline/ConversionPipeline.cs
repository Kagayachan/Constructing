// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Assets;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Ini;
using Ssf2Weasel.Core.Mapping;
using Ssf2Weasel.Core.Model;
using Ssf2Weasel.Core.Package;
using Ssf2Weasel.Core.Report;
using Ssf2Weasel.Infrastructure.Imaging;
using Ssf2Weasel.Infrastructure.Ssf;
using Ssf2Weasel.Infrastructure.Yaml;

namespace Ssf2Weasel.Infrastructure.Pipeline;

/// <summary>Everything known about a loaded skin before mapping.</summary>
public sealed record LoadedSkin(
    string FilePath,
    string FileName,
    long FileSize,
    string Sha256,
    SsfContainerKind Container,
    SkinPackage Package,
    SkinIniDocument Ini,
    NormalizedSkin Skin);

/// <summary>All artifacts of one conversion, ready to be written to disk.</summary>
public sealed record ConversionArtifacts(
    ConversionResult Result,
    ConversionReport Report,
    string YamlText,
    byte[] PreviewPng,
    string ColorSchemeId);

/// <summary>Orchestrates the conversion flow of §7: load, normalize, select, analyze, map, emit.</summary>
public static class ConversionPipeline
{
    public static LoadedSkin Load(string inputPath, CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = File.ReadAllBytes(inputPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new Ssf2WeaselException(
                ExitCode.InputUnreadable,
                "INPUT_NOT_FOUND",
                $"Input file does not exist: {Path.GetFileName(inputPath)}",
                inner: ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Ssf2WeaselException(
                ExitCode.InputUnreadable,
                "INPUT_UNREADABLE",
                $"Input file could not be read: {ex.Message}",
                inner: ex);
        }

        var container = SsfContainerDetector.Detect(content);
        var reader = SsfContainerDetector.CreateReader(container);
        var package = reader.Read(content, cancellationToken);

        var iniEntry = package.FindSkinIni()
            ?? throw new Ssf2WeaselException(
                ExitCode.IniError,
                DiagnosticCodes.SsfIniMissing,
                "skin.ini was not found in the package.",
                hint: "Verify that the input is a Sogou skin file.");

        var parseDiagnostics = new List<Diagnostic>();
        var ini = SkinIniParser.Parse(iniEntry.Content, parseDiagnostics);

        var fallbackName = Path.GetFileNameWithoutExtension(inputPath);
        var skin = NormalizedSkinBuilder.Build(package, ini, fallbackName, new GdiImageMetadataReader(), parseDiagnostics);

        return new LoadedSkin(
            FilePath: Path.GetFullPath(inputPath),
            FileName: Path.GetFileName(inputPath),
            FileSize: content.LongLength,
            Sha256: System.Convert.ToHexStringLower(SHA256.HashData(content)),
            Container: container,
            Package: package,
            Ini: ini,
            Skin: skin);
    }

    public static ConversionArtifacts Convert(
        LoadedSkin loaded,
        LayoutKind layout,
        string toolVersion,
        IReadOnlyList<string> plannedOutputs)
    {
        var diagnostics = new List<Diagnostic>();
        var sourceScheme = SchemeSelector.Select(layout, loaded.Skin.Schemes.Keys.ToArray(), diagnostics);
        var scheme = loaded.Skin.Schemes[sourceScheme];

        var analyzed = AnalyzeSchemeColors(loaded, scheme, diagnostics);
        var colorSchemeId = SkinIdGenerator.Generate(loaded.Skin.Metadata.Name, loaded.Sha256);

        var result = WeaselMapper.Map(
            loaded.Skin,
            new ConversionOptions(layout),
            sourceScheme,
            analyzed,
            colorSchemeId,
            new GdiFontChecker());

        var allDiagnostics = diagnostics
            .Concat(loaded.Skin.Diagnostics)
            .Concat(result.Diagnostics)
            .ToArray();

        var yamlText = WeaselYamlWriter.Write(result.Theme, toolVersion);
        WeaselYamlValidator.ValidateCustomYaml(yamlText, ExitCode.ConversionError);

        var previewPng = PreviewRenderer.RenderPng(result.Theme);

        var report = new ConversionReport(
            SchemaVersion: ConversionReport.CurrentSchemaVersion,
            ToolVersion: toolVersion,
            Source: BuildSource(loaded),
            Skin: BuildSkin(loaded),
            Selection: new ReportSelection(
                RequestedLayout: layout == LayoutKind.Horizontal ? "horizontal" : "vertical",
                SourceScheme: sourceScheme.ToString(),
                ColorSchemeId: colorSchemeId),
            Mappings: result.Mappings,
            Warnings: allDiagnostics,
            UnsupportedFeatures: result.UnsupportedFeatures,
            UnknownSections: loaded.Skin.UnknownSections,
            Assets: BuildAssets(loaded),
            Outputs: plannedOutputs);

        return new ConversionArtifacts(
            result with { Diagnostics = allDiagnostics },
            report,
            yamlText,
            previewPng,
            colorSchemeId);
    }

    public static ReportSource BuildSource(LoadedSkin loaded) => new(
        FileName: loaded.FileName,
        Size: loaded.FileSize,
        Sha256: loaded.Sha256,
        Container: loaded.Container == SsfContainerKind.Zip ? "zip" : "legacy_encrypted");

    public static ReportSkin BuildSkin(LoadedSkin loaded) => new(
        Name: loaded.Skin.Metadata.Name,
        Version: loaded.Skin.Metadata.Version,
        Author: loaded.Skin.Metadata.Author,
        Email: loaded.Skin.Metadata.Email,
        CreatedAt: loaded.Skin.Metadata.CreatedAt,
        Description: loaded.Skin.Metadata.Description);

    public static IReadOnlyList<ReportAsset> BuildAssets(LoadedSkin loaded) => loaded.Skin.Assets.Values
        .OrderBy(a => a.OriginalName, StringComparer.OrdinalIgnoreCase)
        .Select(a => new ReportAsset(a.OriginalName, a.MediaType, a.Width, a.Height, a.FrameCount, a.Sha256))
        .ToArray();

    private static AnalyzedColors? AnalyzeSchemeColors(LoadedSkin loaded, SkinScheme scheme, List<Diagnostic> diagnostics)
    {
        var assetName = scheme.PrimaryVisualAsset;
        if (assetName is null)
        {
            return null;
        }

        if (!loaded.Package.TryGetEntry(assetName, out var imageEntry))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.AssetMissing,
                DiagnosticSeverity.Warning,
                $"Background image '{assetName}' referenced by Scheme_{scheme.Kind} is missing from the package.",
                Asset: assetName));
            return null;
        }

        byte[]? maskContent = null;
        if (scheme.BackgroundMaskAsset is not null &&
            loaded.Package.TryGetEntry(scheme.BackgroundMaskAsset, out var maskEntry))
        {
            maskContent = maskEntry.Content;
        }

        var analyzed = new GdiImageColorAnalyzer().Analyze(imageEntry.Content, maskContent, scheme.TransparentColor);
        if (analyzed is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.AssetUndecodable,
                DiagnosticSeverity.Warning,
                $"Background image '{assetName}' could not be decoded; default colors are used.",
                Asset: assetName));
        }

        return analyzed;
    }
}
