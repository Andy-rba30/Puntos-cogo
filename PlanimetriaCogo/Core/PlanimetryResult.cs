using System.Collections.Generic;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Motivo por el que un punto no se dibuja.</summary>
public enum DiscardReason
{
    /// <summary>El punto no proyecta sobre el eje (antes del inicio, después del fin o lejos de todo tramo).</summary>
    NoProjection,

    /// <summary>Proyecta, pero |offset| supera offsetMax.</summary>
    BeyondOffsetMax,

    /// <summary>Código con lados pero el punto está sobre el eje (|offset| menor que tolEje): lado indeterminado.</summary>
    OnAxis,

    /// <summary>Grupo o secuencia de un solo punto: no hay línea que dibujar.</summary>
    Alone
}

public sealed record DiscardedPoint(SurveyPoint Point, string Code, DiscardReason Reason, string Detail);

/// <summary>El lado escrito en la descripción ("CUNETA I") no coincide con el calculado por el offset (§3.2).</summary>
public sealed record SideDiscrepancy(SurveyPoint Point, string Code, Side Explicit, Side Computed, double Offset);

/// <summary>Objeto puntual clasificado (tipo Punto) con la capa a la que iría si se activa "mover a capa".</summary>
public sealed record PointObject(SurveyPoint Point, string Code, string Layer, short ColorIndex);

/// <summary>Cambio de descripción propuesto para la opción "actualizar descripciones en el dibujo".</summary>
public sealed record DescriptionChange(SurveyPoint Point, string NewDescription);

/// <summary>Secuencia lista para dibujar como una Polyline3d.</summary>
public sealed class LineSequence
{
    public LineSequence(string code, Side side, int? series, string layer, short colorIndex, IReadOnlyList<ResolvedPoint> points, bool closed = false)
    {
        Code = code;
        Side = side;
        Series = series;
        Layer = layer;
        ColorIndex = colorIndex;
        Points = points;
        Closed = closed;
    }

    public string Code { get; }
    public Side Side { get; }
    public int? Series { get; }
    public string Layer { get; }
    public short ColorIndex { get; }
    public IReadOnlyList<ResolvedPoint> Points { get; }

    /// <summary>true = polilínea cerrada (bloque cerrado del modo por bloques).</summary>
    public bool Closed { get; }

    public uint FirstNumber => Points[0].Point.Number;
    public uint LastNumber => Points[Points.Count - 1].Point.Number;
}

/// <summary>Resultado completo del procesamiento, base del reporte en la línea de comandos y en archivo.</summary>
public sealed class PlanimetryResult
{
    public List<LineSequence> Lines { get; } = new();
    public List<DiscardedPoint> Discarded { get; } = new();
    public List<LineCut> Cuts { get; } = new();
    public List<SideDiscrepancy> SideDiscrepancies { get; } = new();

    /// <summary>Objetos puntuales, uno por punto (para contarlos y, si se activa, moverlos de capa).</summary>
    public List<PointObject> PointObjects { get; } = new();

    /// <summary>Descripciones que cambiarían con "actualizar descripciones en el dibujo" (solo las distintas a la actual).</summary>
    public List<DescriptionChange> DescriptionChanges { get; } = new();

    /// <summary>Descripción original → cantidad, para filas de tipo Ignorar.</summary>
    public Dictionary<string, int> IgnoredCodes { get; } = new();

    /// <summary>Descripción original → cantidad, para filas sin código destino.</summary>
    public Dictionary<string, int> UnresolvedCodes { get; } = new();

    /// <summary>Código destino → cantidad, para objetos puntuales.</summary>
    public Dictionary<string, int> PointObjectCodes { get; } = new();

    public int TotalPoints { get; set; }

    /// <summary>Descripción del eje usado (nombre del alineamiento, polilínea o "N puntos EJE") para el reporte.</summary>
    public string AxisDescription { get; set; } = "";

    /// <summary>true cuando lo produjo el modo por bloques (sin eje ni lados; la progresiva es la posición en el archivo).</summary>
    public bool ByBlocks { get; set; }

    /// <summary>Origen de los puntos en el modo por bloques (nombre del archivo CSV) para el reporte.</summary>
    public string SourceDescription { get; set; } = "";

    public int ClosedLines
    {
        get
        {
            var n = 0;
            foreach (var line in Lines)
                if (line.Closed) n++;
            return n;
        }
    }

    public int PointsInLines
    {
        get
        {
            var n = 0;
            foreach (var line in Lines)
                n += line.Points.Count;
            return n;
        }
    }
}
