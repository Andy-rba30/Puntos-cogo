using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class BreakRulesPhase2Tests
{
    private static readonly DescriptionParser Parser = new(new Settings());

    private static ResolvedPoint R(uint n, double x, double station, string desc = "TN") =>
        new(new SurveyPoint { Number = n, X = x, Y = 0, RawDescription = desc }, "TN", Side.Left, new Projection(station, -1, 0), Parser.Parse(desc));

    private static LineGroup G(params ResolvedPoint[] points) => new("TN", Side.Left, null, points);

    [Fact]
    public void Split_StationGap_Cuts()
    {
        // Distancias cortas pero la progresiva salta 300 m (otra pasada del eje en una curva de vuelta).
        var settings = new Settings { DistMax = 0, StationGapMax = 100 };
        var cuts = new List<LineCut>();
        var sequences = BreakRules.Split(G(R(1, 0, 10), R(2, 5, 20), R(3, 10, 330), R(4, 15, 340)), settings, cuts);

        Assert.Equal(2, sequences.Count);
        var cut = Assert.Single(cuts);
        Assert.Equal(CutReason.StationGap, cut.Reason);
        Assert.Equal(2u, cut.From.Point.Number);
        Assert.Equal(3u, cut.To.Point.Number);
    }

    [Fact]
    public void Split_StationGapZero_Disabled()
    {
        var settings = new Settings { DistMax = 0, StationGapMax = 0 };
        var cuts = new List<LineCut>();
        Assert.Single(BreakRules.Split(G(R(1, 0, 10), R(2, 5, 900)), settings, cuts));
        Assert.Empty(cuts);
    }

    [Fact]
    public void Split_EndMark_CutsAfterMarkedPoint()
    {
        var settings = new Settings { DistMax = 0, StationGapMax = 0 };
        var cuts = new List<LineCut>();
        var sequences = BreakRules.Split(G(R(1, 0, 0), R(2, 5, 5, "TN F"), R(3, 10, 10), R(4, 15, 15)), settings, cuts);

        Assert.Equal(2, sequences.Count);
        Assert.Equal(new uint[] { 1, 2 }, sequences[0].Select(p => p.Point.Number));
        Assert.Equal(new uint[] { 3, 4 }, sequences[1].Select(p => p.Point.Number));
        Assert.Equal(CutReason.EndMark, Assert.Single(cuts).Reason);
    }

    [Fact]
    public void Split_StartMark_CutsBeforeMarkedPoint()
    {
        var settings = new Settings { DistMax = 0, StationGapMax = 0 };
        var cuts = new List<LineCut>();
        var sequences = BreakRules.Split(G(R(1, 0, 0), R(2, 5, 5), R(3, 10, 10, "TN INI"), R(4, 15, 15)), settings, cuts);

        Assert.Equal(2, sequences.Count);
        Assert.Equal(new uint[] { 3, 4 }, sequences[1].Select(p => p.Point.Number));
        Assert.Equal(CutReason.StartMark, Assert.Single(cuts).Reason);
    }

    [Fact]
    public void Split_MarksDisabled_NoCut()
    {
        var settings = new Settings { DistMax = 0, StationGapMax = 0, UseDescriptionMarks = false };
        var cuts = new List<LineCut>();
        Assert.Single(BreakRules.Split(G(R(1, 0, 0), R(2, 5, 5, "TN F"), R(3, 10, 10)), settings, cuts));
    }

    [Fact]
    public void Split_EndMarkOnLastPoint_NoExtraSequence()
    {
        var settings = new Settings { DistMax = 0, StationGapMax = 0 };
        var cuts = new List<LineCut>();
        var sequences = BreakRules.Split(G(R(1, 0, 0), R(2, 5, 5, "TN F")), settings, cuts);
        Assert.Single(sequences);
        Assert.Empty(cuts);
    }

    [Fact]
    public void Split_MarksTakePrecedenceOverDistanceInReason()
    {
        var settings = new Settings { DistMax = 30, StationGapMax = 0 };
        var cuts = new List<LineCut>();
        BreakRules.Split(G(R(1, 0, 0, "TN F"), R(2, 100, 100)), settings, cuts);
        Assert.Equal(CutReason.EndMark, Assert.Single(cuts).Reason);
    }

    [Fact]
    public void Grouper_SplitSeries_SeparatesSeriesIntoGroups()
    {
        var points = new[] { R(1, 0, 0, "TN1"), R(2, 5, 5, "TN2"), R(3, 10, 10, "TN1"), R(4, 15, 15, "TN") };
        var groups = LineGrouper.Group(points, PointOrder.Number, splitSeries: true);

        Assert.Equal(3, groups.Count);
        Assert.Equal(new uint[] { 4 }, groups.Single(g => g.Series == null).Points.Select(p => p.Point.Number));
        Assert.Equal(new uint[] { 1, 3 }, groups.Single(g => g.Series == 1).Points.Select(p => p.Point.Number));
        Assert.Equal(new uint[] { 2 }, groups.Single(g => g.Series == 2).Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void Grouper_SeriesDisabled_SingleGroup()
    {
        var points = new[] { R(1, 0, 0, "TN1"), R(2, 5, 5, "TN2") };
        var groups = LineGrouper.Group(points, PointOrder.Number, splitSeries: false);
        var group = Assert.Single(groups);
        Assert.Null(group.Series);
        Assert.Equal(2, group.Points.Count);
    }

    [Fact]
    public void Grouper_OrderByStation_SortsByProgressive()
    {
        var points = new[] { R(1, 0, 50), R(2, 5, 10), R(3, 10, 30) };
        var group = Assert.Single(LineGrouper.Group(points, PointOrder.Station, splitSeries: false));
        Assert.Equal(new uint[] { 2, 3, 1 }, group.Points.Select(p => p.Point.Number));
    }
}
