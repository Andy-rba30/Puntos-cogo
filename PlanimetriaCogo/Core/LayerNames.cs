using System.Text;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Construcción y saneado de nombres de capa (PL-CUNETA-IZQ, PT-CASA…).</summary>
public static class LayerNames
{
    // Caracteres que AutoCAD no admite en nombres de símbolos.
    private const string Forbidden = "<>/\\\":;?*|,=`";

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
            sb.Append(Forbidden.IndexOf(ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }

    /// <summary>Capa base propuesta para una regla según su tipo: PL-&lt;CODIGO&gt; o PT-&lt;CODIGO&gt;.</summary>
    public static string ProposeBase(CodeRule rule, Settings settings)
    {
        if (!rule.IsResolved || rule.Kind == CodeKind.Ignore)
            return "";
        var prefix = rule.Kind == CodeKind.Point ? settings.PointLayerPrefix : settings.LineLayerPrefix;
        return Sanitize(prefix + rule.DestinationCode);
    }

    /// <summary>Capa final de una línea: base + sufijo de lado cuando el código se separa por lado.</summary>
    public static string ForLine(string layerBase, Side side, bool splitBySide, Settings settings)
    {
        var baseName = Sanitize(layerBase);
        if (!splitBySide)
            return baseName;
        return side switch
        {
            Side.Left => baseName + settings.LeftLayerSuffix,
            Side.Right => baseName + settings.RightLayerSuffix,
            _ => baseName
        };
    }
}
