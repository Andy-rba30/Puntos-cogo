using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Punto ya clasificado: código destino, lado y proyección elegida.</summary>
public sealed record ResolvedPoint(SurveyPoint Point, string Code, Side Side, Projection Projection);

/// <summary>Grupo (código, lado) con sus puntos ordenados por número.</summary>
public sealed class LineGroup
{
    public LineGroup(string code, Side side, IReadOnlyList<ResolvedPoint> points)
    {
        Code = code;
        Side = side;
        Points = points;
    }

    public string Code { get; }
    public Side Side { get; }
    public IReadOnlyList<ResolvedPoint> Points { get; }
}

/// <summary>
/// Agrupa por clave (código destino, lado) y ordena dentro del grupo por número de punto
/// (desempate por identificador de origen, para números duplicados). Los códigos sin lados
/// deben llegar con <see cref="Side.Center"/>.
/// </summary>
public static class LineGrouper
{
    public static IReadOnlyList<LineGroup> Group(IEnumerable<ResolvedPoint> points)
    {
        return points
            .GroupBy(p => (p.Code, p.Side))
            .OrderBy(g => g.Key.Code, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Side)
            .Select(g => new LineGroup(
                g.Key.Code,
                g.Key.Side,
                g.OrderBy(p => p.Point.Number).ThenBy(p => p.Point.SourceId, StringComparer.Ordinal).ToList()))
            .ToList();
    }
}
