using System.Collections.Generic;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Eje de referencia. Devuelve TODAS las proyecciones candidatas de un punto (una por cada paso
/// del eje cerca del punto), para que el modo continuidad pueda elegir en curvas de vuelta.
/// La primera candidata es la más cercana. Vacío = el punto no proyecta sobre el eje.
/// </summary>
public interface IAxis
{
    IEnumerable<Projection> ProjectAll(double x, double y);
}
