using System;
using System.Collections.Generic;
using System.Linq;

namespace PlanimetriaCogo.Core;

/// <summary>Vértice de un eje poligonal con su progresiva.</summary>
public readonly record struct AxisVertex(double X, double Y, double Station);

/// <summary>
/// Eje definido por una poligonal con progresivas en los vértices (por ejemplo, un alineamiento
/// muestreado cada metro). Calcula todas las proyecciones candidatas: cada tramo donde la distancia
/// al punto es un mínimo local a lo largo de la poligonal es una candidata, de modo que en una
/// curva de vuelta se obtienen las dos (o más) pasadas del eje cercanas al punto.
/// Sin dependencias de AutoCAD; se usa también como eje de pruebas.
/// </summary>
public sealed class SampledPolylineAxis : IAxis
{
    private readonly AxisVertex[] _vertices;
    private readonly double _searchRadius;

    /// <param name="vertices">Vértices ordenados por progresiva creciente (al menos dos).</param>
    /// <param name="searchRadius">Solo se devuelven candidatas con distancia menor o igual (m).</param>
    public SampledPolylineAxis(IEnumerable<AxisVertex> vertices, double searchRadius)
    {
        _vertices = vertices.ToArray();
        if (_vertices.Length < 2)
            throw new ArgumentException("El eje necesita al menos dos vértices.", nameof(vertices));
        _searchRadius = searchRadius > 0 ? searchRadius : double.PositiveInfinity;
    }

    /// <summary>Construye el eje a partir de coordenadas, calculando progresivas acumuladas desde startStation.</summary>
    public static SampledPolylineAxis FromPoints(IEnumerable<(double X, double Y)> points, double searchRadius, double startStation = 0.0)
    {
        var list = new List<AxisVertex>();
        var station = startStation;
        (double X, double Y)? previous = null;
        foreach (var p in points)
        {
            if (previous.HasValue)
                station += Math.Sqrt((p.X - previous.Value.X) * (p.X - previous.Value.X) + (p.Y - previous.Value.Y) * (p.Y - previous.Value.Y));
            list.Add(new AxisVertex(p.X, p.Y, station));
            previous = p;
        }
        return new SampledPolylineAxis(list, searchRadius);
    }

    public double StartStation => _vertices[0].Station;
    public double EndStation => _vertices[^1].Station;

    public IEnumerable<Projection> ProjectAll(double x, double y)
    {
        var segmentCount = _vertices.Length - 1;
        var distance = new double[segmentCount];
        var param = new double[segmentCount];
        var signedOffset = new double[segmentCount];
        Array.Fill(distance, double.PositiveInfinity);

        for (var i = 0; i < segmentCount; i++)
        {
            var a = _vertices[i];
            var b = _vertices[i + 1];

            // Descarte rápido por caja envolvente ampliada con el radio de búsqueda.
            if (x < Math.Min(a.X, b.X) - _searchRadius || x > Math.Max(a.X, b.X) + _searchRadius ||
                y < Math.Min(a.Y, b.Y) - _searchRadius || y > Math.Max(a.Y, b.Y) + _searchRadius)
                continue;

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length2 = dx * dx + dy * dy;
            if (length2 <= 1e-18)
                continue; // tramo degenerado

            var t = ((x - a.X) * dx + (y - a.Y) * dy) / length2;
            var clamped = Math.Clamp(t, 0.0, 1.0);
            var px = a.X + clamped * dx;
            var py = a.Y + clamped * dy;
            var d = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));

            // Producto cruz tangente × (eje→punto): positivo = punto a la izquierda → offset negativo.
            var cross = dx * (y - a.Y) - dy * (x - a.X);

            distance[i] = d;
            param[i] = clamped;
            signedOffset[i] = cross > 0 ? -d : d;
        }

        var candidates = new List<Projection>();
        var i0 = 0;
        while (i0 < segmentCount)
        {
            if (double.IsPositiveInfinity(distance[i0]))
            {
                i0++;
                continue;
            }

            // Meseta: tramos consecutivos con la misma distancia (pie en un vértice compartido).
            var i1 = i0;
            while (i1 + 1 < segmentCount && Math.Abs(distance[i1 + 1] - distance[i0]) <= 1e-9)
                i1++;

            var left = i0 > 0 ? distance[i0 - 1] : double.PositiveInfinity;
            var right = i1 + 1 < segmentCount ? distance[i1 + 1] : double.PositiveInfinity;
            var d = distance[i0];

            if (d <= left && d <= right && d <= _searchRadius)
            {
                var atChainStart = i0 == 0 && param[i0] <= 0.0;
                var atChainEnd = i1 == segmentCount - 1 && param[i1] >= 1.0;
                if (!atChainStart && !atChainEnd)
                {
                    var a = _vertices[i0];
                    var b = _vertices[i0 + 1];
                    var station = a.Station + param[i0] * (b.Station - a.Station);
                    candidates.Add(new Projection(station, signedOffset[i0], i0));
                }
            }

            i0 = i1 + 1;
        }

        return candidates.OrderBy(c => c.Distance).ThenBy(c => c.Station);
    }
}
