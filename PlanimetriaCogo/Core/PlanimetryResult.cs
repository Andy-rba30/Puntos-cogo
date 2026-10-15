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

/// <summary>Secuencia lista para dibujar como una Polyline3d.</summary>
public sealed class LineSequence
{
    public LineSequence(string code, Side side, string layer, short colorIndex, IReadOnlyList<ResolvedPoint> points)
    {
        Code = code;
        Side = side;
        Layer = layer;
        ColorIndex = colorIndex;
        Points = points;
    }

    public string Code { get; }
    public Side Side { get; }
    public string Layer { get; }
    public short ColorIndex { get; }
    public IReadOnlyList<ResolvedPoint> Points { get; }
}

/// <summary>Resultado completo del procesamiento, base del reporte en la línea de comandos.</summary>
public sealed class PlanimetryResult
{
    public List<LineSequence> Lines { get; } = new();
    public List<DiscardedPoint> Discarded { get; } = new();
    public List<LineCut> Cuts { get; } = new();

    /// <summary>Descripción original → cantidad, para filas de tipo Ignorar.</summary>
    public Dictionary<string, int> IgnoredCodes { get; } = new();

    /// <summary>Descripción original → cantidad, para filas sin código destino.</summary>
    public Dictionary<string, int> UnresolvedCodes { get; } = new();

    /// <summary>Código destino → cantidad, para objetos puntuales (solo se cuentan en la fase 1).</summary>
    public Dictionary<string, int> PointObjectCodes { get; } = new();

    public int TotalPoints { get; set; }

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
