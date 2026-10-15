using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Acad;

/// <summary>
/// IAxis sobre una polilínea del dibujo (Polyline, Polyline2d o Polyline3d), fase 3 (§3.2).
///
/// Igual que <see cref="AlignmentAxis"/>: se muestrea la curva cada <see cref="Settings.AxisSampleStep"/> metros
/// con GetPointAtDist (respeta los arcos de las polilíneas 2D) y <see cref="SampledPolylineAxis"/> devuelve todas
/// las candidatas. La más cercana se reemplaza por la proyección exacta de la curva (GetClosestPointTo proyectado
/// en planta + GetDistAtPoint; el signo sale del producto cruz entre la tangente y el vector eje→punto):
/// negativo = izquierda, positivo = derecha mirando en el sentido de la polilínea.
/// La progresiva empieza en 0 en el primer vértice. En una Polyline3d la distancia a lo largo de la curva es 3D.
/// </summary>
public sealed class PolylineAxis : IAxis
{
    private readonly Curve _curve;
    private readonly SampledPolylineAxis _sampled;
    private readonly double _step;
    private readonly double _searchRadius;

    public PolylineAxis(Curve curve, Settings settings)
    {
        _curve = curve ?? throw new ArgumentNullException(nameof(curve));
        _step = settings.AxisSampleStep > 0 ? settings.AxisSampleStep : 1.0;
        _searchRadius = AxisBuilder.SearchRadius(settings);
        _sampled = new SampledPolylineAxis(Sample(curve, _step), _searchRadius);
    }

    public int SampleCount { get; private set; }

    public double Length { get; private set; }

    public static bool IsSupported(Entity? entity) => entity is Polyline || entity is Polyline2d || entity is Polyline3d;

    private IEnumerable<AxisVertex> Sample(Curve curve, double step)
    {
        var start = curve.GetDistanceAtParameter(curve.StartParam);
        var end = curve.GetDistanceAtParameter(curve.EndParam);
        Length = end - start;
        if (Length <= 0)
            throw new InvalidOperationException("La polilínea no tiene longitud.");

        var count = Math.Max(2, (int)Math.Ceiling(Length / step) + 1);
        var vertices = new List<AxisVertex>(count);
        for (var i = 0; i < count; i++)
        {
            var dist = Math.Min(start + i * step, end);
            var p = curve.GetPointAtDist(dist);
            vertices.Add(new AxisVertex(p.X, p.Y, dist - start));
            if (dist >= end)
                break;
        }
        SampleCount = vertices.Count;
        return vertices;
    }

    public IEnumerable<Projection> ProjectAll(double x, double y)
    {
        var candidates = _sampled.ProjectAll(x, y).ToList();

        if (TryExact(x, y, out var station, out var offset))
        {
            var index = candidates.FindIndex(c => Math.Abs(c.Station - station) <= 2.0 * _step);
            if (index >= 0)
                candidates[index] = new Projection(station, offset, candidates[index].SegmentIndex);
            else if (Math.Abs(offset) <= _searchRadius && candidates.Count == 0)
                candidates.Add(new Projection(station, offset, -1));
        }

        return candidates.OrderBy(c => c.Distance).ThenBy(c => c.Station);
    }

    private bool TryExact(double x, double y, out double station, out double offset)
    {
        station = 0;
        offset = 0;
        try
        {
            var point = new Point3d(x, y, 0);
            var closest = _curve.GetClosestPointTo(point, Vector3d.ZAxis, false);
            var dist = _curve.GetDistAtPoint(closest);
            station = dist - _curve.GetDistanceAtParameter(_curve.StartParam);

            var param = _curve.GetParameterAtPoint(closest);
            var tangent = _curve.GetFirstDerivative(param);
            var toPoint = new Vector3d(x - closest.X, y - closest.Y, 0);
            var distance2d = toPoint.Length;
            // Producto cruz tangente × (eje→punto): positivo = punto a la izquierda → offset negativo.
            var cross = tangent.X * toPoint.Y - tangent.Y * toPoint.X;
            offset = cross > 0 ? -distance2d : distance2d;
            return !double.IsNaN(station) && !double.IsNaN(offset);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
