namespace PlanimetriaCogo.Models;

/// <summary>
/// Punto de levantamiento leído desde un CogoPoint. Solo datos: no referencia tipos de AutoCAD.
/// </summary>
public sealed class SurveyPoint
{
    /// <summary>Número de punto (PointNumber).</summary>
    public uint Number { get; init; }

    /// <summary>Nombre del punto (puede estar vacío).</summary>
    public string Name { get; init; } = "";

    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }

    /// <summary>Descripción de campo tal como está en el punto (RawDescription), sin expandir.</summary>
    public string RawDescription { get; init; } = "";

    /// <summary>
    /// Identificador de origen (Handle en AutoCAD) usado como desempate cuando hay números
    /// de punto duplicados y para el reporte.
    /// </summary>
    public string SourceId { get; init; } = "";

    /// <summary>
    /// Posición en el archivo de origen (1 = primera fila leída), para el modo por bloques, que respeta el
    /// orden de toma en campo. 0 cuando el punto viene del dibujo (el orden lo da el número de punto).
    /// </summary>
    public int Sequence { get; init; }

    public override string ToString() => $"#{Number} '{RawDescription}' ({X:0.000}, {Y:0.000}, {Z:0.000})";
}
