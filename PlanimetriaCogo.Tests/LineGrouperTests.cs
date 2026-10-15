using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class LineGrouperTests
{
    private static ResolvedPoint R(uint n, string code, Side side, string id = "") =>
        new(new SurveyPoint { Number = n, SourceId = id }, code, side, new Projection(n, side == Side.Left ? -1 : 1, 0));

    [Fact]
    public void Group_ByCodeAndSide_OrderedByNumber()
    {
        var groups = LineGrouper.Group(new[]
        {
            R(27, "CUNETA", Side.Right), R(24, "CUNETA", Side.Left), R(26, "CUNETA", Side.Left),
            R(25, "CUNETA", Side.Right), R(10, "EJE", Side.Center), R(9, "EJE", Side.Center),
        });

        Assert.Equal(3, groups.Count);
        var left = groups.Single(g => g.Code == "CUNETA" && g.Side == Side.Left);
        Assert.Equal(new uint[] { 24, 26 }, left.Points.Select(p => p.Point.Number));
        var right = groups.Single(g => g.Code == "CUNETA" && g.Side == Side.Right);
        Assert.Equal(new uint[] { 25, 27 }, right.Points.Select(p => p.Point.Number));
        var eje = groups.Single(g => g.Code == "EJE");
        Assert.Equal(new uint[] { 9, 10 }, eje.Points.Select(p => p.Point.Number));
    }

    [Fact]
    public void Group_DuplicateNumbers_TieBreakBySourceId()
    {
        var groups = LineGrouper.Group(new[] { R(5, "TN", Side.Left, "B"), R(5, "TN", Side.Left, "A") });
        Assert.Equal(new[] { "A", "B" }, groups[0].Points.Select(p => p.Point.SourceId));
    }
}
