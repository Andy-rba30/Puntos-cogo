using PlanimetriaCogo.Core;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class DescriptionNormalizerTests
{
    [Theory]
    [InlineData("cuneta", "CUNETA")]
    [InlineData("  Cuneta  ", "CUNETA")]
    [InlineData("cuneta   izq", "CUNETA IZQ")]
    [InlineData("Árbol grande", "ARBOL GRANDE")]
    [InlineData("vía", "VIA")]
    [InlineData("ñandú", "ÑANDU")]
    [InlineData("\tTN\r\n", "TN")]
    public void Normalize_UppercasesStripsAccentsAndSpaces(string input, string expected)
    {
        Assert.Equal(expected, DescriptionNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_NullOrBlank_ReturnsEmpty()
    {
        Assert.Equal("", DescriptionNormalizer.Normalize(null));
        Assert.Equal("", DescriptionNormalizer.Normalize("   "));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("3015396", true)]
    [InlineData("12.5", true)]
    [InlineData("TN", false)]
    [InlineData("BM1", false)]
    public void IsEmptyOrNumeric(string normalized, bool expected)
    {
        Assert.Equal(expected, DescriptionNormalizer.IsEmptyOrNumeric(normalized));
    }
}
