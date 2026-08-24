// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using Core;
using Core.Assets;
using Core.Diagnostics;
using Core.Ini;
using Core.Limits;
using Core.Mapping;
using Core.Model;
using Core.Package;
using Core.Report;
using Infrastructure.Imaging;
using Infrastructure.Ssf;
using Infrastructure.Yaml;

namespace Infrastructure.Pipeline;

/// <summary>Everything known about a loaded skin before mapping.</summary>
public sealed record LoadedSkin(
    string FileName,
    long FileSize,
    string Sha256,
    SsfContainerKind Container,
    SkinPackage Package,
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
    public static LoadedSkin Load(string inputPath, ResourceLimits? limits = null)
    {
        limits ??= ResourceLimits.Default;

        // Reject an oversized input up front, before reading it fully into memory.
        try
        {
            var info = new FileInfo(inputPath);
            if (info.Exists && info.Length > limits.MaxInputBytes)
            {
                throw new ToolException(
                    ExitCode.PackageError,
                    DiagnosticCodes.SsfResourceLimitExceeded,
                    $"Input file is {info.Length} bytes, exceeding the limit of {limits.MaxInputBytes} bytes.",
                    hint: "The file is unexpectedly large and was rejected to protect memory.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to ReadAllBytes, which reports a precise input error below.
        }

        byte[] content;
        try
        {
            content = File.ReadAllBytes(inputPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ToolException(
                ExitCode.InputUnreadable,
                "INPUT_NOT_FOUND",
                $"Input file does not exist: {Path.GetFileName(inputPath)}",
                inner: ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolException(
                ExitCode.InputUnreadable,
                "INPUT_UNREADABLE",
                $"Input file could not be read: {ex.Message}",
                inner: ex);
        }

        var container = SsfContainerDetector.Detect(content);
        var reader = SsfContainerDetector.CreateReader(container, limits);
        var package = reader.Read(content);

        var iniEntry = package.FindSkinIni()
            ?? throw new ToolException(
                ExitCode.IniError,
                DiagnosticCodes.SsfIniMissing,
                "skin.ini was not found in the package.",
                hint: "Verify that the input is a Sogou skin file.");

        var parseDiagnostics = new List<Diagnostic>();
        var ini = SkinIniParser.Parse(iniEntry.Content, parseDiagnostics);

        var fallbackName = Path.GetFileNameWithoutExtension(inputPath);
        var skin = NormalizedSkinBuilder.Build(package, ini, fallbackName, new GdiImageMetadataReader(), parseDiagnostics);

        return new LoadedSkin(
            FileName: Path.GetFileName(inputPath),
            FileSize: content.LongLength,
            Sha256: System.Convert.ToHexStringLower(SHA256.HashData(content)),
            Container: container,
            Package: package,
            Skin: skin);
    }

    public static ConversionArtifacts Convert(
        LoadedSkin loaded,
        LayoutKind layout,
        string toolVersion,
        IReadOnlyList<string> plannedOutputs,
        ResourceLimits? limits = null)
    {
        limits ??= ResourceLimits.Default;
        var diagnostics = new List<Diagnostic>();
        var sourceScheme = SchemeSelector.Select(layout, loaded.Skin.Schemes.Keys.ToArray(), diagnostics);
        var scheme = loaded.Skin.Schemes[sourceScheme];

        var analyzed = AnalyzeSchemeColors(loaded, scheme, diagnostics, limits);
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

    private static AnalyzedColors? AnalyzeSchemeColors(
        LoadedSkin loaded,
        SkinScheme scheme,
        List<Diagnostic> diagnostics,
        ResourceLimits limits)
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

        // Reject an oversized declared image before the analyzer decodes its pixels.
        if (loaded.Skin.Assets.TryGetValue(assetName, out var baseAsset) &&
            baseAsset is { Width: { } bw, Height: { } bh } &&
            (bw > limits.MaxImageDimension || bh > limits.MaxImageDimension ||
             (long)bw * bh > limits.MaxImagePixels))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.AssetTooLarge,
                DiagnosticSeverity.Warning,
                $"Background image '{assetName}' is {bw}x{bh}, exceeding the analysis size limit; default colors are used.",
                Asset: assetName));
            return null;
        }

        var maskContent = ResolveMask(loaded, scheme, baseAsset, diagnostics);

        var analyzed = new GdiImageColorAnalyzer(limits).Analyze(imageEntry.Content, maskContent, scheme.TransparentColor);
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

    /// <summary>
    /// Validates a referenced background mask before use (§13, code review M-09):
    /// missing, undecodable and dimension-mismatched masks are reported and the
    /// analysis falls back to the image's own alpha channel.
    /// </summary>
    private static byte[]? ResolveMask(
        LoadedSkin loaded,
        SkinScheme scheme,
        SkinAsset? baseAsset,
        List<Diagnostic> diagnostics)
    {
        var maskName = scheme.BackgroundMaskAsset;
        if (maskName is null)
        {
            return null;
        }

        if (!loaded.Package.TryGetEntry(maskName, out var maskEntry))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MaskInvalid,
                DiagnosticSeverity.Warning,
                $"Mask '{maskName}' referenced by Scheme_{scheme.Kind} is missing; the image alpha channel is used instead.",
                Asset: maskName));
            return null;
        }

        if (!loaded.Skin.Assets.TryGetValue(maskName, out var maskAsset) ||
            maskAsset.Width is null || maskAsset.Height is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MaskInvalid,
                DiagnosticSeverity.Warning,
                $"Mask '{maskName}' could not be decoded; the image alpha channel is used instead.",
                Asset: maskName));
            return null;
        }

        if (baseAsset is { Width: { } bw, Height: { } bh } &&
            (maskAsset.Width != bw || maskAsset.Height != bh))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MaskInvalid,
                DiagnosticSeverity.Warning,
                $"Mask '{maskName}' is {maskAsset.Width}x{maskAsset.Height} but the background is {bw}x{bh}; " +
                "the mask is ignored and the image alpha channel is used instead.",
                Asset: maskName));
            return null;
        }

        return maskEntry.Content;
    }
}
