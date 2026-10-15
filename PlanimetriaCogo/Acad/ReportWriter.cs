using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Acad;

/// <summary>Resumen del comando en la línea de comandos.</summary>
public static class ReportWriter
{
    private const int MaxListed = 20;

    public static void Write(Editor ed, PlanimetryResult result)
    {
        ed.WriteMessage("\n--- PLANIMETRIA: resumen ---");
        ed.WriteMessage($"\nPuntos procesados: {result.TotalPoints}");
        ed.WriteMessage($"\nLíneas creadas: {result.Lines.Count} ({result.PointsInLines} puntos unidos)");

        foreach (var group in result.Lines.GroupBy(l => (l.Code, l.Side, l.Layer)).OrderBy(g => g.Key.Layer))
        {
            var points = group.Sum(l => l.Points.Count);
            ed.WriteMessage($"\n  {group.Key.Layer}: {group.Count()} línea(s), {points} puntos");
        }

        if (result.PointObjectCodes.Count > 0)
        {
            var total = result.PointObjectCodes.Values.Sum();
            ed.WriteMessage($"\nObjetos puntuales (no se dibujan en esta fase): {total}");
            foreach (var kv in result.PointObjectCodes.OrderByDescending(k => k.Value))
                ed.WriteMessage($"\n  {kv.Key}: {kv.Value}");
        }

        if (result.Cuts.Count > 0)
        {
            ed.WriteMessage($"\nCortes por distancia máxima: {result.Cuts.Count}");
            foreach (var cut in result.Cuts.Take(MaxListed))
                ed.WriteMessage($"\n  {cut.From.Code}: punto {cut.From.Point.Number} → {cut.To.Point.Number} ({cut.Reason})");
            if (result.Cuts.Count > MaxListed)
                ed.WriteMessage($"\n  … y {result.Cuts.Count - MaxListed} más");
        }

        WriteDiscarded(ed, result);

        if (result.IgnoredCodes.Count > 0)
        {
            ed.WriteMessage($"\nCódigos ignorados: {result.IgnoredCodes.Values.Sum()} puntos");
            foreach (var kv in result.IgnoredCodes.OrderByDescending(k => k.Value))
                ed.WriteMessage($"\n  '{kv.Key}': {kv.Value}");
        }

        if (result.UnresolvedCodes.Count > 0)
        {
            ed.WriteMessage($"\nCódigos sin resolver (no se dibujaron): {result.UnresolvedCodes.Values.Sum()} puntos");
            foreach (var kv in result.UnresolvedCodes.OrderByDescending(k => k.Value))
                ed.WriteMessage($"\n  '{kv.Key}': {kv.Value}");
        }

        ed.WriteMessage("\n--- fin del resumen ---\n");
    }

    private static void WriteDiscarded(Editor ed, PlanimetryResult result)
    {
        if (result.Discarded.Count == 0)
            return;

        ed.WriteMessage($"\nPuntos descartados: {result.Discarded.Count}");
        foreach (var byReason in result.Discarded.GroupBy(d => d.Reason))
        {
            ed.WriteMessage($"\n  {Describe(byReason.Key)}: {byReason.Count()}");
            var numbers = byReason.Take(MaxListed).Select(d => $"{d.Point.Number}({d.Code})");
            ed.WriteMessage("\n    " + string.Join(", ", numbers) + (byReason.Count() > MaxListed ? ", …" : ""));
        }
    }

    private static string Describe(DiscardReason reason) => reason switch
    {
        DiscardReason.NoProjection => "no proyectan sobre el eje",
        DiscardReason.BeyondOffsetMax => "fuera de offsetMax",
        DiscardReason.OnAxis => "sobre el eje con código por lados (lado indeterminado)",
        DiscardReason.Alone => "únicos en su grupo (sin línea)",
        _ => reason.ToString()
    };
}
