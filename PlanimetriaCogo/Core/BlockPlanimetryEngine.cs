using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Bloques encontrados para una descripción original: cuántos y el más largo (columna "Bloques" de la ventana).</summary>
public sealed record BlockStats(int Blocks, int MaxSize)
{
    public override string ToString() => Blocks == 0 ? "" : $"{Blocks} (máx. {MaxSize})";

    /// <summary>
    /// Cuenta las corridas de filas consecutivas con la misma descripción original (normalizada) en orden de archivo.
    /// Es informativo: los bloques reales se forman por código destino en <see cref="BlockPlanimetryEngine"/>.
    /// </summary>
    public static Dictionary<string, BlockStats> ByDescription(IEnumerable<SurveyPoint> pointsInFileOrder)
    {
        var blocks = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxSize = new Dictionary<string, int>(StringComparer.Ordinal);
        var originals = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string? currentKey = null;
        var currentSize = 0;

        void Flush()
        {
            if (currentKey == null)
                return;
            blocks[currentKey] = blocks.GetValueOrDefault(currentKey) + 1;
            maxSize[currentKey] = Math.Max(maxSize.GetValueOrDefault(currentKey), currentSize);
        }

        foreach (var point in InFileOrder(pointsInFileOrder))
        {
            var raw = point.RawDescription ?? "";
            var key = DescriptionNormalizer.Normalize(raw);
            if (!originals.TryGetValue(key, out var set))
                originals[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(raw);
            if (key == currentKey)
            {
                currentSize++;
                continue;
            }
            Flush();
            currentKey = key;
            currentSize = 1;
        }
        Flush();

        var result = new Dictionary<string, BlockStats>(StringComparer.Ordinal);
        foreach (var (key, set) in originals)
            foreach (var original in set)
                result[original] = new BlockStats(blocks[key], maxSize[key]);
        return result;
    }

    /// <summary>Orden de archivo: por <see cref="SurveyPoint.Sequence"/> (estable: sin secuencia se respeta el orden dado).</summary>
    public static IEnumerable<SurveyPoint> InFileOrder(IEnumerable<SurveyPoint> points) => points.OrderBy(p => p.Sequence);
}

/// <summary>
/// Motor del modo por bloques (comando PLANIMETRIACSV): dibuja la planimetría tal como se tomó en campo, sin eje
/// ni lados. Se recorren los puntos en orden de archivo; un <b>bloque</b> es una corrida de filas consecutivas
/// cuyo código destino es el mismo (y la misma serie, con <see cref="Settings.SplitSeries"/>). Cualquier fila
/// intermedia de otro código —incluso una ignorada o sin resolver— termina el bloque: así se respeta lo que
/// hizo el topógrafo. Según <see cref="CodeRule.BlockJoin"/> cada bloque produce una polilínea abierta, una
/// cerrada (3 o más puntos) o, en el modo continuo, todos los puntos del código forman una sola línea.
/// Dentro de una secuencia se aplican las marcas de inicio/fin y <see cref="Settings.DistMax"/> (no el salto de
/// progresiva, que no existe aquí). Los bloques de un solo punto no se dibujan y se reportan.
/// El resultado reutiliza <see cref="PlanimetryResult"/>: la "progresiva" de cada punto es su posición en el
/// archivo y el lado es siempre <see cref="Side.Center"/>.
/// </summary>
public sealed class BlockPlanimetryEngine
{
    private readonly Settings _settings;
    private readonly Settings _breakSettings;
    private readonly DescriptionParser _parser;

    public BlockPlanimetryEngine(Settings settings)
    {
        _settings = settings;
        _parser = new DescriptionParser(settings);
        // Mismas reglas de corte que el modo por eje, salvo el salto de progresiva (no hay eje).
        _breakSettings = settings.Clone();
        _breakSettings.StationGapMax = 0;
    }

    public PlanimetryResult Run(IEnumerable<SurveyPoint> pointsInFileOrder, IEnumerable<CodeRule> rules)
    {
        var result = new PlanimetryResult { ByBlocks = true };
        var ruleByOriginal = new Dictionary<string, CodeRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
            ruleByOriginal[rule.Original] = rule;
        var ruleByCode = new Dictionary<string, CodeRule>(StringComparer.Ordinal);

        // 1. Recorrido en orden de archivo: clasificar y cortar en bloques.
        var runs = new List<(string Key, List<ResolvedPoint> Points)>();
        var continuous = new Dictionary<string, List<ResolvedPoint>>(StringComparer.Ordinal);
        string? currentKey = null;
        List<ResolvedPoint>? current = null;

        foreach (var point in BlockStats.InFileOrder(pointsInFileOrder))
        {
            result.TotalPoints++;
            var raw = point.RawDescription ?? "";
            ruleByOriginal.TryGetValue(raw, out var rule);

            string? key = null;
            ResolvedPoint? resolved = null;

            if (rule != null && (rule.Kind == CodeKind.Ignore || !rule.Draw))
                Increment(result.IgnoredCodes, raw);
            else if (rule == null || !rule.IsResolved)
                Increment(result.UnresolvedCodes, raw);
            else
            {
                var parsed = _parser.Parse(raw);
                if (rule.Kind == CodeKind.Point)
                {
                    Increment(result.PointObjectCodes, rule.DestinationCode);
                    var pointLayer = string.IsNullOrWhiteSpace(rule.LayerBase) ? LayerNames.ProposeBase(rule, _settings) : LayerNames.Sanitize(rule.LayerBase);
                    result.PointObjects.Add(new PointObject(point, rule.DestinationCode, pointLayer, rule.ColorIndex));
                }
                else
                {
                    // Si varias descripciones van al mismo código con capa/color/unión distintos, manda la de más puntos.
                    if (!ruleByCode.TryGetValue(rule.DestinationCode, out var existing) || rule.Count > existing.Count)
                        ruleByCode[rule.DestinationCode] = rule;

                    var series = _settings.SplitSeries ? parsed.Series : null;
                    key = series.HasValue ? $"{rule.DestinationCode}\u0001{series.Value}" : rule.DestinationCode;
                    var projection = new Projection(point.Sequence, 0.0, -1);
                    resolved = new ResolvedPoint(point, rule.DestinationCode, Side.Center, projection, parsed);
                }
            }

            if (key != currentKey)
            {
                currentKey = key;
                current = key == null ? null : new List<ResolvedPoint>();
                if (current != null)
                    runs.Add((key!, current));
            }

            if (resolved != null)
            {
                if (rule!.BlockJoin == BlockJoin.Continuous)
                {
                    if (!continuous.TryGetValue(key!, out var list))
                        continuous[key!] = list = new List<ResolvedPoint>();
                    list.Add(resolved);
                }
                else
                {
                    current!.Add(resolved);
                }
            }
        }

        // 2. Secuencias: cortes por marcas y distancia dentro de cada bloque (o de la línea continua).
        foreach (var (key, points) in runs.Where(r => r.Points.Count > 0))
            AddSequences(result, key, points, ruleByCode);
        foreach (var (key, points) in continuous.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            AddSequences(result, key, points, ruleByCode);

        return result;
    }

    private void AddSequences(PlanimetryResult result, string key, List<ResolvedPoint> points, Dictionary<string, CodeRule> ruleByCode)
    {
        var code = points[0].Code;
        var rule = ruleByCode[code];
        var series = _settings.SplitSeries ? points[0].Series : null;
        var layerBase = string.IsNullOrWhiteSpace(rule.LayerBase) ? LayerNames.ProposeBase(rule, _settings) : rule.LayerBase;
        var layer = LayerNames.Sanitize(layerBase);
        var group = new LineGroup(code, Side.Center, series, points);

        foreach (var sequence in BreakRules.Split(group, _breakSettings, result.Cuts))
        {
            if (sequence.Count < 2)
            {
                foreach (var alone in sequence)
                    result.Discarded.Add(new DiscardedPoint(alone.Point, code, DiscardReason.Alone,
                        rule.BlockJoin == BlockJoin.Continuous ? $"único punto de {code}" : $"bloque de un solo punto ({code})"));
                continue;
            }
            var closed = rule.BlockJoin == BlockJoin.ClosedBlock && sequence.Count >= 3;
            result.Lines.Add(new LineSequence(code, Side.Center, series, layer, rule.ColorIndex, sequence, closed));
        }
    }

    private static void Increment(Dictionary<string, int> counter, string key)
    {
        counter.TryGetValue(key, out var n);
        counter[key] = n + 1;
    }
}
