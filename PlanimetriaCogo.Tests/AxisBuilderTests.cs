using System;
using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class AxisBuilderTests
{
    private static SurveyPoint P(uint n, string desc, double x, double y) =>
        new() { Number = n, RawDescription = desc, X = x, Y = y, SourceId = n.ToString() };

    [Fact]
    public void FromPoints_UsesAxisCodePointsOrderedByNumber()
    {
        var settings = new Settings();
        // Patrón v, e, v (§9.1): solo los "e" (y "EJE", "CL") forman el eje, en orden de número.
        var points = new[]
        {
            P(1, "v", 0, 5), P(2, "e", 0, 0), P(3, "v", 0, -5),
            P(6, "EJE", 100, 0), P(4, "v", 50, 5), P(5, "CL", 50, 0), P(7, "v", 100, -5),
        };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);

        var built = AxisBuilder.FromPoints(points, rules, settings);

        Assert.Equal(new uint[] { 2, 5, 6 }, built.Points.Select(p => p.Number));
        Assert.Equal(100.0, built.Length, 6);
        Assert.Equal(0, built.DroppedDuplicates);

        // Un punto "v" a Y=+5 queda a la izquierda del eje que avanza hacia +X.
        var projection = built.Axis.ProjectAll(50, 5).First();
        Assert.True(projection.Offset < 0);
        Assert.Equal(50.0, projection.Station, 6);
    }

    [Fact]
    public void Build_DropsConsecutiveDuplicates()
    {
        var settings = new Settings();
        var built = AxisBuilder.Build(new[] { P(1, "e", 0, 0), P(2, "e", 0, 0), P(3, "e", 10, 0) }, settings);
        Assert.Equal(2, built.Points.Count);
        Assert.Equal(1, built.DroppedDuplicates);
    }

    [Fact]
    public void Build_FewerThanTwoPoints_Throws()
    {
        var settings = new Settings();
        var ex = Assert.Throws<InvalidOperationException>(() => AxisBuilder.Build(new[] { P(1, "e", 0, 0) }, settings));
        Assert.Contains("EJE", ex.Message);
    }

    [Fact]
    public void FromPoints_HonoursCustomAxisCode()
    {
        var settings = new Settings { AxisCode = "CARRETERA" };
        var points = new[] { P(1, "crr", 0, 0), P(2, "e", 5, 5), P(3, "CRC", 10, 0) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);

        var built = AxisBuilder.FromPoints(points, rules, settings);
        Assert.Equal(new uint[] { 1, 3 }, built.Points.Select(p => p.Number));
    }

    [Fact]
    public void FromPoints_IncludesIgnoredRowsWithAxisDestination()
    {
        // El usuario puede marcar EJE como "Ignorar" (no dibujar la línea) y aun así usarlo como eje.
        var settings = new Settings();
        var points = new[] { P(1, "e", 0, 0), P(2, "e", 10, 0) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);
        rules[0].Kind = CodeKind.Ignore;

        var built = AxisBuilder.FromPoints(points, rules, settings);
        Assert.Equal(2, built.Points.Count);
    }
}
