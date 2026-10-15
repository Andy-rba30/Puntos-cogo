using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class CodeCatalogTests
{
    [Theory]
    [InlineData("cumeta")]
    [InlineData("CNT")]
    [InlineData("cuneta")]
    [InlineData("Cuneta")]
    [InlineData(" CUNETA ")]
    public void Resolve_CunetaVariants_ToCuneta(string description)
    {
        Assert.Equal("CUNETA", CodeCatalog.Default.Resolve(description));
    }

    [Theory]
    [InlineData("v", "VEREDA")]
    [InlineData("VRD", "VEREDA")]
    [InlineData("b", "BORDE")]
    [InlineData("e", "EJE")]
    [InlineData("crr", "CARRETERA")]
    [InlineData("tn", "TN")]
    [InlineData("PilonA", "PILON")]
    public void Resolve_Aliases(string description, string expected)
    {
        Assert.Equal(expected, CodeCatalog.Default.Resolve(description));
    }

    [Fact]
    public void Resolve_Unknown_ReturnsNull()
    {
        Assert.Null(CodeCatalog.Default.Resolve("POSTE"));
        Assert.Null(CodeCatalog.Default.Resolve(""));
        Assert.Null(CodeCatalog.Default.Resolve(null));
    }

    [Fact]
    public void Eje_IsLineWithoutSides()
    {
        var entry = CodeCatalog.Default.Find("EJE")!;
        Assert.Equal(CodeKind.Line, entry.Kind);
        Assert.False(entry.SplitBySide);
    }

    [Theory]
    [InlineData("casa", true)]
    [InlineData("Muro", true)]
    [InlineData("BM", true)]
    [InlineData("tubo", true)]
    [InlineData("cuneta", false)]
    [InlineData("desconocido", false)]
    public void IsPointObject(string description, bool expected)
    {
        Assert.Equal(expected, CodeCatalog.Default.IsPointObject(description));
    }
}
