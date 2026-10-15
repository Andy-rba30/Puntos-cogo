using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Civil.DatabaseServices;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Acad;

/// <summary>
/// IAxis sobre un Alignment de Civil 3D.
///
/// Para obtener TODAS las proyecciones candidatas (necesario en curvas de vuelta) se muestrea el
/// alineamiento con PointLocation cada <see cref="Settings.AxisSampleStep"/> metros y se delega
/// en <see cref="SampledPolylineAxis"/>, que detecta un mínimo local de distancia por cada pasada
/// del eje cercana al punto. La candidata más cercana se reemplaza por el valor exacto de
/// Alignment.StationOffset (misma convención de signo: negativo = izquierda, positivo = derecha),
/// de modo que la proyección "normal" coincide con la que muestra Civil 3D y solo las candidatas
/// alternativas llevan el error del muestreo (del orden de milímetros con paso de 1 m).
/// </summary>
public sealed class AlignmentAxis : IAxis
{
    private readonly Alignment _alignment;
    private readonly SampledPolylineAxis _sampled;
    private readonly double _step;
    private readonly double _searchRadius;

    public AlignmentAxis(Alignment alignment, Settings settings)
    {
        _alignment = alignment ?? throw new ArgumentNullException(nameof(alignment));
        _step = settings.AxisSampleStep > 0 ? settings.AxisSampleStep : 1.0;
        // Radio mayor que offsetMax para poder distinguir "fuera de offsetMax" de "no proyecta".
        _searchRadius = AxisBuilder.SearchRadius(settings);
        _sampled = new SampledPolylineAxis(Sample(alignment, _step), _searchRadius);
    }

    public int SampleCount { get; private set; }

    private IEnumerable<AxisVertex> Sample(Alignment alignment, double step)
    {
        var start = alignment.StartingStation;
        var end = alignment.EndingStation;
        if (end <= start)
            throw new InvalidOperationException("El alineamiento no tiene longitud.");

        var count = Math.Max(2, (int)Math.Ceiling((end - start) / step) + 1);
        var vertices = new List<AxisVertex>(count);
        for (var i = 0; i < count; i++)
        {
            var station = Math.Min(start + i * step, end);
            double easting = 0, northing = 0;
            alignment.PointLocation(station, 0.0, ref easting, ref northing);
            vertices.Add(new AxisVertex(easting, northing, station));
            if (station >= end)
                break;
        }
        SampleCount = vertices.Count;
        return vertices;
    }

    public IEnumerable<Projection> ProjectAll(double x, double y)
    {
        var candidates = _sampled.ProjectAll(x, y).ToList();

        // Valor exacto de Civil 3D para la proyección más cercana.
        if (TryExactStationOffset(x, y, out var station, out var offset))
        {
            var index = candidates.FindIndex(c => Math.Abs(c.Station - station) <= 2.0 * _step);
            if (index >= 0)
                candidates[index] = new Projection(station, offset, candidates[index].SegmentIndex);
            else if (Math.Abs(offset) <= _searchRadius)
                candidates.Add(new Projection(station, offset, -1));
        }

        return candidates.OrderBy(c => c.Distance).ThenBy(c => c.Station);
    }

    private bool TryExactStationOffset(double x, double y, out double station, out double offset)
    {
        station = 0;
        offset = 0;
        try
        {
            _alignment.StationOffset(x, y, ref station, ref offset);
            return !double.IsNaN(station) && !double.IsNaN(offset);
        }
        catch (Exception)
        {
            // Civil 3D lanza excepción cuando el punto no proyecta sobre el alineamiento.
            return false;
        }
    }
}
