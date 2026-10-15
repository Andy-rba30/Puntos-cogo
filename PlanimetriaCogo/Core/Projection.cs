using System;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Una proyección candidata de un punto sobre el eje.
/// Offset con signo: negativo = izquierda, positivo = derecha (convención de Civil 3D).
/// </summary>
public readonly record struct Projection(double Station, double Offset, int SegmentIndex)
{
    /// <summary>Distancia perpendicular al eje (|offset|).</summary>
    public double Distance => Math.Abs(Offset);

    public override string ToString() => $"PK {Station:0.000} / offset {Offset:+0.000;-0.000}";
}
