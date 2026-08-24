// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Core;
using Core.Diagnostics;
using Core.Mapping;
using YamlDotNet.RepresentationModel;

namespace Infrastructure.Yaml;

/// <summary>
/// Semantic merge of the generated theme into an existing weasel.custom.yaml (§15.4):
/// every key not managed by this tool is preserved; managed style keys and the
/// generated color scheme are added or replaced.
/// </summary>
public static class WeaselCustomMerger
{
    public static string Merge(string? existingYaml, WeaselTheme theme, bool force)
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

        // Distinguish an absent 'patch' key from one present with the wrong node
        // type: the latter is a controlled YAML error, not an internal crash (M-04).
        YamlMappingNode patch;
        if (TryFindKey(root, "patch", out var existingPatch))
        {
            if (existingPatch is YamlMappingNode existingPatchMapping)
            {
                patch = existingPatchMapping;
            }
            else
            {
                throw new ToolException(
                    ExitCode.InstallError,
                    DiagnosticCodes.YamlInvalid,
                    "The existing weasel.custom.yaml has a 'patch' entry that is not a mapping.",
                    hint: "Fix or remove the 'patch' entry so it is a mapping, then retry.");
            }
        }
        else
        {
            patch = new YamlMappingNode();
            root.Add("patch", patch);
        }

        var schemePath = $"preset_color_schemes/{theme.ColorSchemeId}";
        CheckConflict(patch, theme.ColorSchemeId, schemePath, force);

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

        return text;
    }

    /// <summary>
    /// Throws unless an existing scheme with the same id is absent, or is tool-managed
    /// and --force was given (§12.2 rule 7).
    /// </summary>
    private static void CheckConflict(YamlMappingNode patch, string schemeId, string schemePath, bool force)
    {
        if (!TryFindExistingScheme(patch, schemeId, schemePath, out var existingNode))
        {
            return;
        }

        // A same-id node that is not a mapping (scalar, sequence or null) is foreign
        // structure we did not create; it must be protected regardless of --force (M-03).
        if (existingNode is not YamlMappingNode existing)
        {
            throw new ToolException(
                ExitCode.OutputConflict,
                DiagnosticCodes.ColorSchemeIdConflict,
                $"Color scheme '{schemeId}' already exists and was not created by ssf2weasel.",
                hint: "Rename the skin or remove the conflicting scheme manually.");
        }

        var isManaged = existing.Children.Any(c =>
            c.Key is YamlScalarNode { Value: WeaselPatchBuilder.ManagedMarkerKey } &&
            c.Value is YamlScalarNode { Value: "true" });

        if (!isManaged)
        {
            throw new ToolException(
                ExitCode.OutputConflict,
                DiagnosticCodes.ColorSchemeIdConflict,
                $"Color scheme '{schemeId}' already exists and was not created by ssf2weasel.",
                hint: "Rename the skin or remove the conflicting scheme manually.");
        }

        if (!force)
        {
            throw new ToolException(
                ExitCode.OutputConflict,
                DiagnosticCodes.ColorSchemeIdConflict,
                $"Color scheme '{schemeId}' already exists. Use --force to replace it.",
                hint: "The existing entry was created by ssf2weasel and can be replaced safely with --force.");
        }
    }

    /// <summary>
    /// Finds an existing scheme entry by id, whether written flat
    /// ("preset_color_schemes/&lt;id&gt;") or nested, and independent of its node
    /// type so a foreign scalar/sequence/null is still detected (M-03).
    /// </summary>
    private static bool TryFindExistingScheme(
        YamlMappingNode patch,
        string schemeId,
        string schemePath,
        out YamlNode? existing)
    {
        foreach (var (key, value) in patch.Children)
        {
            if (key is YamlScalarNode { Value: { } path } && path == schemePath)
            {
                existing = value;
                return true;
            }
        }

        if (WeaselYamlValidator.TryGetMapping(patch, "preset_color_schemes", out var nested))
        {
            foreach (var (key, value) in nested.Children)
            {
                if (key is YamlScalarNode { Value: { } id } && id == schemeId)
                {
                    existing = value;
                    return true;
                }
            }
        }

        existing = null;
        return false;
    }

    private static bool TryFindKey(YamlMappingNode parent, string key, out YamlNode value)
    {
        foreach (var (k, v) in parent.Children)
        {
            if (k is YamlScalarNode { Value: { } name } && name == key)
            {
                value = v;
                return true;
            }
        }

        value = null!;
        return false;
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
