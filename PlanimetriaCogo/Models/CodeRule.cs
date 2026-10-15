namespace PlanimetriaCogo.Models;

/// <summary>
/// Regla de clasificación de una descripción original: a qué código destino va, de qué tipo es,
/// en qué capa y color se dibuja y si se separa por lado. Es una fila de la ventana de clasificación
/// y el mapa interno que usa el motor (nunca se reescribe el CogoPoint).
/// </summary>
public sealed class CodeRule
{
    /// <summary>Descripción tal como está en los puntos (sensible a mayúsculas: "cumeta", "CNT", "v").</summary>
    public string Original { get; set; } = "";

    /// <summary>Cantidad de puntos con esa descripción.</summary>
    public int Count { get; set; }

    /// <summary>Código destino normalizado ("CUNETA"). Vacío = sin resolver.</summary>
    public string DestinationCode { get; set; } = "";

    public CodeKind Kind { get; set; } = CodeKind.Line;

    /// <summary>true = grupos separados izquierda/derecha; false = un solo grupo (p. ej. EJE).</summary>
    public bool SplitBySide { get; set; } = true;

    /// <summary>
    /// Capa base. Para líneas con lados se agrega el sufijo -IZQ / -DER al dibujar;
    /// para líneas sin lados y para puntos se usa tal cual.
    /// </summary>
    public string LayerBase { get; set; } = "";

    /// <summary>Color ACI (1..255) para la capa; 7 = blanco/negro por defecto.</summary>
    public short ColorIndex { get; set; } = 7;

    /// <summary>
    /// false = la fila no se dibuja (ni líneas ni objetos puntuales) pero conserva su código, tipo, capa y color,
    /// para volver a activarla sin reconfigurar. Sus puntos se reportan como ignorados.
    /// </summary>
    public bool Draw { get; set; } = true;

    public bool IsResolved => !string.IsNullOrWhiteSpace(DestinationCode);

    public CodeRule Clone() => (CodeRule)MemberwiseClone();
}
