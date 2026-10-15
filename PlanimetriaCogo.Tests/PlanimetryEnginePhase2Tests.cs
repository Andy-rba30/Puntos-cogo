using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class PlanimetryEnginePhase2Tests
{
    private static SurveyPoint P(uint n, string desc, double x, double y, double z = 0) =>
        new() { Number = n, RawDescription = desc, X = x, Y = y, Z = z, SourceId = n.ToString() };

    private static PlanimetryResult Run(Settings settings, params SurveyPoint[] points)
    {
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);
        return new PlanimetryEngine(settings).Run(points, rules, TestAxes.StraightX());
    }

    [Fact]
    public void DescriptionWithMark_ResolvesToBaseCode()
    {
        var rules = ClassificationProposer.BuildRules(new[] { P(1, "cuneta f", 10, 4) }, CodeCatalog.Default, new Settings());
        var rule = Assert.Single(rules);
        Assert.Equal("CUNETA", rule.DestinationCode);
        Assert.Equal(CodeKind.Line, rule.Kind);
    }

    [Fact]
    public void ExplicitSide_PrevailsOverComputed_AndIsReported()
    {
        // El punto 2 está a la derecha (Y negativo) pero dice "I": va al grupo izquierdo y se reporta.
        var result = Run(new Settings(), P(1, "CUNETA", 10, 4), P(2, "CUNETA I", 20, -4), P(3, "CUNETA", 30, 4));

        var line = Assert.Single(result.Lines);
        Assert.Equal(Side.Left, line.Side);
        Assert.Equal(new uint[] { 1, 2, 3 }, line.Points.Select(p => p.Point.Number));
        var discrepancy = Assert.Single(result.SideDiscrepancies);
        Assert.Equal(2u, discrepancy.Point.Number);
        Assert.Equal(Side.Left, discrepancy.Explicit);
        Assert.Equal(Side.Right, discrepancy.Computed);
    }

    [Fact]
    public void ExplicitSide_ResolvesPointOnAxis()
    {
        var result = Run(new Settings(), P(1, "CUNETA D", 10, 0.01), P(2, "CUNETA", 20, -4));
        var line = Assert.Single(result.Lines);
        Assert.Equal(Side.Right, line.Side);
        Assert.Empty(result.Discarded);
        Assert.Empty(result.SideDiscrepancies);
    }

    [Fact]
    public void ExplicitSide_IgnoredWhenMarksDisabled()
    {
        var settings = new Settings { UseDescriptionMarks = false };
        var points = new[] { P(1, "CUNETA", 10, 4), P(2, "CUNETA I", 20, -4) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);
        // Sin marcas, "CUNETA I" es otro código: el usuario lo unifica a mano.
        rules.Single(r => r.Original == "CUNETA I").DestinationCode = "CUNETA";
        var result = new PlanimetryEngine(settings).Run(points, rules, TestAxes.StraightX());

        Assert.Empty(result.Lines); // un punto por lado: ambos "únicos en su grupo"
        Assert.Equal(2, result.Discarded.Count(d => d.Reason == DiscardReason.Alone));
    }

    [Fact]
    public void EndMark_CutsLine_AndCutIsReported()
    {
        var result = Run(new Settings(), P(1, "TN", 10, 4), P(2, "TN F", 20, 4), P(3, "TN", 30, 4), P(4, "TN", 40, 4));
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(CutReason.EndMark, Assert.Single(result.Cuts).Reason);
    }

    [Fact]
    public void Series_SplitWhenEnabled_MergedWhenDisabled()
    {
        var points = new[] { P(1, "TN1", 10, 4), P(2, "TN2", 20, 4), P(3, "TN1", 30, 4), P(4, "TN2", 40, 4) };

        var split = Run(new Settings { SplitSeries = true }, points);
        Assert.Equal(2, split.Lines.Count);
        Assert.Equal(new uint[] { 1, 3 }, split.Lines.Single(l => l.Series == 1).Points.Select(p => p.Point.Number));
        Assert.All(split.Lines, l => Assert.Equal("PL-TN-IZQ", l.Layer));

        var merged = Run(new Settings { SplitSeries = false }, points);
        var line = Assert.Single(merged.Lines);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, line.Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void OrderByStation_ReordersWithinGroup()
    {
        var points = new[] { P(1, "TN", 30, 4), P(2, "TN", 10, 4), P(3, "TN", 20, 4) };

        var byNumber = Run(new Settings { Order = PointOrder.Number }, points);
        Assert.Equal(new uint[] { 1, 2, 3 }, Assert.Single(byNumber.Lines).Points.Select(p => p.Point.Number));

        var byStation = Run(new Settings { Order = PointOrder.Station }, points);
        Assert.Equal(new uint[] { 2, 3, 1 }, Assert.Single(byStation.Lines).Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void StationGap_CutsJumpBetweenHairpinPasses()
    {
        // Puntos 1-2 junto a la pasada A (y=0) y 3-4 junto a la pasada B (y=20), todos a la izquierda de su
        // pasada. Con offsetMax 10 cada punto tiene una sola candidata, así que la continuidad no puede
        // retenerlos en A: el salto de progresiva (~190 m) supera stationGapMax y se corta en vez de unir.
        var settings = new Settings { DistMax = 0, StationGapMax = 50, OffsetMax = 10 };
        var points = new[] { P(1, "TN", 10, 4), P(2, "TN", 20, 4), P(3, "TN", 20, 16), P(4, "TN", 10, 16) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);
        var result = new PlanimetryEngine(settings).Run(points, rules, TestAxes.Hairpin());

        var cut = Assert.Single(result.Cuts);
        Assert.Equal(CutReason.StationGap, cut.Reason);
        Assert.Equal(2u, cut.From.Point.Number);
        Assert.Equal(3u, cut.To.Point.Number);
        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.Equal(Side.Left, l.Side));
    }

    [Fact]
    public void PointObjects_AreListedWithLayer()
    {
        var result = Run(new Settings(), P(1, "casa", 10, 4), P(2, "Muro", 20, -4), P(3, "casa", 30, 4));

        Assert.Equal(3, result.PointObjects.Count);
        Assert.Equal(2, result.PointObjectCodes["CASA"]);
        Assert.All(result.PointObjects.Where(p => p.Code == "CASA"), p => Assert.Equal("PT-CASA", p.Layer));
        Assert.Equal("PT-MURO", result.PointObjects.Single(p => p.Code == "MURO").Layer);
    }

    [Fact]
    public void DescriptionChanges_OnlyForDescriptionsThatDiffer()
    {
        var result = Run(new Settings(), P(1, "cumeta", 10, 4), P(2, "CUNETA", 20, 4), P(3, "cuneta f", 30, 4), P(4, "casa", 40, 4));

        var changes = result.DescriptionChanges.OrderBy(c => c.Point.Number).ToList();
        Assert.Equal(new uint[] { 1, 3, 4 }, changes.Select(c => c.Point.Number));
        Assert.Equal("CUNETA", changes[0].NewDescription);
        Assert.Equal("CUNETA F", changes[1].NewDescription);
        Assert.Equal("CASA", changes[2].NewDescription);
    }

    [Fact]
    public void UnresolvedAndIgnored_ProduceNoDescriptionChange()
    {
        var result = Run(new Settings(), P(1, "zzz", 10, 4), P(2, "3015396", 20, 4));
        Assert.Empty(result.DescriptionChanges);
        Assert.Single(result.UnresolvedCodes);
        Assert.Single(result.IgnoredCodes);
    }
}
