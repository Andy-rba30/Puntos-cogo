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
    /// <summary>
    /// Una fila por descripción original (sensible a mayúsculas), ordenadas por cantidad. Las marcas de inicio de
    /// bloque no crean filas aparte: "casa B" se cuenta en la fila "casa" (ver <see cref="DescriptionParser.RowKey"/>).
    /// </summary>
    public static List<CodeRule> BuildRules(IEnumerable<SurveyPoint> points, CodeCatalog catalog, Settings settings)
    {
        var rules = new List<CodeRule>();
        var parser = new DescriptionParser(settings);
        var rows = points
            .Select(p => (Raw: p.RawDescription ?? "", Key: parser.RowKey(p.RawDescription)))
            .GroupBy(p => p.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var row in rows)
        {
            var rule = new CodeRule { Original = row.Key, Count = row.Count };
            // Se resuelve por el código base: "cuneta f" → CUNETA (la marca F la conserva el motor).
            var norm = parser.Parse(row.Key).BaseCode;

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
                    ApplyDestination(rule, settings.UseDescriptionAsCode ? CodeFromDescription(row.Key, settings) : "", catalog, settings, exact: true);
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
            ApplyBlockProposal(rule, settings, catalog);
        return rules;
    }

    /// <summary>
    /// Asigna un código destino y vuelve a proponer tipo, lados, color y capa. El código se normaliza
    /// (mayúsculas, sin tildes) salvo con <paramref name="exact"/>, que lo conserva tal cual ("Usar nombre como
    /// código"); el catálogo se consulta igual en forma normalizada y también por alias ("e" toma lo de EJE).
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

        var entry = catalog.Find(code) ?? catalog.Find(catalog.Resolve(code));
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
            ApplyBlockProposal(rule, settings, catalog);
        rule.LayerBase = LayerNames.ProposeBase(rule, settings);
    }

    /// <summary>
    /// Propuesta del modo por bloques. El único criterio es el archivo: cada bloque (filas consecutivas con el mismo
    /// código, en orden de archivo; una marca "B" abre uno nuevo) es un elemento separado, para todos los códigos.
    /// Por eso todo código resuelto se propone como Línea por bloque, aunque el catálogo lo tenga como Punto; los
    /// de <see cref="Settings.ClosedBlockCodes"/> (CASA…), como bloque cerrado, que solo se cierra si el bloque forma
    /// un polígono. "Continua" y "Punto" quedan para elegirlos a mano en la ventana.
    /// </summary>
    public static void ApplyBlockProposal(CodeRule rule, Settings settings, CodeCatalog? catalog = null)
    {
        if (!rule.IsResolved || rule.Kind == CodeKind.Ignore)
        {
            rule.BlockJoin = BlockJoin.Block;
            return;
        }

        var code = DescriptionNormalizer.Normalize(rule.DestinationCode);
        var canonical = catalog?.Resolve(code) ?? code;
        var closed = settings.ClosedBlockCodes.Select(DescriptionNormalizer.Normalize).Any(c => c == code || c == canonical);
        rule.Kind = CodeKind.Line;
        rule.BlockJoin = closed ? BlockJoin.ClosedBlock : BlockJoin.Block;
        rule.LayerBase = LayerNames.ProposeBase(rule, settings);
    }

    /// <summary>
    /// Código destino "tal cual el nombre": la descripción sin sus marcas (inicio, fin, lado, inicio de bloque) y con
    /// el resto exacto (mayúsculas, minúsculas, tildes y espacios interiores respetados; solo se recortan los
    /// extremos): "cunetq B" → "cunetq", "Muro  alto" → "Muro  alto". Las marcas no pueden ir en el código: "casa B"
    /// y "casa" deben ser el mismo código para formar el mismo bloque; las marcas siguen actuando al dibujar.
    /// Devuelve null para descripciones vacías o numéricas (quedan en Ignorar).
    /// </summary>
    public static string? CodeFromDescription(string? original, Settings? settings = null)
    {
        var text = new DescriptionParser(settings ?? new Settings()).StripMarks(original);
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
