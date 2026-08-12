// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Mapping;
using YamlDotNet.RepresentationModel;

namespace Ssf2Weasel.Infrastructure.Yaml;

/// <summary>
/// Semantic merge of the generated theme into an existing weasel.custom.yaml (§15.4):
/// every key not managed by this tool is preserved; managed style keys and the
/// generated color scheme are added or replaced.
/// </summary>
public static class WeaselCustomMerger
{
    public sealed record MergeResult(string MergedYaml, bool ReplacedExistingScheme);

    public static MergeResult Merge(string? existingYaml, WeaselTheme theme, bool force)
    {
        YamlMappingNode root;
        if (string.IsNullOrWhiteSpace(existingYaml))
        {
            root = new YamlMappingNode();
        }
        else
        {
            root = WeaselYamlValidator.ParseRoot(existingYaml, ExitCode.InstallError);
        }

        if (!WeaselYamlValidator.TryGetMapping(root, "patch", out var patch))
        {
            patch = new YamlMappingNode();
            root.Add("patch", patch);
        }

        var schemePath = $"preset_color_schemes/{theme.ColorSchemeId}";
        var replaced = CheckConflict(patch, theme.ColorSchemeId, schemePath, force);

        foreach (var (path, value) in WeaselPatchBuilder.Build(theme))
        {
            SetPatchValue(patch, path, ToNode(value));
        }

        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        }

        // YamlStream.Save appends a document-end marker line ("..."); strip it for a clean file.
        var text = sb.ToString().Replace("\r\n", "\n");
        if (text.EndsWith("...\n", StringComparison.Ordinal))
        {
            text = text[..^4];
        }

        return new MergeResult(text, replaced);
    }

    /// <summary>
    /// Returns true when an existing scheme with the same id will be replaced.
    /// Throws unless the existing entry is tool-managed and --force was given (§12.2 rule 7).
    /// </summary>
    private static bool CheckConflict(YamlMappingNode patch, string schemeId, string schemePath, bool force)
    {
        var existing = FindExistingScheme(patch, schemeId, schemePath);
        if (existing is null)
        {
            return false;
        }

        var isManaged = existing.Children.Any(c =>
            c.Key is YamlScalarNode { Value: WeaselPatchBuilder.ManagedMarkerKey } &&
            c.Value is YamlScalarNode { Value: "true" });

        if (!isManaged)
        {
            throw new Ssf2WeaselException(
                ExitCode.OutputConflict,
                DiagnosticCodes.ColorSchemeIdConflict,
                $"Color scheme '{schemeId}' already exists and was not created by ssf2weasel.",
                hint: "Rename the skin or remove the conflicting scheme manually.");
        }

        if (!force)
        {
            throw new Ssf2WeaselException(
                ExitCode.OutputConflict,
                DiagnosticCodes.ColorSchemeIdConflict,
                $"Color scheme '{schemeId}' already exists. Use --force to replace it.",
                hint: "The existing entry was created by ssf2weasel and can be replaced safely with --force.");
        }

        return true;
    }

    private static YamlMappingNode? FindExistingScheme(YamlMappingNode patch, string schemeId, string schemePath)
    {
        foreach (var (key, value) in patch.Children)
        {
            if (key is YamlScalarNode { Value: { } path } && path == schemePath && value is YamlMappingNode direct)
            {
                return direct;
            }
        }

        if (WeaselYamlValidator.TryGetMapping(patch, "preset_color_schemes", out var nested) &&
            WeaselYamlValidator.TryGetMapping(nested, schemeId, out var inner))
        {
            return inner;
        }

        return null;
    }

    /// <summary>
    /// Sets a managed patch path. When the user's patch already contains a nested
    /// mapping covering the path (e.g. a "style:" block), the value is set inside
    /// it so the user's structure is preserved; otherwise the flat slash key is used.
    /// </summary>
    internal static void SetPatchValue(YamlMappingNode patch, string path, YamlNode value)
    {
        var segments = path.Split('/');

        // Try to descend into existing nested mappings as far as they exist.
        YamlMappingNode current = patch;
        for (var consumed = 0; consumed < segments.Length - 1; consumed++)
        {
            if (!WeaselYamlValidator.TryGetMapping(current, segments[consumed], out var next))
            {
                // No deeper nesting: set the remaining path as one flat key here.
                var flatKey = string.Join('/', segments[consumed..]);
                ReplaceKey(current, flatKey, value);
                return;
            }

            current = next;
        }

        ReplaceKey(current, segments[^1], value);
    }

    private static void ReplaceKey(YamlMappingNode mapping, string key, YamlNode value)
    {
        var existingKey = mapping.Children.Keys
            .OfType<YamlScalarNode>()
            .FirstOrDefault(k => k.Value == key);
        if (existingKey is not null)
        {
            mapping.Children[existingKey] = value;
        }
        else
        {
            mapping.Add(key, value);
        }
    }

    private static YamlNode ToNode(YamlValue value) => value switch
    {
        YamlString s => new YamlScalarNode(s.Value) { Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted },
        YamlRaw r => new YamlScalarNode(r.Value) { Style = YamlDotNet.Core.ScalarStyle.Plain },
        YamlMap m => BuildMapping(m),
        _ => throw new InvalidOperationException($"Unknown YAML value type {value.GetType()}."),
    };

    private static YamlMappingNode BuildMapping(YamlMap map)
    {
        var node = new YamlMappingNode();
        foreach (var (key, value) in map.Entries)
        {
            node.Add(key, ToNode(value));
        }

        return node;
    }
}
