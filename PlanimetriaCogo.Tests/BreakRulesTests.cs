using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class BreakRulesTests
{
    private static ResolvedPoint R(uint n, double x) =>
        new(new SurveyPoint { Number = n, X = x, Y = 0 }, "TN", Side.Left, new Projection(x, -1, 0));

    [Fact]
    public void Split_CutsWhenDistanceExceedsDistMax()
    {
        var group = new LineGroup("TN", Side.Left, new[] { R(1, 0), R(2, 10), R(3, 50), R(4, 60) });
        var cuts = new List<LineCut>();

        var sequences = BreakRules.Split(group, 30, cuts);

        Assert.Equal(2, sequences.Count);
        Assert.Equal(new uint[] { 1, 2 }, sequences[0].Select(p => p.Point.Number));
        Assert.Equal(new uint[] { 3, 4 }, sequences[1].Select(p => p.Point.Number));
        var cut = Assert.Single(cuts);
        Assert.Equal(2u, cut.From.Point.Number);
        Assert.Equal(3u, cut.To.Point.Number);
    }

    [Fact]
    public void Split_DistMaxZero_NeverCuts()
    {
        var group = new LineGroup("TN", Side.Left, new[] { R(1, 0), R(2, 500) });
        var cuts = new List<LineCut>();
        Assert.Single(BreakRules.Split(group, 0, cuts));
        Assert.Empty(cuts);
    }
}
