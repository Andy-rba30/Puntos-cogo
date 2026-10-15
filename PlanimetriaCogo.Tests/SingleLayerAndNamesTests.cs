using System.Linq;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using PlanimetriaCogo.UI;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class SingleLayerAndNamesTests
{
    private static SurveyPoint P(uint n, string desc, double x, double y) =>
        new() { Number = n, RawDescription = desc, X = x, Y = y, Sequence = (int)n, SourceId = n.ToString() };

    [Fact]
    public void SingleLayer_AxisMode_AllLinesGoToOneLayer_KeepingRowColor()
    {
        var settings = new Settings { SingleLayer = true, SingleLayerName = "Planimetría" };
        var points = new[] { P(1, "CUNETA", 10, 4), P(2, "CUNETA", 20, 4), P(3, "v", 10, -4), P(4, "v", 20, -4) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);

        var result = new PlanimetryEngine(settings).Run(points, rules, TestAxes.StraightX());

        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.Equal("Planimetría", l.Layer));
        Assert.Equal(4, result.Lines.Single(l => l.Code == "CUNETA").ColorIndex);
        Assert.Equal(3, result.Lines.Single(l => l.Code == "VEREDA").ColorIndex);
    }

    [Fact]
    public void SingleLayer_BlockMode_SanitizesName_AndPointObjectsKeepTheirLayer()
    {
        var settings = new Settings { SingleLayer = true, SingleLayerName = "Plani:metría" };
        var points = new[] { P(1, "casa", 0, 0), P(2, "casa", 1, 0), P(3, "casa", 1, 1), P(4, "Pt", 5, 5) };
        var rules = ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, settings);

        var result = new BlockPlanimetryEngine(settings).Run(points, rules);

        Assert.Equal("Plani_metría", Assert.Single(result.Lines).Layer);
        Assert.Equal("PT-PT", Assert.Single(result.PointObjects).Layer);
    }

    [Fact]
    public void SingleLayerOff_KeepsLayerPerCode()
    {
        var settings = new Settings { SingleLayer = false, SingleLayerName = "Planimetría" };
        var points = new[] { P(1, "CUNETA", 10, 4), P(2, "CUNETA", 20, 4) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);
        var result = new PlanimetryEngine(settings).Run(points, rules, TestAxes.StraightX());
        Assert.Equal("PL-CUNETA-IZQ", Assert.Single(result.Lines).Layer);
    }

    [Fact]
    public void CodeFromDescription_UsesBaseCodeWithoutMarks()
    {
        var settings = new Settings();
        Assert.Equal("CUNETQ B", ClassificationProposer.CodeFromDescription("cunetq B", settings));
        Assert.Equal("CUNETA", ClassificationProposer.CodeFromDescription("cuneta f", settings));
        Assert.Equal("CUNETA", ClassificationProposer.CodeFromDescription("cuneta2", settings));
        Assert.Null(ClassificationProposer.CodeFromDescription("", settings));
        Assert.Null(ClassificationProposer.CodeFromDescription("3015396", settings));
    }

    [Fact]
    public void UseDescriptionAsCode_ResolvesUnknownAtLoad()
    {
        var settings = new Settings { UseDescriptionAsCode = true };
        var points = new[] { P(1, "cunetq B", 0, 0), P(2, "v", 0, 0), P(3, "", 0, 0), P(4, "3015396", 0, 0) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);

        var unknown = rules.Single(r => r.Original == "cunetq B");
        Assert.Equal("CUNETQ B", unknown.DestinationCode);
        Assert.Equal(CodeKind.Line, unknown.Kind);
        Assert.Equal("PL-CUNETQ B", unknown.LayerBase);
        Assert.Equal("VEREDA", rules.Single(r => r.Original == "v").DestinationCode); // el catálogo sigue mandando
        Assert.Equal(CodeKind.Ignore, rules.Single(r => r.Original == "").Kind);
        Assert.Equal(CodeKind.Ignore, rules.Single(r => r.Original == "3015396").Kind);
    }

    [Fact]
    public void UseNamesAsCodes_WithoutSelection_OnlyUnresolvedRows()
    {
        var settings = new Settings();
        var points = new[] { P(1, "cunetq B", 0, 0), P(2, "v", 0, 0), P(3, "zzz", 0, 0), P(4, "", 0, 0) };
        var rules = ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, settings);
        var vm = new ClassificationViewModel(rules, CodeCatalog.Default, settings) { BlockMode = true };
        Assert.Equal(2, vm.PointsUnresolved);

        Assert.Equal(2, vm.UseNamesAsCodes());

        Assert.Equal("CUNETQ B", vm.Rows.Single(r => r.Rule.Original == "cunetq B").DestinationCode);
        Assert.Equal("ZZZ", vm.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode);
        Assert.Equal(BlockJoin.Block, vm.Rows.Single(r => r.Rule.Original == "zzz").Rule.BlockJoin);
        Assert.Equal("VEREDA", vm.Rows.Single(r => r.Rule.Original == "v").DestinationCode); // resuelta: no se toca
        Assert.Equal(CodeKind.Ignore, vm.Rows.Single(r => r.Rule.Original == "").Rule.Kind);
        Assert.Equal(0, vm.PointsUnresolved);
        Assert.Contains("2 fila(s)", vm.StatusMessage);
        Assert.Contains("CUNETQ B", vm.AvailableCodes);

        Assert.Equal(0, vm.UseNamesAsCodes());
        Assert.Equal("No hay filas sin resolver.", vm.StatusMessage);
    }

    [Fact]
    public void UseNamesAsCodes_WithSelection_OverridesCatalogMapping()
    {
        var settings = new Settings();
        var points = new[] { P(1, "v", 0, 0), P(2, "ver", 0, 0), P(3, "zzz", 0, 0) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);
        var vm = new ClassificationViewModel(rules, CodeCatalog.Default, settings);
        var v = vm.Rows.Single(r => r.Rule.Original == "v");
        v.IsSelected = true;

        Assert.Equal(1, vm.UseNamesAsCodes());

        Assert.Equal("V", v.DestinationCode);
        Assert.False(v.IsSelected);
        Assert.Equal("VEREDA", vm.Rows.Single(r => r.Rule.Original == "ver").DestinationCode);
        Assert.False(vm.Rows.Single(r => r.Rule.Original == "zzz").IsResolved); // había selección: lo demás no se toca

        // Selección de la tabla como alternativa a la casilla Sel.
        Assert.Equal(1, vm.UseNamesAsCodes(new[] { vm.Rows.Single(r => r.Rule.Original == "zzz") }));
        Assert.Equal("ZZZ", vm.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode);
    }

    [Fact]
    public void ViewModel_SingleLayerOptions_AndSummary()
    {
        var settings = new Settings();
        var vm = new ClassificationViewModel(ClassificationProposer.BuildRules(new[] { P(1, "v", 0, 0) }, CodeCatalog.Default, settings), CodeCatalog.Default, settings);
        Assert.Contains("una capa por código", vm.SettingsSummary);

        vm.SingleLayer = true;
        vm.SingleLayerName = "Plani/metría";
        Assert.True(settings.SingleLayer);
        Assert.Equal("Plani_metría", settings.SingleLayerName);
        Assert.Contains("capa única: Plani_metría", vm.SettingsSummary);

        vm.SingleLayerName = "   "; // vacío: se conserva el anterior
        Assert.Equal("Plani_metría", settings.SingleLayerName);
    }

    [Fact]
    public void SettingsViewModel_ValidatesSingleLayerName_AndCopiesNewFields()
    {
        var target = new Settings();
        var vm = new SettingsViewModel(target) { SingleLayer = true, SingleLayerName = " ", UseDescriptionAsCode = true };
        Assert.Contains(vm.Validate(), e => e.Contains("capa única"));
        Assert.False(vm.Apply());

        vm.SingleLayerName = "Planimetría";
        Assert.True(vm.Apply());
        Assert.True(target.SingleLayer);
        Assert.Equal("Planimetría", target.SingleLayerName);
        Assert.True(target.UseDescriptionAsCode);

        var clone = target.Clone();
        Assert.True(clone.SingleLayer);
        Assert.True(clone.UseDescriptionAsCode);
    }
}
