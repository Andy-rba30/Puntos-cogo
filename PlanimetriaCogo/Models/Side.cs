namespace PlanimetriaCogo.Models;

/// <summary>
/// Lado del eje en el que cae un punto, mirando en el sentido de progresivas crecientes.
/// Convención de Civil 3D: offset negativo = izquierda, positivo = derecha.
/// </summary>
public enum Side
{
    /// <summary>Izquierda del eje (offset negativo).</summary>
    Left,

    /// <summary>Derecha del eje (offset positivo).</summary>
    Right,

    /// <summary>Sobre el eje (|offset| menor que la tolerancia) o código "sin lados".</summary>
    Center
}

/// <summary>Textos en español para mostrar lados en reportes e interfaz.</summary>
public static class SideNames
{
    public static string ToSpanish(this Side side) => side switch
    {
        Side.Left => "Izquierda",
        Side.Right => "Derecha",
        _ => "Centro"
    };
}
