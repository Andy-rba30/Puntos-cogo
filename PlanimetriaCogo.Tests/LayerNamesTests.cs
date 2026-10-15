using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class LayerNamesTests
{
    [Fact]
    public void ForLine_AddsSideSuffixOnlyWhenSplit()
    {
        var s = new Settings();
        Assert.Equal("PL-CUNETA-IZQ", LayerNames.ForLine("PL-CUNETA", Side.Left, true, s));
        Assert.Equal("PL-CUNETA-DER", LayerNames.ForLine("PL-CUNETA", Side.Right, true, s));
        Assert.Equal("PL-EJE", LayerNames.ForLine("PL-EJE", Side.Center, false, s));
    }

    [Fact]
    public void Sanitize_ReplacesForbiddenCharacters()
    {
        Assert.Equal("PL-A_B_C", LayerNames.Sanitize("PL-A/B:C"));
    }
}
