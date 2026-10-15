using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Da formato al <see cref="PlanimetryResult"/> para la línea de comandos (resumen con listas acotadas)
/// y para archivo (.txt completo y .csv). Sin dependencias de AutoCAD para poder probarlo.
/// </summary>
public static class ReportFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Líneas del resumen. <paramref name="maxListed"/> acota cada lista; 0 = sin límite (archivo).</summary>
    public static List<string> Summary(PlanimetryResult result, int maxListed = 20)
    {
        var lines = new List<string>();
        var unlimited = maxListed <= 0;
        int Cap(int n) => unlimited ? n : Math.Min(n, maxListed);

        lines.Add(result.ByBlocks ? "--- PLANIMETRIACSV (por bloques): resumen ---" : "--- PLANIMETRIA: resumen ---");
        if (result.SourceDescription.Length > 0)
            lines.Add($"Origen: {result.SourceDescription}");
        if (result.AxisDescription.Length > 0)
            lines.Add($"Eje: {result.AxisDescription}");
        lines.Add($"Puntos procesados: {result.TotalPoints}");
        lines.Add($"Líneas creadas: {result.Lines.Count} ({result.PointsInLines} puntos unidos)"
                  + (result.ByBlocks ? $", {result.ClosedLines} cerrada(s)" : ""));
        if (result.ClosedBlocksLeftOpen > 0)
            lines.Add($"  Bloques cerrados que quedaron abiertos (fachada o ángulo, no encierran un polígono): {result.ClosedBlocksLeftOpen}");

        foreach (var group in result.Lines.GroupBy(l => l.Layer).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var points = group.Sum(l => l.Points.Count);
            lines.Add($"  {group.Key}: {group.Count()} línea(s), {points} puntos");
        }

        if (result.PointObjects.Count > 0)
        {
            lines.Add($"Objetos puntuales: {result.PointObjects.Count}");
            foreach (var kv in result.PointObjectCodes.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
                lines.Add($"  {kv.Key}: {kv.Value}");
        }

        if (result.Cuts.Count > 0)
        {
            lines.Add($"Cortes: {result.Cuts.Count}");
            foreach (var byReason in result.Cuts.GroupBy(c => c.Reason))
                lines.Add($"  por {BreakRules.Describe(byReason.Key)}: {byReason.Count()}");
            foreach (var cut in result.Cuts.Take(Cap(result.Cuts.Count)))
                lines.Add($"  {cut.From.Code}: punto {cut.From.Point.Number} → {cut.To.Point.Number} ({cut.Detail})");
            if (result.Cuts.Count > Cap(result.Cuts.Count))
                lines.Add($"  … y {result.Cuts.Count - maxListed} más");
        }

        if (result.LongJumps.Count > 0)
        {
            lines.Add($"Tramos largos dentro de un bloque (se dibujan, revíselos): {result.LongJumps.Count}");
            foreach (var jump in result.LongJumps.Take(Cap(result.LongJumps.Count)))
                lines.Add($"  {jump.From.Code}: punto {jump.From.Point.Number} → {jump.To.Point.Number} ({jump.Detail})");
            if (result.LongJumps.Count > Cap(result.LongJumps.Count))
                lines.Add($"  … y {result.LongJumps.Count - maxListed} más");
        }

        if (result.SideDiscrepancies.Count > 0)
        {
            lines.Add($"Lado explícito distinto del calculado (prevalece el escrito): {result.SideDiscrepancies.Count}");
            foreach (var d in result.SideDiscrepancies.Take(Cap(result.SideDiscrepancies.Count)))
                lines.Add($"  {d.Code}: punto {d.Point.Number} dice {d.Explicit.ToSpanish()}, offset {d.Offset.ToString("+0.00;-0.00", Inv)} m indica {d.Computed.ToSpanish()}");
            if (result.SideDiscrepancies.Count > Cap(result.SideDiscrepancies.Count))
                lines.Add($"  … y {result.SideDiscrepancies.Count - maxListed} más");
        }

        if (result.Discarded.Count > 0)
        {
            lines.Add($"Puntos descartados: {result.Discarded.Count}");
            foreach (var byReason in result.Discarded.GroupBy(d => d.Reason))
            {
                var all = byReason.ToList();
                lines.Add($"  {Describe(byReason.Key)}: {all.Count}");
                var numbers = all.Take(Cap(all.Count)).Select(d => $"{d.Point.Number}({d.Code})");
                lines.Add("    " + string.Join(", ", numbers) + (all.Count > Cap(all.Count) ? ", …" : ""));
            }
        }

        if (result.IgnoredCodes.Count > 0)
        {
            lines.Add($"Códigos ignorados: {result.IgnoredCodes.Values.Sum()} puntos");
            foreach (var kv in result.IgnoredCodes.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
                lines.Add($"  '{kv.Key}': {kv.Value}");
        }

        if (result.UnresolvedCodes.Count > 0)
        {
            lines.Add($"Códigos sin resolver (no se dibujaron): {result.UnresolvedCodes.Values.Sum()} puntos");
            foreach (var kv in result.UnresolvedCodes.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
                lines.Add($"  '{kv.Key}': {kv.Value}");
        }

        if (result.DescriptionChanges.Count > 0)
            lines.Add($"Descripciones que cambian con \"actualizar descripciones en el dibujo\": {result.DescriptionChanges.Count}");

        lines.Add("--- fin del resumen ---");
        return lines;
    }

    /// <summary>Reporte completo para archivo .txt: encabezado + resumen sin acotar + detalle de cada línea.</summary>
    public static string FullText(PlanimetryResult result, string drawingName, DateTime when)
    {
        var sb = new StringBuilder();
        sb.AppendLine(result.ByBlocks ? "PlanimetriaCogo — reporte del comando PLANIMETRIACSV (por bloques)" : "PlanimetriaCogo — reporte del comando PLANIMETRIA");
        sb.AppendLine($"Dibujo: {drawingName}");
        sb.AppendLine($"Fecha: {when.ToString("yyyy-MM-dd HH:mm:ss", Inv)}");
        sb.AppendLine();
        foreach (var line in Summary(result, 0))
            sb.AppendLine(line);

        if (result.Lines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(result.ByBlocks
                ? "Detalle de líneas (capa, código, abierta/cerrada, puntos desde → hasta, cantidad):"
                : "Detalle de líneas (capa, código, lado, puntos desde → hasta, cantidad):");
            foreach (var line in result.Lines)
                sb.AppendLine($"  {line.Layer}  {line.Code}  {SideOrShape(result, line)}{(line.Series.HasValue ? $" serie {line.Series}" : "")}  {line.FirstNumber} → {line.LastNumber}  ({line.Points.Count} puntos)");
        }

        if (result.DescriptionChanges.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Cambios de descripción propuestos (punto: actual → nueva):");
            foreach (var change in result.DescriptionChanges)
                sb.AppendLine($"  {change.Point.Number}: '{change.Point.RawDescription}' → '{change.NewDescription}'");
        }

        return sb.ToString();
    }

    /// <summary>
    /// CSV (separador ';', decimales con punto) con una fila por evento: LINEA, CORTE, DESCARTADO,
    /// DISCREPANCIA_LADO, PUNTO, IGNORADO, SIN_RESOLVER.
    /// </summary>
    public static string Csv(PlanimetryResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("tipo;codigo;lado;capa;punto_desde;punto_hasta;cantidad;detalle");

        foreach (var line in result.Lines)
        {
            var detail = string.Join(" ", new[] { line.Series.HasValue ? $"serie {line.Series}" : "", line.Closed ? "cerrada" : "" }.Where(s => s.Length > 0));
            Row(sb, "LINEA", line.Code, result.ByBlocks ? "" : line.Side.ToSpanish(), line.Layer, line.FirstNumber.ToString(Inv), line.LastNumber.ToString(Inv), line.Points.Count, detail);
        }

        foreach (var cut in result.Cuts)
            Row(sb, "CORTE", cut.From.Code, result.ByBlocks ? "" : cut.From.Side.ToSpanish(), "", cut.From.Point.Number.ToString(Inv), cut.To.Point.Number.ToString(Inv), 2, cut.Detail);

        foreach (var d in result.Discarded)
            Row(sb, "DESCARTADO", d.Code, "", "", d.Point.Number.ToString(Inv), "", 1, $"{Describe(d.Reason)}: {d.Detail}");

        foreach (var d in result.SideDiscrepancies)
            Row(sb, "DISCREPANCIA_LADO", d.Code, d.Explicit.ToSpanish(), "", d.Point.Number.ToString(Inv), "", 1, $"calculado {d.Computed.ToSpanish()}, offset {d.Offset.ToString("0.000", Inv)}");

        foreach (var p in result.PointObjects)
            Row(sb, "PUNTO", p.Code, "", p.Layer, p.Point.Number.ToString(Inv), "", 1, p.Point.RawDescription);

        foreach (var kv in result.IgnoredCodes)
            Row(sb, "IGNORADO", kv.Key, "", "", "", "", kv.Value, "");

        foreach (var kv in result.UnresolvedCodes)
            Row(sb, "SIN_RESOLVER", kv.Key, "", "", "", "", kv.Value, "");

        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string type, string code, string side, string layer, string from, string to, int count, string detail)
    {
        sb.Append(type).Append(';')
          .Append(Escape(code)).Append(';')
          .Append(side).Append(';')
          .Append(Escape(layer)).Append(';')
          .Append(from).Append(';')
          .Append(to).Append(';')
          .Append(count.ToString(Inv)).Append(';')
          .Append(Escape(detail))
          .AppendLine();
    }

    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static string SideOrShape(PlanimetryResult result, LineSequence line) =>
        result.ByBlocks ? (line.Closed ? "cerrada" : "abierta") : line.Side.ToSpanish();

    public static string Describe(DiscardReason reason) => reason switch
    {
        DiscardReason.NoProjection => "no proyectan sobre el eje",
        DiscardReason.BeyondOffsetMax => "fuera de offsetMax",
        DiscardReason.OnAxis => "sobre el eje con código por lados (lado indeterminado)",
        DiscardReason.Alone => "únicos en su grupo o bloque (sin línea)",
        _ => reason.ToString()
    };
}
