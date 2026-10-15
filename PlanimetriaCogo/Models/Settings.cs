using System.Collections.Generic;

namespace PlanimetriaCogo.Models;

/// <summary>Criterio de orden dentro de cada grupo (código, lado).</summary>
public enum PointOrder
{
    /// <summary>Por número de punto (orden de toma en campo). Valor por defecto.</summary>
    Number,

    /// <summary>Por progresiva sobre el eje (sentido de la vía), útil si la toma fue desordenada.</summary>
    Station
}

/// <summary>
/// Parámetros del comando. Se guardan en <c>%APPDATA%\PlanimetriaCogo\config.json</c>
/// (ver <c>Config/ConfigService</c>) y se editan con el comando PLANIMETRIACONFIG o desde
/// la ventana de clasificación. Los valores de aquí son los predeterminados.
/// </summary>
public sealed class Settings
{
    // ---- Eje y lado ----

    /// <summary>Distancia máxima al eje (m). Puntos con |offset| mayor se descartan y se reportan.</summary>
    public double OffsetMax { get; set; } = 40.0;

    /// <summary>Tolerancia para considerar un punto "sobre el eje" (m).</summary>
    public double AxisTolerance { get; set; } = 0.15;

    /// <summary>Paso de muestreo del alineamiento o polilínea (m) para obtener todas las proyecciones candidatas.</summary>
    public double AxisSampleStep { get; set; } = 1.0;

    /// <summary>Código destino cuyos puntos forman el eje cuando se elige "eje desde puntos" (§9.2).</summary>
    public string AxisCode { get; set; } = "EJE";

    /// <summary>Si el eje se construyó desde puntos, dibujarlo también como Polyline3d (§10.1 "conservar eje generado").</summary>
    public bool KeepGeneratedAxis { get; set; } = false;

    /// <summary>Capa donde se dibuja el eje generado desde puntos cuando <see cref="KeepGeneratedAxis"/> está activo.</summary>
    public string GeneratedAxisLayer { get; set; } = "PL-EJE-GENERADO";

    // ---- Orden y reglas de corte (§3.4 y §3.5) ----

    /// <summary>Orden dentro del grupo: número de punto (por defecto) o progresiva.</summary>
    public PointOrder Order { get; set; } = PointOrder.Number;

    /// <summary>Distancia 2D máxima entre puntos consecutivos de un grupo (m). 0 = sin límite.</summary>
    public double DistMax { get; set; } = 30.0;

    /// <summary>Salto máximo de progresiva entre puntos consecutivos de un grupo (m). 0 = sin límite.</summary>
    public double StationGapMax { get; set; } = 100.0;

    /// <summary>Interpretar las marcas de inicio/fin y de lado en la descripción ("CUNETA F", "CUNETA I").</summary>
    public bool UseDescriptionMarks { get; set; } = true;

    /// <summary>Marcas que indican que en este punto EMPIEZA una línea nueva (corte con el anterior).</summary>
    public List<string> StartMarks { get; set; } = new() { "INI", "INICIO", ".." };

    /// <summary>Marcas que indican que en este punto TERMINA la línea (corte con el siguiente).</summary>
    public List<string> EndMarks { get; set; } = new() { "F", "FIN" };

    /// <summary>Marcas de lado izquierdo explícito. Prevalecen sobre el lado calculado (§3.2).</summary>
    public List<string> LeftMarks { get; set; } = new() { "I", "IZQ" };

    /// <summary>Marcas de lado derecho explícito.</summary>
    public List<string> RightMarks { get; set; } = new() { "D", "DER" };

    /// <summary>Sufijo numérico de serie ("CUNETA1", "CUNETA2") produce líneas distintas.</summary>
    public bool SplitSeries { get; set; } = false;

    // ---- Acciones sobre el dibujo (todas apagadas por defecto, §10.1) ----

    /// <summary>Mover los CogoPoint de tipo Punto a la capa indicada en su fila (§10.2).</summary>
    public bool MovePointObjectsToLayer { get; set; } = false;

    /// <summary>Reescribir la descripción cruda de los puntos con el código destino (conservando marcas).</summary>
    public bool UpdateDescriptionsInDrawing { get; set; } = false;

    // ---- Reporte ----

    /// <summary>Además de la línea de comandos, escribir el reporte en archivos .txt y .csv.</summary>
    public bool ReportToFile { get; set; } = false;

    /// <summary>Carpeta del reporte. Vacío = la carpeta del DWG (o %APPDATA%\PlanimetriaCogo\reportes si el DWG no está guardado).</summary>
    public string ReportFolder { get; set; } = "";

    // ---- Capas ----

    /// <summary>Prefijo de capa para líneas.</summary>
    public string LineLayerPrefix { get; set; } = "PL-";

    /// <summary>Prefijo de capa para objetos puntuales.</summary>
    public string PointLayerPrefix { get; set; } = "PT-";

    public string LeftLayerSuffix { get; set; } = "-IZQ";
    public string RightLayerSuffix { get; set; } = "-DER";

    public Settings Clone()
    {
        var copy = (Settings)MemberwiseClone();
        copy.StartMarks = new List<string>(StartMarks);
        copy.EndMarks = new List<string>(EndMarks);
        copy.LeftMarks = new List<string>(LeftMarks);
        copy.RightMarks = new List<string>(RightMarks);
        return copy;
    }

    /// <summary>Copia todos los valores de otra instancia (para aplicar lo editado en la ventana de parámetros).</summary>
    public void CopyFrom(Settings other)
    {
        OffsetMax = other.OffsetMax;
        AxisTolerance = other.AxisTolerance;
        AxisSampleStep = other.AxisSampleStep;
        AxisCode = other.AxisCode;
        KeepGeneratedAxis = other.KeepGeneratedAxis;
        GeneratedAxisLayer = other.GeneratedAxisLayer;
        Order = other.Order;
        DistMax = other.DistMax;
        StationGapMax = other.StationGapMax;
        UseDescriptionMarks = other.UseDescriptionMarks;
        StartMarks = new List<string>(other.StartMarks);
        EndMarks = new List<string>(other.EndMarks);
        LeftMarks = new List<string>(other.LeftMarks);
        RightMarks = new List<string>(other.RightMarks);
        SplitSeries = other.SplitSeries;
        MovePointObjectsToLayer = other.MovePointObjectsToLayer;
        UpdateDescriptionsInDrawing = other.UpdateDescriptionsInDrawing;
        ReportToFile = other.ReportToFile;
        ReportFolder = other.ReportFolder;
        LineLayerPrefix = other.LineLayerPrefix;
        PointLayerPrefix = other.PointLayerPrefix;
        LeftLayerSuffix = other.LeftLayerSuffix;
        RightLayerSuffix = other.RightLayerSuffix;
    }
}
