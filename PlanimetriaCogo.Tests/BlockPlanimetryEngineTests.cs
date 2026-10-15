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

    /// <summary>Puntos en orden de archivo con coordenadas dadas (descripción, X, Y).</summary>
    private static List<SurveyPoint> At(params (string Description, double X, double Y)[] rows) =>
        rows.Select((r, i) => new SurveyPoint
        {
            Number = (uint)(100 + i), RawDescription = r.Description, X = r.X, Y = r.Y, Sequence = i + 1, SourceId = (i + 1).ToString()
        }).ToList();

    /// <summary>Casa de 6 x 4 m tomada por sus cuatro esquinas: el cierre es la cuarta pared.</summary>
    private static readonly (double, double)[] House = { (0, 0), (6, 0), (6, 4), (0, 4) };

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
        var points = At(("casa", 0, 0), ("casa", 6, 0), ("casa", 6, 4), ("casa", 0, 4), ("e", 20, 0), ("casa", 30, 0), ("casa", 31, 0));
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
    public void EveryCode_FormsBlocks_EvenCatalogPointObjects()
    {
        // Único criterio: el archivo. "Pt" es objeto puntual en el catálogo, pero en el modo por bloques forma bloques.
        var rules = ClassificationProposer.BuildBlockRules(File("Pt", "Pt"), CodeCatalog.Default, new Settings());
        Assert.Equal(CodeKind.Line, Assert.Single(rules).Kind);
        Assert.Equal(new[] { "PT", "Q", "PILON" }, Run(File("Pt", "Pt", "Q", "Q", "PilonA", "PilonA")).Lines.Select(l => l.Code));
    }

    [Fact]
    public void PointKind_IsCountedNotJoined_AndUnresolvedReported()
    {
        var result = Run(File("Pt", "Pt", "zzz", "zzz"), edit: rules =>
        {
            var pt = rules.Single(r => r.Original == "Pt");
            pt.Kind = CodeKind.Point;
            pt.LayerBase = LayerNames.ProposeBase(pt, new Settings());
        });
        Assert.Empty(result.Lines);
        Assert.Equal(2, result.PointObjects.Count);
        Assert.Equal("PT-PT", result.PointObjects[0].Layer);
        Assert.Equal(2, result.UnresolvedCodes["zzz"]);
    }

    [Fact]
    public void DistMax_DoesNotCutABlock_LongJumpIsReported()
    {
        var points = At(("tn", 0, 0), ("tn", 1, 0), ("tn", 80, 0), ("tn", 81, 0));
        var result = Run(points, new Settings { DistMax = 30 });

        // El bloque del archivo es el elemento: no se corta por distancia.
        var line = Assert.Single(result.Lines);
        Assert.Equal(4, line.Points.Count);
        Assert.Empty(result.Cuts);
        var jump = Assert.Single(result.LongJumps);
        Assert.Equal(101u, jump.From.Point.Number);
        Assert.Equal(102u, jump.To.Point.Number);
        Assert.Contains("Tramos largos dentro de un bloque (se dibujan, revíselos): 1", ReportFormatter.Summary(result));
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
    public void AxisCode_IsNotJoinedAcrossBlocks_UnlessContinuousIsChosen()
    {
        var points = File("v", "e", "v", "v", "e", "v", "e");
        // Cada bloque es un elemento separado: los "e" sueltos no se unen entre sí.
        var byBlocks = Run(points);
        Assert.DoesNotContain(byBlocks.Lines, l => l.Code == "EJE");
        Assert.Equal(BlockJoin.Block, ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, new Settings()).Single(r => r.Original == "e").BlockJoin);

        // "Continua" se elige a mano en la ventana.
        var result = Run(points, edit: rules => rules.Single(r => r.Original == "e").BlockJoin = BlockJoin.Continuous);
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
    public void ClosedBlockCodes_FromSettings_CloseOnlyRealPolygons()
    {
        var settings = new Settings { ClosedBlockCodes = new List<string> { "casa", "muro" } };
        var points = At(("Muro", 0, 0), ("Muro", 6, 0), ("Muro", 6, 4), ("Muro", 0, 4), ("tn", 50, 50), ("Muro", 10, 0), ("Muro", 12, 0), ("Muro", 12, 3));
        var result = Run(points, settings);
        Assert.True(result.Lines[0].Closed);
        Assert.False(result.Lines[1].Closed); // tres puntos: dos paredes, el cierre sería una diagonal
        Assert.Equal(1, result.ClosedBlocksLeftOpen);
    }

    [Fact]
    public void FormsPolygon_ClosesRectangles_NotFacadesOrLShapes()
    {
        Assert.True(BlockPlanimetryEngine.FormsPolygon(House));
        // Fachada tomada desde la calle: cuatro puntos casi alineados.
        Assert.False(BlockPlanimetryEngine.FormsPolygon(new (double, double)[] { (0, 0), (2, 0.2), (5, 0.1), (9, 0.3) }));
        // Fachada y costado ("L") con un punto más: el cierre sería una diagonal aguda.
        Assert.False(BlockPlanimetryEngine.FormsPolygon(new (double, double)[] { (0, 0), (0.5, 0.5), (7.5, -1), (7.7, -7) }));
        // Tres puntos nunca se cierran.
        Assert.False(BlockPlanimetryEngine.FormsPolygon(new (double, double)[] { (0, 0), (6, 0), (6, 4) }));
    }

    [Fact]
    public void BlockMark_StartsANewBlock_AndIsItsFirstVertex()
    {
        // Como en el archivo de campo: "casa B" abre la casa y las filas "casa" siguientes son sus esquinas.
        var points = At(("ver B", 0, 10), ("ver", 1, 10), ("casa B", 0, 0), ("casa", 6, 0), ("casa", 6, 4), ("casa", 0, 4),
                        ("casa B", 10, 0), ("casa", 12, 0), ("tn", 20, 20), ("casa B", 30, 0));
        var result = Run(points);

        var houses = result.Lines.Where(l => l.Code == "CASA").ToList();
        Assert.Equal(2, houses.Count);
        Assert.Equal(new uint[] { 102, 103, 104, 105 }, houses[0].Points.Select(p => p.Point.Number));
        Assert.True(houses[0].Closed);
        Assert.Equal(new uint[] { 106, 107 }, houses[1].Points.Select(p => p.Point.Number));
        Assert.Equal(new uint[] { 100, 101 }, Assert.Single(result.Lines, l => l.Code == "VEREDA").Points.Select(p => p.Point.Number));
        // El "tn" y la última "casa B" son bloques de un punto; la marca no es un corte.
        Assert.Equal(new uint[] { 108, 109 }, result.Discarded.Select(d => d.Point.Number));
        Assert.Empty(result.Cuts);
    }

    [Fact]
    public void BlockMark_SeparatesConsecutiveBlocksOfTheSameCode()
    {
        var result = Run(File("cuneta B", "cuneta", "cuneta", "cuneta B", "cuneta", "cuneta B", "cuneta"));
        Assert.Equal(new[] { 3, 2, 2 }, result.Lines.Select(l => l.Points.Count));
        Assert.All(result.Lines, l => Assert.Equal("cuneta B", l.Points[0].Point.RawDescription));
        Assert.All(result.Lines, l => Assert.Equal("PL-CUNETA", l.Layer));
    }

    [Fact]
    public void BlockMark_IsNotACutInAContinuousLine()
    {
        var result = Run(File("e B", "v", "e B", "v", "e B"), edit: rules => rules.Single(r => r.Original == "e").BlockJoin = BlockJoin.Continuous);
        Assert.Equal(3, Assert.Single(result.Lines, l => l.Code == "EJE").Points.Count);
    }

    [Fact]
    public void EveryCode_WithBlockMarks_KeepsItsFirstPoint()
    {
        // Todos los códigos del archivo de muestra que llevan "B", incluidos los que el catálogo tiene como objeto puntual.
        var codes = new[] { "cuneta", "casa", "ver", "CNT", "crr", "Q", "PilonA", "Muro", "LOSA", "ALC", "SH", "tubo", "agua", "BZ", "cumeta", "cunetq", "Crr", "e", "b", "EJE" };
        var descriptions = codes.SelectMany(c => new[] { c + " B", c, c, "tn" }).ToArray();
        var settings = new Settings();
        foreach (var useNames in new[] { false, true })
        {
            var rules = ClassificationProposer.BuildBlockRules(File(descriptions), CodeCatalog.Default, settings);
            var vm = new UI.ClassificationViewModel(rules, CodeCatalog.Default, settings) { BlockMode = true };
            if (useNames)
                vm.UseNamesAsCodes();
            Assert.Equal(codes.Length + 1, vm.Rows.Count); // una fila por código (la "B" no crea filas) más tn
            var result = new BlockPlanimetryEngine(settings).Run(File(descriptions), vm.ToRules());

            var drawn = result.Lines.Where(l => l.Points[0].Point.RawDescription != "tn").ToList();
            Assert.Equal(codes.Length, drawn.Count);
            for (var i = 0; i < codes.Length; i++)
            {
                Assert.Equal(new[] { codes[i] + " B", codes[i], codes[i] }, drawn[i].Points.Select(p => p.Point.RawDescription));
                if (useNames)
                    Assert.Equal(codes[i], drawn[i].Code);
            }
        }
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
    public void BlockStats_BlockMarkOpensABlock_AndHasNoRowOfItsOwn()
    {
        var stats = BlockStats.ByDescription(File("casa B", "casa", "casa", "casa B", "casa", "tn", "casa B"));
        Assert.Equal(new BlockStats(3, 3), stats["casa"]);
        Assert.False(stats.ContainsKey("casa B"));
    }

    [Fact]
    public void Report_ShowsBlocksModeAndClosedLines()
    {
        var result = Run(At(("casa", 0, 0), ("casa", 6, 0), ("casa", 6, 4), ("casa", 0, 4), ("tn", 10, 0), ("tn", 11, 0)));
        result.SourceDescription = "muestra.csv (6 puntos, orden PNEZD)";
        var summary = ReportFormatter.Summary(result);
        Assert.Contains("--- PLANIMETRIACSV (por bloques): resumen ---", summary);
        Assert.Contains("Origen: muestra.csv (6 puntos, orden PNEZD)", summary);
        Assert.Contains("Líneas creadas: 2 (6 puntos unidos), 1 cerrada(s)", summary);

        var text = ReportFormatter.FullText(result, "dibujo.dwg", new System.DateTime(2026, 1, 1));
        Assert.Contains("PLANIMETRIACSV", text);
        Assert.Contains("PL-CASA  CASA  cerrada  100 → 103  (4 puntos)", text);
        Assert.Contains("PL-TN  TN  abierta  104 → 105  (2 puntos)", text);

        var csv = ReportFormatter.Csv(result);
        Assert.Contains("LINEA;CASA;;PL-CASA;100;103;4;cerrada", csv);
        Assert.Contains("LINEA;TN;;PL-TN;104;105;2;", csv);
    }

    [Fact]
    public void Report_CountsClosedBlocksLeftOpen()
    {
        var result = Run(At(("casa", 0, 0), ("casa", 3, 0.1), ("casa", 6, 0), ("casa", 9, 0.2)));
        Assert.False(Assert.Single(result.Lines).Closed);
        Assert.Contains("  Bloques cerrados que quedaron abiertos (fachada o ángulo, no encierran un polígono): 1", ReportFormatter.Summary(result));
    }
}
