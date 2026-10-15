using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class CsvPointReaderTests
{
    [Fact]
    public void Pnezd_CommaAndDotDecimals_AsInFieldSample()
    {
        var lines = new[]
        {
            "23,9472112.813,612971.2074,1444.977,tn",
            "24,9472110.847,612975.2724,1444.843,tn",
            "25,9472142.951,612992.1954,1442.052,v",
        };
        var result = CsvPointReader.Parse(lines, CsvColumnOrder.PNEZD);

        Assert.Equal(',', result.Separator);
        Assert.Equal(3, result.Points.Count);
        Assert.Equal(0, result.SkippedLines);
        var first = result.Points[0];
        Assert.Equal(23u, first.Number);
        Assert.Equal(612971.2074, first.X, 6); // este
        Assert.Equal(9472112.813, first.Y, 6); // norte
        Assert.Equal(1444.977, first.Z, 6);
        Assert.Equal("tn", first.RawDescription);
        Assert.Equal(new[] { 1, 2, 3 }, result.Points.Select(p => p.Sequence));
        Assert.Equal("0000001", first.SourceId);
    }

    [Fact]
    public void Penzd_SwapsNorthAndEast()
    {
        var result = CsvPointReader.Parse(new[] { "1,612971.2,9472112.8,1444.9,casa" }, CsvColumnOrder.PENZD);
        var p = Assert.Single(result.Points);
        Assert.Equal(612971.2, p.X, 6);
        Assert.Equal(9472112.8, p.Y, 6);
    }

    [Fact]
    public void HeaderAndBlankLines_AreSkippedAndReported()
    {
        var lines = new[] { "P,N,E,Z,D", "", "1,100,200,10,tn", "   ", "x,y,z,w,q" };
        var result = CsvPointReader.Parse(lines, CsvColumnOrder.PNEZD);

        Assert.Single(result.Points);
        Assert.Equal(2, result.SkippedLines);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("fila 1", result.Warnings[0]);
        Assert.Contains("fila 5", result.Warnings[1]);
    }

    [Fact]
    public void Semicolon_AllowsDecimalComma()
    {
        var lines = new[] { "1;9472112,813;612971,207;1444,977;cuneta f", "2;9472110,847;612975,272;1444,843;cuneta" };
        var result = CsvPointReader.Parse(lines, CsvColumnOrder.PNEZD);

        Assert.Equal(';', result.Separator);
        Assert.Equal("punto y coma", result.SeparatorName);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal(612971.207, result.Points[0].X, 6);
        Assert.Equal("cuneta f", result.Points[0].RawDescription);
    }

    [Fact]
    public void Tab_IsDetected()
    {
        var result = CsvPointReader.Parse(new[] { "1\t100\t200\t10\tb", "2\t101\t201\t11\tb" }, CsvColumnOrder.PNEZD);
        Assert.Equal('\t', result.Separator);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal("b", result.Points[1].RawDescription);
    }

    [Fact]
    public void QuotedDescription_KeepsSeparatorInside()
    {
        var result = CsvPointReader.Parse(new[] { "1,100,200,10,\"casa, roja\"" }, CsvColumnOrder.PNEZD);
        Assert.Equal("casa, roja", Assert.Single(result.Points).RawDescription);
    }

    [Fact]
    public void ExtraColumns_JoinIntoDescription()
    {
        var result = CsvPointReader.Parse(new[] { "1,100,200,10,cuneta,f" }, CsvColumnOrder.PNEZD);
        Assert.Equal("cuneta f", Assert.Single(result.Points).RawDescription);
    }

    [Fact]
    public void MissingDescriptionElevationOrNumber_AreHandled()
    {
        var lines = new[]
        {
            "1,100,200,10",          // sin descripción
            "2,101,201,,tn",         // sin cota
            "P3,102,202,12,tn",      // número no entero
            "4.0,103,203,13,tn",     // número exportado como decimal
            "5,104,205",             // menos de 4 columnas
        };
        var result = CsvPointReader.Parse(lines, CsvColumnOrder.PNEZD);

        Assert.Equal(4, result.Points.Count);
        Assert.Equal("", result.Points[0].RawDescription);
        Assert.Equal(0.0, result.Points[1].Z);
        Assert.Equal(1, result.LinesWithoutElevation);
        Assert.Equal(3u, result.Points[2].Number); // posición en el archivo
        Assert.Equal("P3", result.Points[2].Name);
        Assert.Equal(1, result.LinesWithoutNumber);
        Assert.Equal(4u, result.Points[3].Number);
        Assert.Equal(1, result.SkippedLines);
    }

    [Fact]
    public void DetectSeparator_PrefersSemicolonOnTie()
    {
        Assert.Equal(';', CsvPointReader.DetectSeparator(new[] { "1;2,5;3,5;4,5;a" }));
        Assert.Equal(',', CsvPointReader.DetectSeparator(new[] { "1,2,3,4,a" }));
        Assert.Equal(',', CsvPointReader.DetectSeparator(new string[0]));
    }

    [Fact]
    public void Split_HandlesEscapedQuotes()
    {
        Assert.Equal(new[] { "a", "b \"c\"", "", "d" }, CsvPointReader.Split("a,\"b \"\"c\"\"\",,d", ','));
    }
}
