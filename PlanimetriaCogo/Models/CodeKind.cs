namespace PlanimetriaCogo.Models;

/// <summary>Qué se hace con los puntos de una descripción.</summary>
public enum CodeKind
{
    /// <summary>Se unen con una polilínea por lado.</summary>
    Line,

    /// <summary>Objeto puntual (casa, muro, BM…): no se une. En la fase 1 solo se cuenta.</summary>
    Point,

    /// <summary>Se omite y se reporta.</summary>
    Ignore
}

/// <summary>Textos en español para la interfaz.</summary>
public static class CodeKindNames
{
    public const string Line = "Línea";
    public const string Point = "Punto";
    public const string Ignore = "Ignorar";

    public static readonly string[] All = { Line, Point, Ignore };

    public static string ToSpanish(this CodeKind kind) => kind switch
    {
        CodeKind.Line => Line,
        CodeKind.Point => Point,
        _ => Ignore
    };

    public static CodeKind FromSpanish(string? text) => text switch
    {
        Point => CodeKind.Point,
        Ignore => CodeKind.Ignore,
        _ => CodeKind.Line
    };
}
