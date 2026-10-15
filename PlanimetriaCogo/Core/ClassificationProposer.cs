using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Construye las filas iniciales de la ventana de clasificación y aplica cambios de destino
/// re-proponiendo tipo, lados, capa y color.
/// </summary>
public static class ClassificationProposer
{
    /// <summary>Una fila por descripción original (sensible a mayúsculas), ordenadas por cantidad.</summary>
    public static List<CodeRule> BuildRules(IEnumerable<SurveyPoint> points, CodeCatalog catalog, Settings settings)
    {
        var rules = new List<CodeRule>();
        foreach (var group in points
                     .GroupBy(p => p.RawDescription ?? "", StringComparer.Ordinal)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var rule = new CodeRule { Original = group.Key, Count = group.Count() };
            var norm = DescriptionNormalizer.Normalize(group.Key);

            if (DescriptionNormalizer.IsEmptyOrNumeric(norm))
            {
                // Sin código o punto de control numérico: se omite y se reporta.
                rule.Kind = CodeKind.Ignore;
                rule.DestinationCode = "";
            }
            else
            {
                // En la carga inicial solo se resuelve por catálogo (código o alias);
                // la similitud se aplica al pulsar "Sugerir similares".
                ApplyDestination(rule, catalog.Resolve(norm) ?? "", catalog, settings);
            }

            rule.LayerBase = LayerNames.ProposeBase(rule, settings);
            rules.Add(rule);
        }
        return rules;
    }

    /// <summary>
    /// Asigna un código destino (se normaliza) y vuelve a proponer tipo, lados, color y capa.
    /// </summary>
    public static void ApplyDestination(CodeRule rule, string? destination, CodeCatalog catalog, Settings settings)
    {
        var code = DescriptionNormalizer.Normalize(destination);
        rule.DestinationCode = code;

        if (code.Length == 0)
        {
            rule.Kind = rule.Kind == CodeKind.Ignore ? CodeKind.Ignore : CodeKind.Line;
            rule.SplitBySide = true;
            rule.LayerBase = "";
            return;
        }

        var entry = catalog.Find(code);
        if (entry != null)
        {
            rule.Kind = entry.Kind;
            rule.SplitBySide = entry.SplitBySide;
            rule.ColorIndex = entry.ColorIndex;
        }
        else
        {
            rule.Kind = ProposeKind(code, catalog);
            rule.SplitBySide = true;
        }

        rule.LayerBase = LayerNames.ProposeBase(rule, settings);
    }

    /// <summary>Tipo por defecto: Punto para nombres de objeto conocidos, Línea para el resto.</summary>
    public static CodeKind ProposeKind(string normalizedCode, CodeCatalog catalog)
    {
        if (DescriptionNormalizer.IsEmptyOrNumeric(normalizedCode))
            return CodeKind.Ignore;
        return catalog.IsPointObject(normalizedCode) ? CodeKind.Point : CodeKind.Line;
    }

    /// <summary>Aplica las sugerencias del agrupador a las filas cuya descripción tenga propuesta.</summary>
    public static int ApplySuggestions(IEnumerable<CodeRule> rules, IEnumerable<SimilarityGroup> groups, CodeCatalog catalog, Settings settings)
    {
        var map = SimilarityGrouper.ToMap(groups);
        var changed = 0;
        foreach (var rule in rules)
        {
            if (rule.Kind == CodeKind.Ignore)
                continue;
            if (!map.TryGetValue(rule.Original, out var destination))
                continue;
            if (rule.DestinationCode == destination)
                continue;
            ApplyDestination(rule, destination, catalog, settings);
            changed++;
        }
        return changed;
    }
}
