// SPDX-License-Identifier: GPL-3.0-or-later
using Core.Diagnostics;
using Core.Model;

namespace Core.Mapping;

/// <summary>Implements the source scheme selection and fallback rules of §11.2.</summary>
public static class SchemeSelector
{
    public static SkinSchemeKind Select(
        LayoutKind requested,
        IReadOnlyCollection<SkinSchemeKind> available,
        ICollection<Diagnostic> diagnostics)
    {
        SkinSchemeKind[] preferred = requested == LayoutKind.Horizontal
            ? [SkinSchemeKind.H1, SkinSchemeKind.H2]
            : [SkinSchemeKind.V1, SkinSchemeKind.V2];
        SkinSchemeKind[] crossFallback = requested == LayoutKind.Horizontal
            ? [SkinSchemeKind.V1, SkinSchemeKind.V2]
            : [SkinSchemeKind.H1, SkinSchemeKind.H2];

        foreach (var kind in preferred)
        {
            if (available.Contains(kind))
            {
                if (kind != preferred[0])
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.SchemeFallback,
                        DiagnosticSeverity.Info,
                        $"Scheme {preferred[0]} is missing; falling back to {kind}.",
                        SourceSection: $"Scheme_{kind}"));
                }

                return kind;
            }
        }

        foreach (var kind in crossFallback)
        {
            if (available.Contains(kind))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.SchemeFallback,
                    DiagnosticSeverity.Warning,
                    $"No {(requested == LayoutKind.Horizontal ? "horizontal" : "vertical")} scheme is available; " +
                    $"falling back to {kind} from the other orientation.",
                    SourceSection: $"Scheme_{kind}"));
                return kind;
            }
        }

        throw new ToolException(
            ExitCode.ConversionError,
            DiagnosticCodes.SchemeMissing,
            "The skin does not define any of Scheme_H1, Scheme_H2, Scheme_V1 or Scheme_V2.",
            hint: "The skin.ini may be incomplete or not a Sogou input method skin.");
    }
}
