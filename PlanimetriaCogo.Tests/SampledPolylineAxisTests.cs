using System.Linq;
using PlanimetriaCogo.Core;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class SampledPolylineAxisTests
{
    [Fact]
    public void Straight_LeftPointHasNegativeOffset()
    {
        var axis = TestAxes.StraightX();
        var p = Assert.Single(axis.ProjectAll(30, 4.1));
        Assert.Equal(30, p.Station, 6);
        Assert.Equal(-4.1, p.Offset, 6);
    }

    [Fact]
    public void Straight_RightPointHasPositiveOffset()
    {
        var axis = TestAxes.StraightX();
        var p = Assert.Single(axis.ProjectAll(30, -3.9));
        Assert.Equal(3.9, p.Offset, 6);
    }

    [Fact]
    public void Straight_PointBeforeStartOrAfterEnd_DoesNotProject()
    {
        var axis = TestAxes.StraightX();
        Assert.Empty(axis.ProjectAll(-5, 2));
        Assert.Empty(axis.ProjectAll(105, 2));
    }

    [Fact]
    public void Straight_PointBeyondSearchRadius_DoesNotProject()
    {
        var axis = TestAxes.StraightX(searchRadius: 10);
        Assert.Empty(axis.ProjectAll(50, 11));
        Assert.Single(axis.ProjectAll(50, 9));
    }

    [Fact]
    public void Hairpin_PointBetweenPasses_HasTwoCandidatesNearestFirst()
    {
        var axis = TestAxes.Hairpin();
        var candidates = axis.ProjectAll(50, 12).ToList();

        Assert.Equal(2, candidates.Count);
        // La más cercana es el tramo de regreso B (a 8 m), luego el tramo A (a 12 m).
        Assert.Equal(8, candidates[0].Distance, 6);
        Assert.True(candidates[0].Station > 150);
        Assert.Equal(12, candidates[1].Distance, 6);
        Assert.Equal(50, candidates[1].Station, 6);
        // Signos: en A (hacia +X) el punto queda a la izquierda; en B (hacia −X) también a la izquierda.
        Assert.True(candidates[1].Offset < 0);
        Assert.True(candidates[0].Offset < 0);
    }

    [Fact]
    public void Polyline_ClosestPointAtInteriorVertex_YieldsSingleCandidate()
    {
        // Codo en (50,0): el punto (50,-5) tiene su pie en el vértice; no debe duplicarse.
        var axis = SampledPolylineAxis.FromPoints(new[] { (0.0, 0.0), (50.0, 0.0), (50.0, 50.0) }, 100);
        var p = Assert.Single(axis.ProjectAll(50, -5));
        Assert.Equal(50, p.Station, 6);
        Assert.Equal(5, p.Distance, 6);
    }
}
