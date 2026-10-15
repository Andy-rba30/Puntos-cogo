using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class DescriptionParserTests
{
    private static DescriptionParser Parser => new(new Settings());

    [Theory]
    [InlineData("CUNETA", "CUNETA")]
    [InlineData("cumeta", "CUMETA")]
    [InlineData("  Árbol  grande ", "ARBOL GRANDE")]
    public void Parse_PlainDescription_IsBaseCodeWithoutMarks(string raw, string expected)
    {
        var parsed = Parser.Parse(raw);
        Assert.Equal(expected, parsed.BaseCode);
        Assert.Null(parsed.ExplicitSide);
        Assert.False(parsed.IsStart);
        Assert.False(parsed.IsEnd);
        Assert.Null(parsed.Series);
    }

    [Theory]
    [InlineData("CUNETA I", Side.Left)]
    [InlineData("cuneta izq", Side.Left)]
    [InlineData("CUNETA D", Side.Right)]
    [InlineData("CUNETA DER", Side.Right)]
    public void Parse_SideMark_SetsExplicitSide(string raw, Side expected)
    {
        var parsed = Parser.Parse(raw);
        Assert.Equal("CUNETA", parsed.BaseCode);
        Assert.Equal(expected, parsed.ExplicitSide);
    }

    [Theory]
    [InlineData("CUNETA F")]
    [InlineData("cuneta fin")]
    public void Parse_EndMark_SetsIsEnd(string raw)
    {
        var parsed = Parser.Parse(raw);
        Assert.Equal("CUNETA", parsed.BaseCode);
        Assert.True(parsed.IsEnd);
        Assert.False(parsed.IsStart);
    }

    [Theory]
    [InlineData("CUNETA INI")]
    [InlineData("CUNETA ..")]
    [InlineData("CUNETA..")]
    public void Parse_StartMark_SetsIsStart(string raw)
    {
        var parsed = Parser.Parse(raw);
        Assert.Equal("CUNETA", parsed.BaseCode);
        Assert.True(parsed.IsStart);
    }

    [Theory]
    [InlineData("CUNETA2", 2)]
    [InlineData("cuneta 12", 12)]
    [InlineData("TN1", 1)]
    public void Parse_NumericSuffix_IsSeries(string raw, int series)
    {
        var parsed = Parser.Parse(raw);
        Assert.Equal(raw.StartsWith("TN") ? "TN" : "CUNETA", parsed.BaseCode);
        Assert.Equal(series, parsed.Series);
    }

    [Fact]
    public void Parse_OneLetterPlusDigit_IsNotSeries()
    {
        // "P1" podría ser un código propio: con base de una sola letra no se separa la serie.
        var parsed = Parser.Parse("P1");
        Assert.Equal("P1", parsed.BaseCode);
        Assert.Null(parsed.Series);
    }

    [Fact]
    public void Parse_CombinedMarks()
    {
        var parsed = Parser.Parse("cuneta2 i f");
        Assert.Equal("CUNETA", parsed.BaseCode);
        Assert.Equal(2, parsed.Series);
        Assert.Equal(Side.Left, parsed.ExplicitSide);
        Assert.True(parsed.IsEnd);
    }

    [Fact]
    public void Parse_UnknownSecondToken_StaysInBaseCode()
    {
        var parsed = Parser.Parse("CUNETA ROTA");
        Assert.Equal("CUNETA ROTA", parsed.BaseCode);
        Assert.False(parsed.HasMarks);
    }

    [Fact]
    public void Parse_NumericOrEmpty_IsUnchanged()
    {
        Assert.Equal("3015396", Parser.Parse("3015396").BaseCode);
        Assert.Equal("", Parser.Parse("   ").BaseCode);
    }

    [Fact]
    public void Parse_MarksDisabled_OnlySeriesIsSeparated()
    {
        var parser = new DescriptionParser(new Settings { UseDescriptionMarks = false });
        var parsed = parser.Parse("CUNETA2 F");
        Assert.Equal("CUNETA F", parsed.BaseCode);
        Assert.Equal(2, parsed.Series);
        Assert.False(parsed.IsEnd);
    }

    [Fact]
    public void None_DoesNotInterpretAnything()
    {
        var parsed = DescriptionParser.None.Parse("cuneta2 f");
        Assert.Equal("CUNETA2 F", parsed.BaseCode);
        Assert.Null(parsed.Series);
    }

    [Theory]
    [InlineData("cuneta f", "CUNETA", "CUNETA F")]
    [InlineData("cumeta2 i", "CUNETA", "CUNETA2 I")]
    [InlineData("cuneta..", "CUNETA", "CUNETA..")]
    [InlineData("v", "VEREDA", "VEREDA")]
    public void Rebuild_ReplacesBaseAndKeepsMarks(string raw, string newBase, string expected)
    {
        var parsed = Parser.Parse(raw);
        Assert.Equal(expected, DescriptionParser.Rebuild(parsed, newBase));
    }

    [Fact]
    public void Settings_CustomMarks_AreHonoured()
    {
        var settings = new Settings();
        settings.EndMarks.Add("END");
        var parsed = new DescriptionParser(settings).Parse("TN END");
        Assert.True(parsed.IsEnd);
        Assert.Equal("TN", parsed.BaseCode);
    }
}
