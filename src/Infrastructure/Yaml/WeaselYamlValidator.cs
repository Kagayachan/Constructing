// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
using Core;
using Core.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace Infrastructure.Yaml;

/// <summary>Re-parses generated or merged YAML before it is written to disk (§13.1, §15.3 step 9).</summary>
public static partial class WeaselYamlValidator
{
    [GeneratedRegex("^0x[0-9a-f]{6}([0-9a-f]{2})?$")]
    private static partial Regex NormalizedColor();

    /// <summary>Parses YAML text and returns the root mapping, throwing a controlled error on failure.</summary>
    public static YamlMappingNode ParseRoot(string yamlText, ExitCode failureExitCode)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yamlText);
            stream.Load(reader);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new ToolException(
                failureExitCode,
                DiagnosticCodes.YamlInvalid,
                $"YAML could not be parsed: {ex.Message}",
                inner: ex);
        }

        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new ToolException(
                failureExitCode,
                DiagnosticCodes.YamlInvalid,
                "YAML must contain exactly one document with a mapping at its root.");
        }

        return root;
    }

    /// <summary>Validates the structure required of weasel.custom.yaml: a patch mapping with a color scheme.</summary>
    public static void ValidateCustomYaml(string yamlText, ExitCode failureExitCode)
    {
        var root = ParseRoot(yamlText, failureExitCode);
        if (!TryGetMapping(root, "patch", out var patch))
        {
            throw new ToolException(
                failureExitCode,
                DiagnosticCodes.YamlInvalid,
                "weasel.custom.yaml must contain a top-level 'patch' mapping.");
        }

        foreach (var (key, value) in patch.Children)
        {
            if (key is YamlScalarNode { Value: { } path } &&
                path.StartsWith("preset_color_schemes/", StringComparison.Ordinal) &&
                value is YamlMappingNode scheme)
            {
                ValidateColorScheme(path, scheme, failureExitCode);
            }
        }
    }

    private static void ValidateColorScheme(string path, YamlMappingNode scheme, ExitCode failureExitCode)
    {
        if (!scheme.Children.Keys.OfType<YamlScalarNode>().Any(k => k.Value == "name"))
        {
            throw new ToolException(
                failureExitCode,
                DiagnosticCodes.YamlInvalid,
                $"Color scheme '{path}' is missing the required 'name' field.");
        }

        foreach (var (key, value) in scheme.Children)
        {
            if (key is YamlScalarNode { Value: { } fieldName } &&
                fieldName.EndsWith("_color", StringComparison.Ordinal) &&
                value is YamlScalarNode { Value: { } colorValue } &&
                !NormalizedColor().IsMatch(colorValue))
            {
                throw new ToolException(
                    failureExitCode,
                    DiagnosticCodes.YamlInvalid,
                    $"Color '{fieldName}' in '{path}' has non-normalized value '{colorValue}'.");
            }
        }
    }

    internal static bool TryGetMapping(YamlMappingNode parent, string key, out YamlMappingNode mapping)
    {
        foreach (var (k, v) in parent.Children)
        {
            if (k is YamlScalarNode { Value: { } name } && name == key && v is YamlMappingNode m)
            {
                mapping = m;
                return true;
            }
        }

        mapping = null!;
        return false;
    }
}
