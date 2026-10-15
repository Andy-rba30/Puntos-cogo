using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class ContinuityResolverTests
{
    private static SurveyPoint P(uint n, double x, double y) => new() { Number = n, X = x, Y = y, RawDescription = "CUNETA" };

    [Fact]
    public void Hairpin_NearestProjectionIsWrong_ContinuityFixesIt()
    {
        var axis = TestAxes.Hairpin();
        // El punto 2 (50,12) está a 8 m del tramo de regreso B y a 12 m del tramo de ida A.
        // La proyección más cercana (B, PK ≈181) es la incorrecta: el grupo venía avanzando por A.
        var points = new[] { P(1, 40, 3), P(2, 50, 12), P(3, 60, 3) };

        var nearest = ContinuityResolver.ChooseFirst(axis.ProjectAll(50, 12).ToList())!.Value;
        Assert.True(nearest.Station > 150, "la candidata más cercana debería ser el tramo de regreso");

        var resolutions = new ContinuityResolver(axis, offsetMax: 40).Resolve(points);

        Assert.All(resolutions, r => Assert.True(r.Ok));
        Assert.Equal(40, resolutions[0].Chosen!.Value.Station, 6);
        Assert.Equal(50, resolutions[1].Chosen!.Value.Station, 6); // corregida por continuidad
        Assert.Equal(-12, resolutions[1].Chosen!.Value.Offset, 6);
        Assert.Equal(60, resolutions[2].Chosen!.Value.Station, 6);
    }

    [Fact]
    public void FirstPoint_UsesNearestCandidate()
    {
        var axis = TestAxes.Hairpin();
        var resolutions = new ContinuityResolver(axis, 40).Resolve(new[] { P(1, 50, 12) });
        Assert.True(resolutions[0].Chosen!.Value.Station > 150);
    }

    [Fact]
    public void CandidatesBeyondOffsetMax_AreDiscardedButReportedAsHadCandidates()
    {
        var axis = TestAxes.StraightX();
        var resolutions = new ContinuityResolver(axis, offsetMax: 10).Resolve(new[] { P(1, 50, 25), P(2, -10, 0) });

        Assert.False(resolutions[0].Ok);
        Assert.True(resolutions[0].HadCandidates);   // proyecta, pero fuera de offsetMax
        Assert.False(resolutions[1].Ok);
        Assert.False(resolutions[1].HadCandidates);  // no proyecta
    }

    [Fact]
    public void UnprojectedPoint_DoesNotBreakContinuity()
    {
        var axis = TestAxes.Hairpin();
        var points = new[] { P(1, 40, 3), P(2, 50, 80), P(3, 50, 12) };
        var resolutions = new ContinuityResolver(axis, 40).Resolve(points);

        Assert.False(resolutions[1].Ok);
        Assert.Equal(50, resolutions[2].Chosen!.Value.Station, 6); // sigue comparando con el punto 1
    }
}
