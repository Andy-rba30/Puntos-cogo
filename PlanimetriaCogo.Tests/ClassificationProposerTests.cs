using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class ClassificationProposerTests
{
    private static SurveyPoint P(uint n, string desc) => new() { Number = n, RawDescription = desc };

    [Fact]
    public void BuildRules_ProposesKindLayerAndCounts()
    {
        var points = new[] { P(1, "cumeta"), P(2, "cumeta"), P(3, "casa"), P(4, "e"), P(5, ""), P(6, "3015396"), P(7, "poste") };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, new Settings());

        var cumeta = rules.Single(r => r.Original == "cumeta");
        Assert.Equal(2, cumeta.Count);
        Assert.Equal("CUNETA", cumeta.DestinationCode);
        Assert.Equal(CodeKind.Line, cumeta.Kind);
        Assert.True(cumeta.SplitBySide);
        Assert.Equal("PL-CUNETA", cumeta.LayerBase);

        var casa = rules.Single(r => r.Original == "casa");
        Assert.Equal(CodeKind.Point, casa.Kind);
        Assert.Equal("PT-CASA", casa.LayerBase);

        var eje = rules.Single(r => r.Original == "e");
        Assert.Equal("EJE", eje.DestinationCode);
        Assert.False(eje.SplitBySide);
        Assert.Equal("PL-EJE", eje.LayerBase);

        Assert.Equal(CodeKind.Ignore, rules.Single(r => r.Original == "").Kind);
        Assert.Equal(CodeKind.Ignore, rules.Single(r => r.Original == "3015396").Kind);

        var poste = rules.Single(r => r.Original == "poste");
        Assert.False(poste.IsResolved);
        Assert.Equal(CodeKind.Line, poste.Kind);
        Assert.Equal("", poste.LayerBase);
    }

    [Fact]
    public void ApplyDestination_NormalizesAndReproposes()
    {
        var rule = new CodeRule { Original = "x", Count = 1 };
        ClassificationProposer.ApplyDestination(rule, " muro ", CodeCatalog.Default, new Settings());
        Assert.Equal("MURO", rule.DestinationCode);
        Assert.Equal(CodeKind.Point, rule.Kind);
        Assert.Equal("PT-MURO", rule.LayerBase);

        ClassificationProposer.ApplyDestination(rule, "poste", CodeCatalog.Default, new Settings());
        Assert.Equal(CodeKind.Line, rule.Kind);
        Assert.Equal("PL-POSTE", rule.LayerBase);
    }

    [Fact]
    public void ApplySuggestions_FillsUnresolvedRows()
    {
        var settings = new Settings();
        var rules = ClassificationProposer.BuildRules(new[] { P(1, "cunetaa"), P(2, "verea"), P(3, "vereda") }, CodeCatalog.Default, settings);
        Assert.False(rules.Single(r => r.Original == "cunetaa").IsResolved);

        var groups = new SimilarityGrouper().Suggest(rules.Select(r => (r.Original, r.Count)), CodeCatalog.Default);
        var changed = ClassificationProposer.ApplySuggestions(rules, groups, CodeCatalog.Default, settings);

        Assert.Equal(2, changed);
        Assert.Equal("CUNETA", rules.Single(r => r.Original == "cunetaa").DestinationCode);
        Assert.Equal("VEREDA", rules.Single(r => r.Original == "verea").DestinationCode);
    }
}
