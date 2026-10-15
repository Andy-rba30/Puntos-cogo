using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Orquesta el cálculo completo sin tocar AutoCAD:
/// clasificar por reglas → proyectar con continuidad → lado → agrupar (código, lado) → cortar → secuencias.
/// </summary>
public sealed class PlanimetryEngine
{
    private readonly Settings _settings;

    public PlanimetryEngine(Settings settings)
    {
        _settings = settings;
    }

    public PlanimetryResult Run(IEnumerable<SurveyPoint> points, IEnumerable<CodeRule> rules, IAxis axis)
    {
        var result = new PlanimetryResult();
        var ruleByOriginal = new Dictionary<string, CodeRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
            ruleByOriginal[rule.Original] = rule;

        // 1. Clasificación por regla: líneas por código destino; el resto solo se cuenta.
        var lineCandidates = new Dictionary<string, List<SurveyPoint>>(StringComparer.Ordinal);
        var ruleByCode = new Dictionary<string, CodeRule>(StringComparer.Ordinal);

        foreach (var point in points)
        {
            result.TotalPoints++;
            var raw = point.RawDescription ?? "";
            ruleByOriginal.TryGetValue(raw, out var rule);

            if (rule != null && rule.Kind == CodeKind.Ignore)
            {
                Increment(result.IgnoredCodes, raw);
                continue;
            }
            if (rule == null || !rule.IsResolved)
            {
                Increment(result.UnresolvedCodes, raw);
                continue;
            }
            if (rule.Kind == CodeKind.Point)
            {
                Increment(result.PointObjectCodes, rule.DestinationCode);
                continue;
            }

            if (!lineCandidates.TryGetValue(rule.DestinationCode, out var list))
            {
                list = new List<SurveyPoint>();
                lineCandidates[rule.DestinationCode] = list;
            }
            list.Add(point);

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
            var ordered = list.OrderBy(p => p.Number).ThenBy(p => p.SourceId, StringComparer.Ordinal).ToList();
            var resolutions = resolver.Resolve(ordered);

            for (var i = 0; i < ordered.Count; i++)
            {
                var point = ordered[i];
                var resolution = resolutions[i];

                if (!resolution.Ok)
                {
                    result.Discarded.Add(resolution.HadCandidates
                        ? new DiscardedPoint(point, code, DiscardReason.BeyondOffsetMax, $"|offset| > {_settings.OffsetMax:0.00} m")
                        : new DiscardedPoint(point, code, DiscardReason.NoProjection, "no proyecta sobre el eje"));
                    continue;
                }

                var projection = resolution.Chosen!.Value;
                var side = SideClassifier.Classify(projection.Offset, _settings.AxisTolerance);

                if (rule.SplitBySide && side == Side.Center)
                {
                    result.Discarded.Add(new DiscardedPoint(point, code, DiscardReason.OnAxis, $"|offset| {projection.Distance:0.000} m < tolEje {_settings.AxisTolerance:0.000} m"));
                    continue;
                }

                resolved.Add(new ResolvedPoint(point, code, rule.SplitBySide ? side : Side.Center, projection));
            }
        }

        // 3. Agrupar, cortar y armar secuencias dibujables.
        foreach (var group in LineGrouper.Group(resolved))
        {
            var rule = ruleByCode[group.Code];
            // Si el usuario dejó la capa vacía, se usa la propuesta por defecto.
            var layerBase = string.IsNullOrWhiteSpace(rule.LayerBase) ? LayerNames.ProposeBase(rule, _settings) : rule.LayerBase;
            var layer = LayerNames.ForLine(layerBase, group.Side, rule.SplitBySide, _settings);

            foreach (var sequence in BreakRules.Split(group, _settings.DistMax, result.Cuts))
            {
                if (sequence.Count < 2)
                {
                    foreach (var alone in sequence)
                        result.Discarded.Add(new DiscardedPoint(alone.Point, group.Code, DiscardReason.Alone, $"único punto de {group.Code} {group.Side.ToSpanish()}"));
                    continue;
                }
                result.Lines.Add(new LineSequence(group.Code, group.Side, layer, rule.ColorIndex, sequence));
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
