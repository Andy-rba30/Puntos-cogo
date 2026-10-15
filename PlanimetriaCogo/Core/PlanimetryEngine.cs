using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Orquesta el cálculo completo sin tocar AutoCAD:
/// clasificar por reglas → descomponer descripción (marcas, serie) → proyectar con continuidad → lado
/// (el explícito prevalece) → agrupar (código, lado[, serie]) → ordenar → cortar → secuencias.
/// También prepara los objetos puntuales y los cambios de descripción para las opciones del dibujo.
/// </summary>
public sealed class PlanimetryEngine
{
    private readonly Settings _settings;
    private readonly DescriptionParser _parser;

    public PlanimetryEngine(Settings settings)
    {
        _settings = settings;
        _parser = new DescriptionParser(settings);
    }

    public PlanimetryResult Run(IEnumerable<SurveyPoint> points, IEnumerable<CodeRule> rules, IAxis axis)
    {
        var result = new PlanimetryResult();
        var ruleByOriginal = new Dictionary<string, CodeRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
            ruleByOriginal[rule.Original] = rule;

        // 1. Clasificación por regla: líneas por código destino; puntos y descartes solo se registran.
        var lineCandidates = new Dictionary<string, List<(SurveyPoint Point, ParsedDescription Parsed)>>(StringComparer.Ordinal);
        var ruleByCode = new Dictionary<string, CodeRule>(StringComparer.Ordinal);

        foreach (var point in points)
        {
            result.TotalPoints++;
            var raw = point.RawDescription ?? "";
            ruleByOriginal.TryGetValue(raw, out var rule);

            if (rule != null && (rule.Kind == CodeKind.Ignore || !rule.Draw))
            {
                Increment(result.IgnoredCodes, raw);
                continue;
            }
            if (rule == null || !rule.IsResolved)
            {
                Increment(result.UnresolvedCodes, raw);
                continue;
            }

            var parsed = _parser.Parse(raw);
            var newDescription = DescriptionParser.Rebuild(parsed, rule.DestinationCode);
            if (!string.Equals(newDescription, raw, StringComparison.Ordinal))
                result.DescriptionChanges.Add(new DescriptionChange(point, newDescription));

            if (rule.Kind == CodeKind.Point)
            {
                Increment(result.PointObjectCodes, rule.DestinationCode);
                var pointLayer = string.IsNullOrWhiteSpace(rule.LayerBase) ? LayerNames.ProposeBase(rule, _settings) : LayerNames.Sanitize(rule.LayerBase);
                result.PointObjects.Add(new PointObject(point, rule.DestinationCode, pointLayer, rule.ColorIndex));
                continue;
            }

            if (!lineCandidates.TryGetValue(rule.DestinationCode, out var list))
            {
                list = new List<(SurveyPoint, ParsedDescription)>();
                lineCandidates[rule.DestinationCode] = list;
            }
            list.Add((point, parsed));

            // Si varias descripciones van al mismo código con capa/color distintos, manda la de más puntos.
            if (!ruleByCode.TryGetValue(rule.DestinationCode, out var existing) || rule.Count > existing.Count)
                ruleByCode[rule.DestinationCode] = rule;
        }

        // 2. Proyección con continuidad (por código, en orden de número) y lado.
        var resolver = new ContinuityResolver(axis, _settings.OffsetMax);
        var resolved = new List<ResolvedPoint>();

        foreach (var (code, list) in lineCandidates.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var rule = ruleByCode[code];
            var ordered = list.OrderBy(p => p.Point.Number).ThenBy(p => p.Point.SourceId, StringComparer.Ordinal).ToList();
            var resolutions = resolver.Resolve(ordered.Select(p => p.Point).ToList());

            for (var i = 0; i < ordered.Count; i++)
            {
                var (point, parsed) = ordered[i];
                var resolution = resolutions[i];

                if (!resolution.Ok)
                {
                    result.Discarded.Add(resolution.HadCandidates
                        ? new DiscardedPoint(point, code, DiscardReason.BeyondOffsetMax, $"|offset| > {_settings.OffsetMax:0.00} m")
                        : new DiscardedPoint(point, code, DiscardReason.NoProjection, "no proyecta sobre el eje"));
                    continue;
                }

                var projection = resolution.Chosen!.Value;
                var side = Side.Center;

                if (rule.SplitBySide)
                {
                    var computed = SideClassifier.Classify(projection.Offset, _settings.AxisTolerance);
                    var explicitSide = _settings.UseDescriptionMarks ? parsed.ExplicitSide : null;

                    if (explicitSide.HasValue)
                    {
                        // El lado escrito en campo prevalece (§3.2); se reporta si no coincide con el calculado.
                        if (computed != Side.Center && computed != explicitSide.Value)
                            result.SideDiscrepancies.Add(new SideDiscrepancy(point, code, explicitSide.Value, computed, projection.Offset));
                        side = explicitSide.Value;
                    }
                    else if (computed == Side.Center)
                    {
                        result.Discarded.Add(new DiscardedPoint(point, code, DiscardReason.OnAxis, $"|offset| {projection.Distance:0.000} m < tolEje {_settings.AxisTolerance:0.000} m"));
                        continue;
                    }
                    else
                    {
                        side = computed;
                    }
                }

                resolved.Add(new ResolvedPoint(point, code, side, projection, parsed));
            }
        }

        // 3. Agrupar, ordenar, cortar y armar secuencias dibujables.
        foreach (var group in LineGrouper.Group(resolved, _settings.Order, _settings.SplitSeries))
        {
            var rule = ruleByCode[group.Code];
            // Si el usuario dejó la capa vacía, se usa la propuesta por defecto.
            var layerBase = string.IsNullOrWhiteSpace(rule.LayerBase) ? LayerNames.ProposeBase(rule, _settings) : rule.LayerBase;
            var layer = LayerNames.ForLine(layerBase, group.Side, rule.SplitBySide, _settings);

            foreach (var sequence in BreakRules.Split(group, _settings, result.Cuts))
            {
                if (sequence.Count < 2)
                {
                    foreach (var alone in sequence)
                        result.Discarded.Add(new DiscardedPoint(alone.Point, group.Code, DiscardReason.Alone, $"único punto de {group.Label}"));
                    continue;
                }
                result.Lines.Add(new LineSequence(group.Code, group.Side, group.Series, layer, rule.ColorIndex, sequence));
            }
        }

        return result;
    }

    private static void Increment(Dictionary<string, int> counter, string key)
    {
        counter.TryGetValue(key, out var n);
        counter[key] = n + 1;
    }
}
