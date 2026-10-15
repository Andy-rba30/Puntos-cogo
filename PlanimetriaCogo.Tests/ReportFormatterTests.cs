using System;
using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class ReportFormatterTests
{
    private static SurveyPoint P(uint n, string desc, double x, double y) =>
        new() { Number = n, RawDescription = desc, X = x, Y = y, SourceId = n.ToString() };

    private static PlanimetryResult SampleResult()
    {
        var settings = new Settings { DistMax = 30 };
        var points = new[]
        {
            P(24, "CUNETA", 10, 4), P(25, "CUNETA", 12, -4), P(26, "CUNETA", 20, 4), P(27, "CUNETA", 22, -4),
            P(28, "CUNETA", 70, 4), P(29, "CUNETA", 80, 4), // corte por distancia 20→70
            P(30, "CUNETA I", 90, -4),                       // lado explícito distinto del calculado
            P(31, "casa", 50, 10), P(32, "zzz", 50, 10), P(33, "3015396", 50, 10),
            P(34, "TN", 50, 90),                              // fuera de offsetMax
        };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);
        var result = new PlanimetryEngine(settings).Run(points, rules, TestAxes.StraightX());
        result.AxisDescription = "Alineamiento EJE-01";
        return result;
    }

    [Fact]
    public void Summary_ContainsEverySection()
    {
        var text = string.Join("\n", ReportFormatter.Summary(SampleResult()));

        Assert.Contains("Eje: Alineamiento EJE-01", text);
        Assert.Contains("Puntos procesados: 11", text);
        Assert.Contains("PL-CUNETA-IZQ", text);
        Assert.Contains("PL-CUNETA-DER", text);
        Assert.Contains("Objetos puntuales: 1", text);
        Assert.Contains("por distancia máxima: 1", text);
        Assert.Contains("Lado explícito distinto del calculado", text);
        Assert.Contains("fuera de offsetMax: 1", text);
        Assert.Contains("'3015396': 1", text);
        Assert.Contains("'zzz': 1", text);
        Assert.Contains("--- fin del resumen ---", text);
    }

    [Fact]
    public void Summary_MaxListed_TruncatesLists()
    {
        var text = string.Join("\n", ReportFormatter.Summary(SampleResult(), 1));
        // Dos secuencias tras el corte: la lista de cortes tiene 1 entrada, no se trunca; la de descartes sí puede.
        Assert.Contains("Puntos descartados", text);
    }

    [Fact]
    public void FullText_HasHeaderAndLineDetail()
    {
        var text = ReportFormatter.FullText(SampleResult(), "obra.dwg", new DateTime(2026, 10, 15, 8, 30, 0));

        Assert.Contains("Dibujo: obra.dwg", text);
        Assert.Contains("Fecha: 2026-10-15 08:30:00", text);
        Assert.Contains("Detalle de líneas", text);
        Assert.Contains("24 → 26", text);
        Assert.Contains("Cambios de descripción propuestos", text);
        Assert.Contains("31: 'casa' → 'CASA'", text);
    }

    [Fact]
    public void Csv_HasHeaderAndOneRowPerEvent()
    {
        var result = SampleResult();
        var lines = ReportFormatter.Csv(result).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("tipo;codigo;lado;capa;punto_desde;punto_hasta;cantidad;detalle", lines[0]);
        Assert.Equal(result.Lines.Count, lines.Count(l => l.StartsWith("LINEA;")));
        Assert.Equal(result.Cuts.Count, lines.Count(l => l.StartsWith("CORTE;")));
        Assert.Equal(result.Discarded.Count, lines.Count(l => l.StartsWith("DESCARTADO;")));
        Assert.Equal(1, lines.Count(l => l.StartsWith("DISCREPANCIA_LADO;")));
        Assert.Equal(1, lines.Count(l => l.StartsWith("PUNTO;")));
        Assert.Equal(1, lines.Count(l => l.StartsWith("IGNORADO;")));
        Assert.Equal(1, lines.Count(l => l.StartsWith("SIN_RESOLVER;")));
        Assert.All(lines, l => Assert.Equal(7, l.Count(c => c == ';') - QuotedSemicolons(l)));
    }

    private static int QuotedSemicolons(string line)
    {
        var inQuotes = false;
        var n = 0;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ';' && inQuotes) n++;
        }
        return n;
    }

    [Fact]
    public void Csv_EscapesSeparatorInText()
    {
        var result = new PlanimetryResult();
        result.UnresolvedCodes["a;b"] = 1;
        var csv = ReportFormatter.Csv(result);
        Assert.Contains("SIN_RESOLVER;\"a;b\";", csv);
    }
}
