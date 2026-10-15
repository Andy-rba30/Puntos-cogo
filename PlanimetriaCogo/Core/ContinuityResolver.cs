using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Resultado de proyectar un punto: la candidata elegida o el motivo por el que no hay.</summary>
public readonly record struct AxisResolution(Projection? Chosen, bool HadCandidates)
{
    public bool Ok => Chosen.HasValue;
}

/// <summary>
/// Modo "continuidad" (§3.3): entre varias proyecciones candidatas de un punto, elige la que
/// minimiza el salto de progresiva respecto al punto anterior de la misma secuencia (mismo código
/// destino, orden por número de punto). El primer punto de la secuencia toma la candidata más cercana.
/// Así, en una curva de vuelta, un punto más cercano a la pasada siguiente del eje se queda en la
/// pasada por la que venía avanzando el topógrafo.
/// </summary>
public sealed class ContinuityResolver
{
    private readonly IAxis _axis;
    private readonly double _offsetMax;

    public ContinuityResolver(IAxis axis, double offsetMax)
    {
        _axis = axis;
        _offsetMax = offsetMax > 0 ? offsetMax : double.PositiveInfinity;
    }

    /// <summary>Candidatas dentro de offsetMax, más cercana primero.</summary>
    public List<Projection> Candidates(double x, double y, out bool hadAny)
    {
        var all = _axis.ProjectAll(x, y).ToList();
        hadAny = all.Count > 0;
        return all.Where(p => p.Distance <= _offsetMax).OrderBy(p => p.Distance).ToList();
    }

    /// <summary>Primer punto de la secuencia: la candidata más cercana.</summary>
    public static Projection? ChooseFirst(IReadOnlyList<Projection> candidates) =>
        candidates.Count == 0 ? null : candidates.OrderBy(c => c.Distance).First();

    /// <summary>Puntos siguientes: menor salto de progresiva; empate → más cercana.</summary>
    public static Projection? ChooseNext(IReadOnlyList<Projection> candidates, double previousStation) =>
        candidates.Count == 0
            ? null
            : candidates.OrderBy(c => Math.Abs(c.Station - previousStation)).ThenBy(c => c.Distance).First();

    /// <summary>
    /// Resuelve una secuencia ya ordenada (por número de punto). Devuelve una resolución por punto,
    /// en el mismo orden. Los puntos sin candidata válida no rompen la continuidad: el siguiente se
    /// compara con el último punto que sí proyectó.
    /// </summary>
    public IReadOnlyList<AxisResolution> Resolve(IReadOnlyList<SurveyPoint> orderedPoints)
    {
        var result = new List<AxisResolution>(orderedPoints.Count);
        double? previousStation = null;

        foreach (var point in orderedPoints)
        {
            var candidates = Candidates(point.X, point.Y, out var hadAny);
            var chosen = previousStation.HasValue
                ? ChooseNext(candidates, previousStation.Value)
                : ChooseFirst(candidates);

            if (chosen.HasValue)
                previousStation = chosen.Value.Station;

            result.Add(new AxisResolution(chosen, hadAny));
        }

        return result;
    }
}
