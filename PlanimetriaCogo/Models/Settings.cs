namespace PlanimetriaCogo.Models;

/// <summary>
/// Parámetros del comando. En la fase 1 son valores por defecto en código (sin JSON).
/// </summary>
public sealed class Settings
{
    /// <summary>Distancia máxima al eje (m). Puntos con |offset| mayor se descartan y se reportan.</summary>
    public double OffsetMax { get; set; } = 40.0;

    /// <summary>Distancia 2D máxima entre puntos consecutivos de un grupo (m). Si se supera, se corta la línea.</summary>
    public double DistMax { get; set; } = 30.0;

    /// <summary>Tolerancia para considerar un punto "sobre el eje" (m).</summary>
    public double AxisTolerance { get; set; } = 0.15;

    /// <summary>Paso de muestreo del alineamiento (m) para obtener todas las proyecciones candidatas.</summary>
    public double AxisSampleStep { get; set; } = 1.0;

    /// <summary>Prefijo de capa para líneas.</summary>
    public string LineLayerPrefix { get; set; } = "PL-";

    /// <summary>Prefijo de capa para objetos puntuales.</summary>
    public string PointLayerPrefix { get; set; } = "PT-";

    public string LeftLayerSuffix { get; set; } = "-IZQ";
    public string RightLayerSuffix { get; set; } = "-DER";
}
