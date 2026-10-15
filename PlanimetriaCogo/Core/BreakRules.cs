using System;
using System.Collections.Generic;

namespace PlanimetriaCogo.Core;

/// <summary>Corte registrado entre dos puntos consecutivos de un grupo.</summary>
public sealed record LineCut(ResolvedPoint From, ResolvedPoint To, double Distance, string Reason);

/// <summary>
/// Reglas de corte (§3.5). En la fase 1 solo se aplica la distancia máxima entre consecutivos;
/// el salto de progresiva, las marcas de inicio/fin y las series quedan para la fase 2.
/// </summary>
public static class BreakRules
{
    /// <summary>Divide el grupo en secuencias; cada corte queda registrado con los dos puntos.</summary>
    public static IReadOnlyList<IReadOnlyList<ResolvedPoint>> Split(LineGroup group, double distMax, List<LineCut> cuts)
    {
        var sequences = new List<IReadOnlyList<ResolvedPoint>>();
        var current = new List<ResolvedPoint>();
        ResolvedPoint? previous = null;

        foreach (var point in group.Points)
        {
            if (previous != null && distMax > 0)
            {
                var dx = point.Point.X - previous.Point.X;
                var dy = point.Point.Y - previous.Point.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > distMax)
                {
                    cuts.Add(new LineCut(previous, point, distance, $"distancia {distance:0.00} m > distMax {distMax:0.00} m"));
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
}
