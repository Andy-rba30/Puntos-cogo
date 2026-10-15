using System;
using System.Collections.Generic;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Motivo de un corte entre dos puntos consecutivos del mismo grupo.</summary>
public enum CutReason
{
    /// <summary>Distancia 2D mayor que distMax.</summary>
    Distance,

    /// <summary>Salto de progresiva mayor que stationGapMax.</summary>
    StationGap,

    /// <summary>El punto anterior lleva marca de fin ("F", "FIN").</summary>
    EndMark,

    /// <summary>El punto siguiente lleva marca de inicio ("INI", "..").</summary>
    StartMark
}

/// <summary>Corte registrado entre dos puntos consecutivos de un grupo.</summary>
public sealed record LineCut(ResolvedPoint From, ResolvedPoint To, double Distance, CutReason Reason, string Detail);

/// <summary>
/// Reglas de corte (§3.5): cuándo NO unir dos puntos consecutivos del mismo grupo. Cada regla se activa
/// desde <see cref="Settings"/> (0 = sin límite para las numéricas). Cada corte produce una nueva secuencia
/// dentro del grupo y queda registrado con los dos puntos involucrados: nada se une ni se corta "a ciegas".
/// Las series ("CUNETA1"/"CUNETA2") no cortan: forman grupos distintos en <see cref="LineGrouper"/>.
/// </summary>
public static class BreakRules
{
    /// <summary>Compatibilidad: solo distancia máxima.</summary>
    public static IReadOnlyList<IReadOnlyList<ResolvedPoint>> Split(LineGroup group, double distMax, List<LineCut> cuts) =>
        Split(group, new Settings { DistMax = distMax, StationGapMax = 0, UseDescriptionMarks = false }, cuts);

    /// <summary>Divide el grupo en secuencias según las reglas activas en <paramref name="settings"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<ResolvedPoint>> Split(LineGroup group, Settings settings, List<LineCut> cuts)
    {
        var sequences = new List<IReadOnlyList<ResolvedPoint>>();
        var current = new List<ResolvedPoint>();
        ResolvedPoint? previous = null;

        foreach (var point in group.Points)
        {
            if (previous != null)
            {
                var cut = Evaluate(previous, point, settings);
                if (cut != null)
                {
                    cuts.Add(cut);
                    sequences.Add(current);
                    current = new List<ResolvedPoint>();
                }
            }
            current.Add(point);
            previous = point;
        }

        if (current.Count > 0)
            sequences.Add(current);

        return sequences;
    }

    /// <summary>Devuelve el corte entre dos puntos consecutivos, o null si se unen. Orden de evaluación: marcas, distancia, progresiva.</summary>
    public static LineCut? Evaluate(ResolvedPoint previous, ResolvedPoint point, Settings settings)
    {
        var dx = point.Point.X - previous.Point.X;
        var dy = point.Point.Y - previous.Point.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (settings.UseDescriptionMarks)
        {
            if (previous.Parsed.IsEnd)
                return new LineCut(previous, point, distance, CutReason.EndMark, $"marca de fin en el punto {previous.Point.Number}");
            if (point.Parsed.IsStart)
                return new LineCut(previous, point, distance, CutReason.StartMark, $"marca de inicio en el punto {point.Point.Number}");
        }

        if (settings.DistMax > 0 && distance > settings.DistMax)
            return new LineCut(previous, point, distance, CutReason.Distance, $"distancia {distance:0.00} m > distMax {settings.DistMax:0.00} m");

        if (settings.StationGapMax > 0)
        {
            var gap = Math.Abs(point.Projection.Station - previous.Projection.Station);
            if (gap > settings.StationGapMax)
                return new LineCut(previous, point, distance, CutReason.StationGap, $"salto de progresiva {gap:0.00} m > stationGapMax {settings.StationGapMax:0.00} m");
        }

        return null;
    }

    public static string Describe(CutReason reason) => reason switch
    {
        CutReason.Distance => "distancia máxima",
        CutReason.StationGap => "salto de progresiva",
        CutReason.EndMark => "marca de fin",
        CutReason.StartMark => "marca de inicio",
        _ => reason.ToString()
    };
}
