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
        var parser = new DescriptionParser(settings);
        foreach (var group in points
                     .GroupBy(p => p.RawDescription ?? "", StringComparer.Ordinal)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var rule = new CodeRule { Original = group.Key, Count = group.Count() };
            // Se resuelve por el código base: "cuneta f" → CUNETA (la marca F la conserva el motor).
            var norm = parser.Parse(group.Key).BaseCode;

            if (DescriptionNormalizer.IsEmptyOrNumeric(norm))
            {
                // Sin código o punto de control numérico: se omite y se reporta.
                rule.Kind = CodeKind.Ignore;
                rule.DestinationCode = "";
            }
            else
            {
                // En la carga inicial solo se resuelve por catálogo (código o alias); la similitud se aplica al
                // pulsar "Sugerir similares". Con UseDescriptionAsCode, lo desconocido se propone a sí mismo.
                var known = catalog.Resolve(norm);
                if (known != null)
                    ApplyDestination(rule, known, catalog, settings);
                else
                    ApplyDestination(rule, settings.UseDescriptionAsCode ? CodeFromDescription(group.Key) : "", catalog, settings, exact: true);
            }

            rule.LayerBase = LayerNames.ProposeBase(rule, settings);
            rules.Add(rule);
        }
        return rules;
    }

    /// <summary>
    /// Filas iniciales para el modo por bloques (PLANIMETRIACSV): las mismas que <see cref="BuildRules"/>, con la
    /// unión propuesta por <see cref="ApplyBlockProposal"/>.
    /// </summary>
    public static List<CodeRule> BuildBlockRules(IEnumerable<SurveyPoint> points, CodeCatalog catalog, Settings settings)
    {
        var rules = BuildRules(points, catalog, settings);
        foreach (var rule in rules)
            ApplyBlockProposal(rule, settings);
        return rules;
    }

    /// <summary>
    /// Asigna un código destino y vuelve a proponer tipo, lados, color y capa. El código se normaliza
    /// (mayúsculas, sin tildes) salvo con <paramref name="exact"/>, que lo conserva tal cual ("Usar nombre como
    /// código"); el catálogo se consulta igual en forma normalizada.
    /// Con <paramref name="blockMode"/> se propone además la unión por bloques (<see cref="ApplyBlockProposal"/>).
    /// </summary>
    public static void ApplyDestination(CodeRule rule, string? destination, CodeCatalog catalog, Settings settings, bool blockMode = false, bool exact = false)
    {
        var code = exact ? (destination ?? "").Trim() : DescriptionNormalizer.Normalize(destination);
        rule.DestinationCode = code;

        if (code.Length == 0)
        {
            rule.Kind = rule.Kind == CodeKind.Ignore ? CodeKind.Ignore : CodeKind.Line;
            rule.SplitBySide = true;
            rule.BlockJoin = BlockJoin.Block;
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

        if (blockMode)
            ApplyBlockProposal(rule, settings);
        rule.LayerBase = LayerNames.ProposeBase(rule, settings);
    }

    /// <summary>
    /// Unión propuesta para el modo por bloques:
    /// <list type="bullet">
    ///   <item>Código en <see cref="Settings.ClosedBlockCodes"/> (CASA…): tipo Línea con bloque cerrado, aunque el catálogo lo tenga como Punto.</item>
    ///   <item>Código del eje (<see cref="Settings.AxisCode"/>): línea continua con todos sus puntos en orden de archivo.</item>
    ///   <item>Resto: una polilínea abierta por bloque.</item>
    /// </list>
    /// </summary>
    public static void ApplyBlockProposal(CodeRule rule, Settings settings)
    {
        if (!rule.IsResolved || rule.Kind == CodeKind.Ignore)
        {
            rule.BlockJoin = BlockJoin.Block;
            return;
        }

        var code = DescriptionNormalizer.Normalize(rule.DestinationCode);
        var closed = settings.ClosedBlockCodes.Select(DescriptionNormalizer.Normalize);
        if (closed.Contains(code, StringComparer.Ordinal))
        {
            rule.Kind = CodeKind.Line;
            rule.BlockJoin = BlockJoin.ClosedBlock;
        }
        else if (code == DescriptionNormalizer.Normalize(settings.AxisCode))
            rule.BlockJoin = BlockJoin.Continuous;
        else
            rule.BlockJoin = BlockJoin.Block;
        rule.LayerBase = LayerNames.ProposeBase(rule, settings);
    }

    /// <summary>
    /// Código destino "tal cual el nombre": la descripción exacta (mayúsculas, minúsculas, tildes y espacios
    /// interiores respetados; solo se recortan los extremos). Devuelve null para descripciones vacías o numéricas
    /// (quedan en Ignorar). Las marcas de la descripción siguen cortando al dibujar, pero forman parte del código.
    /// </summary>
    public static string? CodeFromDescription(string? original)
    {
        var text = (original ?? "").Trim();
        return DescriptionNormalizer.IsEmptyOrNumeric(DescriptionNormalizer.Normalize(text)) ? null : text;
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
