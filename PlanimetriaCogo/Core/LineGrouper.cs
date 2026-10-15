using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Punto ya clasificado: código destino, lado, proyección elegida y descripción descompuesta.</summary>
public sealed record ResolvedPoint(SurveyPoint Point, string Code, Side Side, Projection Projection, ParsedDescription Parsed)
{
    /// <summary>Serie de la descripción ("CUNETA2" → 2) o null.</summary>
    public int? Series => Parsed.Series;
}

/// <summary>Grupo (código, lado[, serie]) con sus puntos ordenados.</summary>
public sealed class LineGroup
{
    public LineGroup(string code, Side side, int? series, IReadOnlyList<ResolvedPoint> points)
    {
        Code = code;
        Side = side;
        Series = series;
        Points = points;
    }

    public string Code { get; }
    public Side Side { get; }

    /// <summary>Serie del grupo cuando <see cref="Settings.SplitSeries"/> está activo; si no, null.</summary>
    public int? Series { get; }

    public IReadOnlyList<ResolvedPoint> Points { get; }

    /// <summary>Nombre para reportes: "CUNETA Izquierda" o "CUNETA Izquierda serie 2".</summary>
    public string Label => Series.HasValue ? $"{Code} {Side.ToSpanish()} serie {Series}" : $"{Code} {Side.ToSpanish()}";
}

/// <summary>
/// Agrupa por clave (código destino, lado) —y por serie si está activo— y ordena dentro del grupo por
/// número de punto (desempate por identificador de origen, para números duplicados) o por progresiva.
/// Los códigos sin lados deben llegar con <see cref="Side.Center"/>.
/// </summary>
public static class LineGrouper
{
    public static IReadOnlyList<LineGroup> Group(IEnumerable<ResolvedPoint> points) =>
        Group(points, PointOrder.Number, splitSeries: false);

    public static IReadOnlyList<LineGroup> Group(IEnumerable<ResolvedPoint> points, PointOrder order, bool splitSeries)
    {
        return points
            .GroupBy(p => (p.Code, p.Side, Series: splitSeries ? p.Series : null))
            .OrderBy(g => g.Key.Code, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Side)
            .ThenBy(g => g.Key.Series ?? int.MinValue)
            .Select(g => new LineGroup(g.Key.Code, g.Key.Side, g.Key.Series, Sort(g, order)))
            .ToList();
    }

    public static List<ResolvedPoint> Sort(IEnumerable<ResolvedPoint> points, PointOrder order)
    {
        return order == PointOrder.Station
            ? points.OrderBy(p => p.Projection.Station).ThenBy(p => p.Point.Number).ThenBy(p => p.Point.SourceId, StringComparer.Ordinal).ToList()
            : points.OrderBy(p => p.Point.Number).ThenBy(p => p.Point.SourceId, StringComparer.Ordinal).ToList();
    }
}
