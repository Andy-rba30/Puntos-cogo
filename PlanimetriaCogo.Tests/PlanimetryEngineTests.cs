using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class PlanimetryEngineTests
{
    private static SurveyPoint P(uint n, string desc, double x, double y, double z = 0) =>
        new() { Number = n, RawDescription = desc, X = x, Y = y, Z = z, SourceId = n.ToString() };

    private static Settings DefaultSettings => new();

    [Fact]
    public void Section36Example_TwoCunetaLinesOnePerSide()
    {
        // 24 → offset −4.1 (Izq), 25 → +3.9 (Der), 26 → −4.0 (Izq), 27 → +4.2 (Der).
        // Eje hacia +X: izquierda = Y positivo.
        var points = new[]
        {
            P(24, "CUNETA", 10, 4.1), P(25, "CUNETA", 12, -3.9), P(26, "CUNETA", 20, 4.0), P(27, "CUNETA", 22, -4.2),
        };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Equal(2, result.Lines.Count);
        Assert.Empty(result.Discarded);

        var left = result.Lines.Single(l => l.Side == Side.Left);
        Assert.Equal("PL-CUNETA-IZQ", left.Layer);
        Assert.Equal(new uint[] { 24, 26 }, left.Points.Select(p => p.Point.Number));

        var right = result.Lines.Single(l => l.Side == Side.Right);
        Assert.Equal("PL-CUNETA-DER", right.Layer);
        Assert.Equal(new uint[] { 25, 27 }, right.Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void MixedDescriptions_UnifiedByRules_GoToSameGroup()
    {
        var points = new[] { P(1, "cumeta", 10, 4), P(2, "CNT", 20, 4), P(3, "Cuneta", 30, 4) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        var line = Assert.Single(result.Lines);
        Assert.Equal("CUNETA", line.Code);
        Assert.Equal(new uint[] { 1, 2, 3 }, line.Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void CodeWithoutSides_FormsSingleGroupAcrossBothSides()
    {
        var points = new[] { P(1, "e", 10, 0.05), P(2, "e", 20, -0.05), P(3, "e", 30, 0.0) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        var line = Assert.Single(result.Lines);
        Assert.Equal("EJE", line.Code);
        Assert.Equal(Side.Center, line.Side);
        Assert.Equal("PL-EJE", line.Layer);
        Assert.Equal(3, line.Points.Count);
    }

    [Fact]
    public void IgnoredUnresolvedAndPointObjects_AreCountedNotDrawn()
    {
        var points = new[]
        {
            P(1, "casa", 10, 5), P(2, "casa", 12, 5), P(3, "", 14, 5), P(4, "poste", 16, 5), P(5, "TN", 18, 5), P(6, "TN", 20, 5),
        };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Single(result.Lines);
        Assert.Equal(2, result.PointObjectCodes["CASA"]);
        Assert.Equal(1, result.IgnoredCodes[""]);
        Assert.Equal(1, result.UnresolvedCodes["poste"]);
        Assert.Equal(6, result.TotalPoints);
    }

    [Fact]
    public void RuleWithDrawUnchecked_IsNotDrawn_AndCountedAsIgnored()
    {
        var points = new[]
        {
            P(1, "casa", 10, 5), P(2, "casa", 12, 5), P(3, "TN", 14, 5), P(4, "TN", 16, 5), P(5, "TN", 18, 5),
        };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);
        rules.Single(r => r.Original == "TN").Draw = false;
        rules.Single(r => r.Original == "casa").Draw = false;

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Empty(result.Lines);
        Assert.Empty(result.PointObjects);
        Assert.Empty(result.DescriptionChanges);
        Assert.Equal(3, result.IgnoredCodes["TN"]);
        Assert.Equal(2, result.IgnoredCodes["casa"]);
        Assert.Equal(5, result.TotalPoints);
    }

    [Fact]
    public void PointsBeyondOffsetMaxOrOutsideAxis_AreDiscardedWithReason()
    {
        var points = new[] { P(1, "TN", 10, 5), P(2, "TN", 20, 45), P(3, "TN", -10, 5), P(4, "TN", 30, 5) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        var line = Assert.Single(result.Lines);
        Assert.Equal(new uint[] { 1, 4 }, line.Points.Select(p => p.Point.Number));
        Assert.Equal(DiscardReason.BeyondOffsetMax, result.Discarded.Single(d => d.Point.Number == 2).Reason);
        Assert.Equal(DiscardReason.NoProjection, result.Discarded.Single(d => d.Point.Number == 3).Reason);
    }

    [Fact]
    public void SplitCodeOnAxis_IsDiscardedAsUndeterminedSide()
    {
        var points = new[] { P(1, "TN", 10, 5), P(2, "TN", 20, 0.05), P(3, "TN", 30, 5) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Equal(DiscardReason.OnAxis, Assert.Single(result.Discarded).Reason);
        Assert.Equal(new uint[] { 1, 3 }, Assert.Single(result.Lines).Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void SinglePointGroup_IsReportedNotDrawn()
    {
        var points = new[] { P(1, "TN", 10, 5), P(2, "TN", 20, 5), P(3, "TN", 25, -5) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Single(result.Lines);
        var alone = Assert.Single(result.Discarded);
        Assert.Equal(3u, alone.Point.Number);
        Assert.Equal(DiscardReason.Alone, alone.Reason);
    }

    [Fact]
    public void DistMax_SplitsGroupIntoTwoLinesAndReportsCut()
    {
        var points = new[] { P(1, "TN", 10, 5), P(2, "TN", 20, 5), P(3, "TN", 70, 5), P(4, "TN", 80, 5) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Equal(2, result.Lines.Count);
        var cut = Assert.Single(result.Cuts);
        Assert.Equal(2u, cut.From.Point.Number);
        Assert.Equal(3u, cut.To.Point.Number);
    }

    [Fact]
    public void Hairpin_ContinuityKeepsPointOnOutgoingPass()
    {
        var axis = TestAxes.Hairpin();
        // Cuneta izquierda del tramo de ida; el punto 2 está más cerca del tramo de regreso.
        var points = new[] { P(1, "CUNETA", 40, 3), P(2, "CUNETA", 50, 12), P(3, "CUNETA", 60, 3) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, axis);

        var line = Assert.Single(result.Lines);
        Assert.Equal(Side.Left, line.Side);
        Assert.Equal(new uint[] { 1, 2, 3 }, line.Points.Select(p => p.Point.Number));
        Assert.All(line.Points, p => Assert.True(p.Projection.Station < 100));
    }

    [Fact]
    public void UserChangedKindToIgnore_PointsAreIgnored()
    {
        var points = new[] { P(1, "TN", 10, 5), P(2, "TN", 20, 5) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);
        rules[0].Kind = CodeKind.Ignore;

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Empty(result.Lines);
        Assert.Equal(2, result.IgnoredCodes["TN"]);
    }

    [Fact]
    public void EmptyLayerBase_FallsBackToProposedLayer()
    {
        var points = new[] { P(1, "TN", 10, 5), P(2, "TN", 20, 5) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, DefaultSettings);
        rules[0].LayerBase = "";

        var result = new PlanimetryEngine(DefaultSettings).Run(points, rules, TestAxes.StraightX());

        Assert.Equal("PL-TN-IZQ", Assert.Single(result.Lines).Layer);
    }
}
