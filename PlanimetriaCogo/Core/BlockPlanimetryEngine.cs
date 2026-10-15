using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Bloques encontrados para una descripción (columna "Bloques" de la ventana): cuántos y el más largo.</summary>
public sealed record BlockStats(int Blocks, int MaxSize)
{
    public override string ToString() => Blocks == 0 ? "" : $"{Blocks} (máx. {MaxSize})";

    /// <summary>
    /// Cuenta los bloques de cada fila de la ventana (descripción sin marcas de inicio de bloque, ver
    /// <see cref="DescriptionParser.RowKey"/>) en orden de archivo, con la misma regla que el motor: un bloque es una
    /// corrida de filas consecutivas de la misma descripción y una marca de inicio de bloque ("casa B") abre siempre
    /// uno nuevo. Es informativo: los bloques reales se forman por código destino en <see cref="BlockPlanimetryEngine"/>.
    /// </summary>
    public static Dictionary<string, BlockStats> ByDescription(IEnumerable<SurveyPoint> pointsInFileOrder, Settings? settings = null)
    {
        var parser = new DescriptionParser(settings ?? new Settings());
        var sizes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var rowsByGroup = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string? previousGroup = null;

        foreach (var point in InFileOrder(pointsInFileOrder))
        {
            // Fila de la ventana ("casa B" → "casa") y grupo sin distinguir mayúsculas ("casa" y "CASA" comparten bloque).
            var row = parser.RowKey(point.RawDescription);
            var group = DescriptionNormalizer.Normalize(row);
            if (!rowsByGroup.TryGetValue(group, out var rows))
                rowsByGroup[group] = rows = new HashSet<string>(StringComparer.Ordinal);
            rows.Add(row);
            if (!sizes.TryGetValue(group, out var list))
                sizes[group] = list = new List<int>();

            if (group != previousGroup || parser.Parse(point.RawDescription).IsBlockStart)
                list.Add(1);
            else
                list[list.Count - 1]++;
            previousGroup = group;
        }

        var result = new Dictionary<string, BlockStats>(StringComparer.Ordinal);
        foreach (var (group, rows) in rowsByGroup)
            foreach (var row in rows)
                result[row] = new BlockStats(sizes[group].Count, sizes[group].Max());
        return result;
    }

    /// <summary>Orden de archivo: por <see cref="SurveyPoint.Sequence"/> (estable: sin secuencia se respeta el orden dado).</summary>
    public static IEnumerable<SurveyPoint> InFileOrder(IEnumerable<SurveyPoint> points) => points.OrderBy(p => p.Sequence);
}

/// <summary>
/// Motor del modo por bloques (comando PLANIMETRIACSV): dibuja la planimetría tal como se tomó en campo, sin eje
/// ni lados, siguiendo el orden de los puntos del archivo. Cada bloque es un elemento separado: una corrida de filas
/// consecutivas con el mismo código destino exacto (y serie, con <see cref="Settings.SplitSeries"/>). Con "Usar
/// nombre como código" el código es la descripción, así que el bloque es la corrida de filas con la misma
/// descripción; con el catálogo, las descripciones unificadas en un código ("cuneta", "CNT") comparten bloque.
/// El bloque termina en cuanto aparece una fila de otro código
/// —incluso una ignorada, sin resolver o de tipo Punto— o una marca de inicio de bloque ("casa B", ver
/// <see cref="Settings.BlockStartMarks"/>), que abre el siguiente. Dos bloques nunca se unen entre sí.
/// Según <see cref="CodeRule.BlockJoin"/> cada bloque produce una polilínea abierta o cerrada (solo si forma un
/// polígono, ver <see cref="FormsPolygon"/>); "Continua", que el usuario elige en la ventana (y se propone para el
/// eje en archivos sin marcas de inicio de bloque), une todos los puntos del código en una sola línea en orden de
/// archivo. El bloque no se corta por distancia ni por progresiva: solo las marcas explícitas de inicio/fin de la
/// descripción lo dividen. Los tramos más largos que <see cref="Settings.DistMax"/> se dibujan igual y se listan en
/// <see cref="PlanimetryResult.LongJumps"/> para revisarlos. Los bloques de un solo punto no se dibujan y se reportan.
/// El resultado reutiliza <see cref="PlanimetryResult"/>: la "progresiva" de cada punto es su posición en el
/// archivo y el lado es siempre <see cref="Side.Center"/>.
/// </summary>
public sealed class BlockPlanimetryEngine
{
    /// <summary>Puntos mínimos para cerrar un bloque: con 3 solo hay dos paredes y el cierre sería una diagonal.</summary>
    public const int MinPointsToClose = 4;

    /// <summary>Compacidad mínima (4πA/P²) del polígono cerrado: una fachada alineada da casi 0, un rectángulo 2:1 da 0,70.</summary>
    public const double MinCompactness = 0.3;

    /// <summary>Las dos esquinas que crea el lado de cierre deben estar entre estos ángulos (grados): cierre a escuadra.</summary>
    public const double MinClosingAngle = 60.0, MaxClosingAngle = 120.0;

    private readonly Settings _settings;
    private readonly Settings _breakSettings;
    private readonly DescriptionParser _parser;

    public BlockPlanimetryEngine(Settings settings)
    {
        _settings = settings;
        _parser = new DescriptionParser(settings);
        // El bloque del archivo es el elemento: solo cortan las marcas de inicio/fin escritas en la descripción.
        _breakSettings = settings.Clone();
        _breakSettings.StationGapMax = 0;
        _breakSettings.DistMax = 0;
    }

    public PlanimetryResult Run(IEnumerable<SurveyPoint> pointsInFileOrder, IEnumerable<CodeRule> rules)
    {
        var result = new PlanimetryResult { ByBlocks = true };
        var ruleByOriginal = new Dictionary<string, CodeRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
            ruleByOriginal[rule.Original] = rule;

        // 1. Clasificación de cada punto en orden de archivo. Clave de bloque: código destino exacto (+ serie).
        //    Si varias descripciones van al mismo código con capa/color/unión distintos, manda la de más puntos.
        var classified = new List<(string? Key, ResolvedPoint? Point)>();
        var ruleByKey = new Dictionary<string, CodeRule>(StringComparer.Ordinal);

        foreach (var point in BlockStats.InFileOrder(pointsInFileOrder))
        {
            result.TotalPoints++;
            var raw = point.RawDescription ?? "";
            // "casa B" usa la fila "casa": la marca de inicio de bloque no es parte de la descripción.
            var rowKey = _parser.RowKey(raw);
            if (!ruleByOriginal.TryGetValue(raw, out var rule))
                ruleByOriginal.TryGetValue(rowKey, out rule);

            if (rule != null && (rule.Kind == CodeKind.Ignore || !rule.Draw))
            {
                Increment(result.IgnoredCodes, rule.Original);
                classified.Add((null, null));
                continue;
            }
            if (rule == null || !rule.IsResolved)
            {
                Increment(result.UnresolvedCodes, rule?.Original ?? rowKey);
                classified.Add((null, null));
                continue;
            }

            var parsed = _parser.Parse(raw);
            if (rule.Kind == CodeKind.Point)
            {
                Increment(result.PointObjectCodes, rule.DestinationCode);
                var pointLayer = string.IsNullOrWhiteSpace(rule.LayerBase) ? LayerNames.ProposeBase(rule, _settings) : LayerNames.Sanitize(rule.LayerBase);
                result.PointObjects.Add(new PointObject(point, rule.DestinationCode, pointLayer, rule.ColorIndex));
                classified.Add((null, null));
                continue;
            }

            var series = _settings.SplitSeries ? parsed.Series : null;
            var key = series.HasValue ? $"{rule.DestinationCode}\u0001{series.Value}" : rule.DestinationCode;
            if (!ruleByKey.TryGetValue(key, out var existing) || rule.Count > existing.Count)
                ruleByKey[key] = rule;
            classified.Add((key, new ResolvedPoint(point, rule.DestinationCode, Side.Center, new Projection(point.Sequence, 0.0, -1), parsed)));
        }

        // 2. Bloques en orden de archivo, cada uno separado, y líneas continuas.
        var blocks = new List<(string Key, List<ResolvedPoint> Points)>();
        var continuous = new Dictionary<string, List<ResolvedPoint>>(StringComparer.Ordinal);
        string? previousKey = null;
        List<ResolvedPoint>? block = null;

        foreach (var (key, resolved) in classified)
        {
            if (key == null || resolved == null)
            {
                previousKey = null;
                continue;
            }

            if (ruleByKey[key].BlockJoin == BlockJoin.Continuous)
            {
                if (!continuous.TryGetValue(key, out var line))
                    continuous[key] = line = new List<ResolvedPoint>();
                line.Add(resolved);
            }
            else
            {
                if (key != previousKey || resolved.Parsed.IsBlockStart || block == null)
                {
                    block = new List<ResolvedPoint>();
                    blocks.Add((key, block));
                }
                block.Add(resolved);
            }
            previousKey = key;
        }

        // 3. Secuencias: cortes por marcas de inicio/fin y distancia dentro de cada bloque (o de la línea continua).
        //    La marca de inicio de bloque no es un corte: ya separó los bloques; en una línea continua no corta.
        foreach (var (key, points) in blocks)
            AddSequences(result, ruleByKey[key], points);
        foreach (var (key, points) in continuous.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            AddSequences(result, ruleByKey[key], points);

        return result;
    }

    private void AddSequences(PlanimetryResult result, CodeRule rule, List<ResolvedPoint> points)
    {
        var code = rule.DestinationCode;
        var series = _settings.SplitSeries ? points[0].Series : null;
        var layerBase = string.IsNullOrWhiteSpace(rule.LayerBase) ? LayerNames.ProposeBase(rule, _settings) : rule.LayerBase;
        var layer = LayerNames.ApplySingleLayer(LayerNames.Sanitize(layerBase), _settings);
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

            if (_settings.DistMax > 0)
            {
                for (var i = 1; i < sequence.Count; i++)
                {
                    var (from, to) = (sequence[i - 1], sequence[i]);
                    var distance = Math.Sqrt(Math.Pow(to.Point.X - from.Point.X, 2) + Math.Pow(to.Point.Y - from.Point.Y, 2));
                    if (distance > _settings.DistMax)
                        result.LongJumps.Add(new LineCut(from, to, distance, CutReason.Distance,
                            $"tramo de {distance:0.00} m, más que distMax {_settings.DistMax:0.##} m; se respeta el bloque"));
                }
            }

            var closed = false;
            if (rule.BlockJoin == BlockJoin.ClosedBlock && sequence.Count >= 3)
            {
                closed = FormsPolygon(sequence.Select(p => (p.Point.X, p.Point.Y)).ToList());
                if (!closed)
                    result.ClosedBlocksLeftOpen++;
            }
            result.Lines.Add(new LineSequence(code, Side.Center, series, layer, rule.ColorIndex, sequence, closed));
        }
    }

    /// <summary>
    /// true si cerrar el bloque produce un polígono real (una casa), no una diagonal inventada:
    /// <list type="bullet">
    ///   <item>al menos <see cref="MinPointsToClose"/> puntos;</item>
    ///   <item>compacidad 4πA/P² de al menos <see cref="MinCompactness"/> (una fachada tomada en línea no encierra área);</item>
    ///   <item>las dos esquinas que crea el lado de cierre, en el último y en el primer punto, entre
    ///   <see cref="MinClosingAngle"/> y <see cref="MaxClosingAngle"/> grados (una "L" de fachada y costado cerraría
    ///   con una diagonal muy aguda).</item>
    /// </list>
    /// </summary>
    public static bool FormsPolygon(IReadOnlyList<(double X, double Y)> points)
    {
        var n = points.Count;
        if (n < MinPointsToClose)
            return false;

        double area2 = 0, perimeter = 0;
        for (var i = 0; i < n; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % n];
            area2 += a.X * b.Y - b.X * a.Y;
            perimeter += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        }
        if (perimeter <= 0)
            return false;
        var compactness = 4 * Math.PI * (Math.Abs(area2) / 2) / (perimeter * perimeter);
        if (compactness < MinCompactness)
            return false;

        var atLast = Angle(points[n - 2], points[n - 1], points[0]);
        var atFirst = Angle(points[n - 1], points[0], points[1]);
        return atLast is >= MinClosingAngle and <= MaxClosingAngle && atFirst is >= MinClosingAngle and <= MaxClosingAngle;
    }

    /// <summary>Ángulo en grados en el vértice <paramref name="vertex"/> entre los lados hacia <paramref name="a"/> y <paramref name="b"/>.</summary>
    private static double Angle((double X, double Y) a, (double X, double Y) vertex, (double X, double Y) b)
    {
        double ux = a.X - vertex.X, uy = a.Y - vertex.Y, vx = b.X - vertex.X, vy = b.Y - vertex.Y;
        var norms = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
        if (norms <= 0)
            return 0;
        var cos = Math.Clamp((ux * vx + uy * vy) / norms, -1.0, 1.0);
        return Math.Acos(cos) * 180.0 / Math.PI;
    }

    private static void Increment(Dictionary<string, int> counter, string key)
    {
        counter.TryGetValue(key, out var n);
        counter[key] = n + 1;
    }
}
