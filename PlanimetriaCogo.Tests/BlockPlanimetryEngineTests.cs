using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class BlockPlanimetryEngineTests
{
    /// <summary>Puntos en orden de archivo: la secuencia es la posición en la lista; las coordenadas avanzan 1 m por punto.</summary>
    private static List<SurveyPoint> File(params string[] descriptions) =>
        descriptions.Select((d, i) => new SurveyPoint
        {
            Number = (uint)(100 + i), RawDescription = d, X = i, Y = 0, Z = i * 0.1, Sequence = i + 1, SourceId = (i + 1).ToString()
        }).ToList();

    private static PlanimetryResult Run(List<SurveyPoint> points, Settings? settings = null, System.Action<List<CodeRule>>? edit = null)
    {
        settings ??= new Settings();
        var rules = ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, settings);
        edit?.Invoke(rules);
        return new BlockPlanimetryEngine(settings).Run(points, rules);
    }

    [Fact]
    public void ConsecutiveSameCode_FormsOneBlock_ClosedForCasa()
    {
        var points = File("casa", "casa", "casa", "casa", "e", "casa", "casa");
        var result = Run(points);

        Assert.True(result.ByBlocks);
        Assert.Equal(2, result.Lines.Count);
        var house = result.Lines[0];
        Assert.Equal("CASA", house.Code);
        Assert.Equal("PL-CASA", house.Layer);
        Assert.True(house.Closed);
        Assert.Equal(new uint[] { 100, 101, 102, 103 }, house.Points.Select(p => p.Point.Number));

        var edge = result.Lines[1];
        Assert.False(edge.Closed); // dos puntos: no se cierra
        Assert.Equal(new uint[] { 105, 106 }, edge.Points.Select(p => p.Point.Number));
        Assert.Equal(1, result.ClosedLines);

        // El "e" suelto es un bloque de un solo punto: se reporta, no se dibuja.
        var alone = Assert.Single(result.Discarded);
        Assert.Equal(DiscardReason.Alone, alone.Reason);
        Assert.Equal(104u, alone.Point.Number);
    }

    [Fact]
    public void UnifiedDescriptions_ShareTheBlock()
    {
        var result = Run(File("cuneta", "cumeta", "CNT", "tn"));
        var line = Assert.Single(result.Lines);
        Assert.Equal("CUNETA", line.Code);
        Assert.Equal("PL-CUNETA", line.Layer); // sin sufijo de lado
        Assert.Equal(3, line.Points.Count);
    }

    [Fact]
    public void AnyIntermediateRow_BreaksTheBlock_EvenIfNotDrawn()
    {
        var result = Run(File("v", "v", "3015396", "v", "v"));
        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.Equal(2, l.Points.Count));
        Assert.Equal(1, result.IgnoredCodes["3015396"]);
    }

    [Fact]
    public void DrawUnchecked_AndIgnore_AreNotDrawn()
    {
        var result = Run(File("tn", "tn", "b", "b"), edit: rules =>
        {
            rules.Single(r => r.Original == "tn").Draw = false;
            rules.Single(r => r.Original == "b").Kind = CodeKind.Ignore;
        });
        Assert.Empty(result.Lines);
        Assert.Equal(2, result.IgnoredCodes["tn"]);
        Assert.Equal(2, result.IgnoredCodes["b"]);
        Assert.Equal(4, result.TotalPoints);
    }

    [Fact]
    public void PointKind_IsCountedNotJoined_AndUnresolvedReported()
    {
        var result = Run(File("Pt", "Pt", "zzz", "zzz"));
        Assert.Empty(result.Lines);
        Assert.Equal(2, result.PointObjects.Count);
        Assert.Equal("PT-PT", result.PointObjects[0].Layer);
        Assert.Equal(2, result.UnresolvedCodes["zzz"]);
    }

    [Fact]
    public void DistMax_CutsInsideABlock_AndIsReported()
    {
        var points = File("tn", "tn", "tn", "tn");
        points[2] = new SurveyPoint { Number = 102, RawDescription = "tn", X = 80, Y = 0, Sequence = 3, SourceId = "3" };
        points[3] = new SurveyPoint { Number = 103, RawDescription = "tn", X = 81, Y = 0, Sequence = 4, SourceId = "4" };
        var result = Run(points, new Settings { DistMax = 30 });

        Assert.Equal(2, result.Lines.Count);
        var cut = Assert.Single(result.Cuts);
        Assert.Equal(CutReason.Distance, cut.Reason);
        Assert.Equal(101u, cut.From.Point.Number);
        Assert.Equal(102u, cut.To.Point.Number);
    }

    [Fact]
    public void EndMark_CutsInsideABlock()
    {
        var result = Run(File("cuneta", "cuneta f", "cuneta", "cuneta"));
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(CutReason.EndMark, Assert.Single(result.Cuts).Reason);
        Assert.Equal(new uint[] { 100, 101 }, result.Lines[0].Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void AxisCode_IsContinuousAcrossBlocks()
    {
        var result = Run(File("v", "e", "v", "v", "e", "v", "e"));

        var axis = Assert.Single(result.Lines, l => l.Code == "EJE");
        Assert.False(axis.Closed);
        Assert.Equal(new uint[] { 101, 104, 106 }, axis.Points.Select(p => p.Point.Number));
        var vereda = Assert.Single(result.Lines, l => l.Code == "VEREDA");
        Assert.Equal(new uint[] { 102, 103 }, vereda.Points.Select(p => p.Point.Number));
        // Los "v" sueltos se reportan como bloques de un punto.
        Assert.Equal(new uint[] { 100, 105 }, result.Discarded.Select(d => d.Point.Number));
    }

    [Fact]
    public void ContinuousJoin_CanBeChosenForAnyCode()
    {
        var result = Run(File("b", "e", "b", "e", "b"), edit: rules =>
            rules.Single(r => r.Original == "b").BlockJoin = BlockJoin.Continuous);
        var borde = Assert.Single(result.Lines, l => l.Code == "BORDE");
        Assert.Equal(3, borde.Points.Count);
    }

    [Fact]
    public void SplitSeries_SeparatesBlocks()
    {
        var points = File("cuneta1", "cuneta1", "cuneta2", "cuneta2");
        Assert.Single(Run(points).Lines);
        var split = Run(points, new Settings { SplitSeries = true });
        Assert.Equal(2, split.Lines.Count);
        Assert.Equal(1, split.Lines[0].Series);
        Assert.Equal(2, split.Lines[1].Series);
    }

    [Fact]
    public void FileOrder_PrevailsOverPointNumber()
    {
        var points = File("tn", "tn", "tn");
        points[0] = new SurveyPoint { Number = 900, RawDescription = "tn", X = 0, Y = 0, Sequence = 1, SourceId = "1" };
        var line = Assert.Single(Run(points).Lines);
        Assert.Equal(new uint[] { 900, 101, 102 }, line.Points.Select(p => p.Point.Number));
        // La "progresiva" del modo por bloques es la posición en el archivo.
        Assert.Equal(new double[] { 1, 2, 3 }, line.Points.Select(p => p.Projection.Station));
    }

    [Fact]
    public void ClosedBlockCodes_FromSettings_AndMinimumThreePoints()
    {
        var settings = new Settings { ClosedBlockCodes = new List<string> { "casa", "muro" } };
        var result = Run(File("Muro", "Muro", "Muro", "tn", "Muro", "Muro"), settings);
        Assert.True(result.Lines[0].Closed);
        Assert.False(result.Lines[1].Closed);
    }

    [Fact]
    public void BlockStats_CountRunsByNormalizedDescription()
    {
        var stats = BlockStats.ByDescription(File("casa", "casa", "e", "CASA", "casa", "casa", "v"));
        Assert.Equal(new BlockStats(2, 3), stats["casa"]);
        Assert.Equal(new BlockStats(2, 3), stats["CASA"]);
        Assert.Equal(new BlockStats(1, 1), stats["e"]);
        Assert.Equal("2 (máx. 3)", stats["casa"].ToString());
    }

    [Fact]
    public void Report_ShowsBlocksModeAndClosedLines()
    {
        var result = Run(File("casa", "casa", "casa", "tn", "tn"));
        result.SourceDescription = "muestra.csv (5 puntos, orden PNEZD)";
        var summary = ReportFormatter.Summary(result);
        Assert.Contains("--- PLANIMETRIACSV (por bloques): resumen ---", summary);
        Assert.Contains("Origen: muestra.csv (5 puntos, orden PNEZD)", summary);
        Assert.Contains("Líneas creadas: 2 (5 puntos unidos), 1 cerrada(s)", summary);

        var text = ReportFormatter.FullText(result, "dibujo.dwg", new System.DateTime(2026, 1, 1));
        Assert.Contains("PLANIMETRIACSV", text);
        Assert.Contains("PL-CASA  CASA  cerrada  100 → 102  (3 puntos)", text);
        Assert.Contains("PL-TN  TN  abierta  103 → 104  (2 puntos)", text);

        var csv = ReportFormatter.Csv(result);
        Assert.Contains("LINEA;CASA;;PL-CASA;100;102;3;cerrada", csv);
        Assert.Contains("LINEA;TN;;PL-TN;103;104;2;", csv);
    }
}
