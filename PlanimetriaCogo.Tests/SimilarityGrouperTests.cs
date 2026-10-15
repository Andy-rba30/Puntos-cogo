using System.Linq;
using PlanimetriaCogo.Core;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class SimilarityGrouperTests
{
    private static readonly string[] CunetaAliases =
    {
        "cuneta", "Cuneta", "CNT", "cumeta", "cuenta", "cunet", "cunetq", "cunera", "cunetw", "cumet", "vunet", "vuneta"
    };

    [Fact]
    public void Suggest_CunetaAliasesFromSection92_AllGoToCuneta()
    {
        var entries = CunetaAliases.Select(a => (a, 1)).ToList();
        entries[0] = ("cuneta", 500);

        var groups = new SimilarityGrouper().Suggest(entries, CodeCatalog.Default);
        var map = SimilarityGrouper.ToMap(groups);

        foreach (var alias in CunetaAliases)
            Assert.Equal("CUNETA", map[alias]);
        Assert.Single(groups);
    }

    [Fact]
    public void Suggest_WithoutCatalog_ClustersTyposAroundMostFrequent()
    {
        // Sin catálogo: la similitud sola debe reunir los errores de tipeo alrededor de "cuneta".
        var entries = CunetaAliases.Where(a => a != "CNT").Select(a => (a, 1)).ToList();
        entries[0] = ("cuneta", 500);

        var map = SimilarityGrouper.ToMap(new SimilarityGrouper().Suggest(entries, CodeCatalog.Empty));

        foreach (var alias in CunetaAliases.Where(a => a != "CNT"))
            Assert.Equal("CUNETA", map[alias]);
    }

    [Fact]
    public void Suggest_PrefixChain_VToVerToVereda()
    {
        var entries = new[] { ("v", 300), ("ver", 20), ("vereda", 40) };

        var map = SimilarityGrouper.ToMap(new SimilarityGrouper().Suggest(entries, CodeCatalog.Empty));

        Assert.Equal("VEREDA", map["v"]);
        Assert.Equal("VEREDA", map["ver"]);
        Assert.Equal("VEREDA", map["vereda"]);
    }

    [Fact]
    public void Suggest_ShortCodesDoNotMergeWithEachOther()
    {
        // "v", "b", "e" están a distancia 1 entre sí: no deben unificarse.
        var entries = new[] { ("v", 10), ("b", 10), ("e", 10) };

        var groups = new SimilarityGrouper().Suggest(entries, CodeCatalog.Empty);

        Assert.Empty(groups); // una sola letra sin referencia queda sin resolver
    }

    [Fact]
    public void Suggest_AmbiguousPrefixStaysUnresolved()
    {
        // "c" es prefijo de CUNETA, CARRETERA y CASA: no se adivina.
        var map = SimilarityGrouper.ToMap(new SimilarityGrouper().Suggest(new[] { ("c", 5) }, CodeCatalog.Default));
        Assert.False(map.ContainsKey("c"));
    }

    [Fact]
    public void Suggest_SingleUnknownCode_ProposesItself()
    {
        var map = SimilarityGrouper.ToMap(new SimilarityGrouper().Suggest(new[] { ("poste", 12) }, CodeCatalog.Default));
        Assert.Equal("POSTE", map["poste"]);
    }

    [Fact]
    public void Suggest_EmptyAndNumeric_AreNotSuggested()
    {
        var map = SimilarityGrouper.ToMap(new SimilarityGrouper().Suggest(new[] { ("", 126), ("3015396", 1) }, CodeCatalog.Default));
        Assert.Empty(map);
    }

    [Fact]
    public void Suggest_CaseVariants_MergeWithCatalogCode()
    {
        var map = SimilarityGrouper.ToMap(new SimilarityGrouper().Suggest(new[] { ("tn", 6725), ("TN", 1287), ("t", 3), ("tm", 2) }, CodeCatalog.Default));
        Assert.Equal("TN", map["tn"]);
        Assert.Equal("TN", map["TN"]);
        Assert.Equal("TN", map["t"]);
        Assert.Equal("TN", map["tm"]);
    }

    [Fact]
    public void Suggest_TypoOfKnownCode_ResolvesByEditDistance()
    {
        var map = SimilarityGrouper.ToMap(new SimilarityGrouper().Suggest(new[] { ("bordee", 3), ("mur0", 1) }, CodeCatalog.Default));
        Assert.Equal("BORDE", map["bordee"]);
        Assert.Equal("MURO", map["mur0"]);
    }
}
