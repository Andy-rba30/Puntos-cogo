using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Eje construido desde puntos: la poligonal, los puntos usados y su longitud.</summary>
public sealed class BuiltAxis
{
    public BuiltAxis(SampledPolylineAxis axis, IReadOnlyList<SurveyPoint> points, double length, int droppedDuplicates)
    {
        Axis = axis;
        Points = points;
        Length = length;
        DroppedDuplicates = droppedDuplicates;
    }

    public SampledPolylineAxis Axis { get; }

    /// <summary>Puntos del eje en el orden usado (por número), sin duplicados consecutivos.</summary>
    public IReadOnlyList<SurveyPoint> Points { get; }

    public double Length { get; }

    /// <summary>Puntos descartados por coincidir en planta con el anterior.</summary>
    public int DroppedDuplicates { get; }
}

/// <summary>
/// Construye el eje de referencia desde los puntos cuyo código destino es el del eje (§9.2: patrón "v, e, v";
/// por defecto EJE), ordenados por número de punto. El eje vive en memoria (§10.1): no se crea ningún
/// Alignment ni PointGroup; si el usuario marca "conservar eje generado" el comando lo dibuja como Polyline3d.
/// </summary>
public static class AxisBuilder
{
    /// <summary>Radio de búsqueda de candidatas: mayor que offsetMax para distinguir "fuera de offsetMax" de "no proyecta".</summary>
    public static double SearchRadius(Settings settings) => Math.Max(settings.OffsetMax * 2.0, settings.OffsetMax + 50.0);

    /// <summary>Puntos cuya fila tiene como código destino el código del eje (cualquier tipo, incluso Ignorar).</summary>
    public static List<SurveyPoint> SelectAxisPoints(IEnumerable<SurveyPoint> points, IEnumerable<CodeRule> rules, Settings settings)
    {
        var axisCode = DescriptionNormalizer.Normalize(settings.AxisCode);
        var originals = new HashSet<string>(
            rules.Where(r => r.DestinationCode == axisCode).Select(r => r.Original), StringComparer.Ordinal);
        return points
            .Where(p => originals.Contains(p.RawDescription ?? ""))
            .OrderBy(p => p.Number).ThenBy(p => p.SourceId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Lanza <see cref="InvalidOperationException"/> si no hay al menos dos puntos distintos.</summary>
    public static BuiltAxis Build(IEnumerable<SurveyPoint> axisPoints, Settings settings, double startStation = 0.0)
    {
        var used = new List<SurveyPoint>();
        var dropped = 0;
        foreach (var p in axisPoints)
        {
            if (used.Count > 0)
            {
                var last = used[used.Count - 1];
                if (Math.Abs(last.X - p.X) < 1e-6 && Math.Abs(last.Y - p.Y) < 1e-6)
                {
                    dropped++;
                    continue;
                }
            }
            used.Add(p);
        }

        if (used.Count < 2)
            throw new InvalidOperationException(
                $"Se necesitan al menos dos puntos con código {settings.AxisCode} para construir el eje (hay {used.Count}).");

        var axis = SampledPolylineAxis.FromPoints(used.Select(p => (p.X, p.Y)), SearchRadius(settings), startStation);
        return new BuiltAxis(axis, used, axis.EndStation - axis.StartStation, dropped);
    }

    public static BuiltAxis FromPoints(IEnumerable<SurveyPoint> points, IEnumerable<CodeRule> rules, Settings settings) =>
        Build(SelectAxisPoints(points, rules, settings), settings);
}
