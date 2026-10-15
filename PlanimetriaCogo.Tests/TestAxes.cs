using System;
using System.Collections.Generic;
using PlanimetriaCogo.Core;

namespace PlanimetriaCogo.Tests;

/// <summary>Ejes de prueba construidos con la poligonal muestreada del núcleo.</summary>
internal static class TestAxes
{
    /// <summary>Eje recto sobre el eje X, de (0,0) a (length,0). Izquierda = Y positivo.</summary>
    public static SampledPolylineAxis StraightX(double length = 100, double searchRadius = 100) =>
        SampledPolylineAxis.FromPoints(new[] { (0.0, 0.0), (length, 0.0) }, searchRadius);

    /// <summary>
    /// Curva de vuelta: tramo A hacia +X por y=0 (x de 0 a 100), semicírculo de radio 10 centrado en
    /// (100,10), y tramo B de regreso hacia −X por y=20 (x de 100 a 0). Progresivas: A 0–100,
    /// arco ≈100–131.4, B ≈131.4–231.4.
    /// </summary>
    public static SampledPolylineAxis Hairpin(double searchRadius = 100)
    {
        var pts = new List<(double, double)> { (0.0, 0.0), (100.0, 0.0) };
        const int arcSteps = 32;
        for (var i = 1; i < arcSteps; i++)
        {
            var angle = -Math.PI / 2 + Math.PI * i / arcSteps; // de −90° a +90° alrededor de (100,10)
            pts.Add((100.0 + 10.0 * Math.Cos(angle), 10.0 + 10.0 * Math.Sin(angle)));
        }
        pts.Add((100.0, 20.0));
        pts.Add((0.0, 20.0));
        return SampledPolylineAxis.FromPoints(pts, searchRadius);
    }
}
