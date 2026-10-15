using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class SideClassifierTests
{
    [Theory]
    [InlineData(-4.1, Side.Left)]
    [InlineData(3.9, Side.Right)]
    [InlineData(0.05, Side.Center)]
    [InlineData(-0.1, Side.Center)]
    [InlineData(-0.15, Side.Left)]
    public void Classify(double offset, Side expected)
    {
        Assert.Equal(expected, SideClassifier.Classify(offset, 0.15));
    }
}
