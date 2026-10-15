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
    public void SingleLayer_AxisMode_AllLinesGoToOneLayer()
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
        // En bloques todo se une; el tipo Punto se elige a mano.
        var pt = rules.Single(r => r.Original == "Pt");
        pt.Kind = CodeKind.Point;
        pt.LayerBase = LayerNames.ProposeBase(pt, settings);

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
    public void CodeFromDescription_KeepsTextExactly_WithoutMarks()
    {
        // Las marcas no son parte del nombre: "cunetq B" y "cunetq" deben ser el mismo código y el mismo bloque.
        Assert.Equal("cunetq", ClassificationProposer.CodeFromDescription("cunetq B"));
        Assert.Equal("Cuneta", ClassificationProposer.CodeFromDescription("Cuneta B"));
        Assert.Equal("cuneta", ClassificationProposer.CodeFromDescription("cuneta f"));
        Assert.Equal("cuneta", ClassificationProposer.CodeFromDescription("cuneta I"));
        Assert.Equal("cuneta", ClassificationProposer.CodeFromDescription("cuneta.."));
        // El resto se respeta tal cual: mayúsculas, minúsculas, tildes, espacios interiores y números.
        Assert.Equal("Muro  alto", ClassificationProposer.CodeFromDescription("  Muro  alto "));
        Assert.Equal("Árbol", ClassificationProposer.CodeFromDescription("Árbol"));
        Assert.Equal("PG5394", ClassificationProposer.CodeFromDescription("PG5394"));
        Assert.Equal("B", ClassificationProposer.CodeFromDescription("B")); // la primera palabra siempre es el código
        Assert.Null(ClassificationProposer.CodeFromDescription(""));
        Assert.Null(ClassificationProposer.CodeFromDescription("   "));
        Assert.Null(ClassificationProposer.CodeFromDescription("3015396"));
        // Sin interpretar marcas, el texto queda entero.
        Assert.Equal("cunetq B", ClassificationProposer.CodeFromDescription("cunetq B", new Settings { UseDescriptionMarks = false }));
    }

    [Fact]
    public void ExactCode_StillMatchesCatalogKindAndBlockProposal_AndAxisCode()
    {
        var settings = new Settings();
        var rule = new CodeRule { Original = "casa", Count = 3 };
        ClassificationProposer.ApplyDestination(rule, "casa", CodeCatalog.Default, settings, blockMode: true, exact: true);
        Assert.Equal("casa", rule.DestinationCode);
        Assert.Equal(CodeKind.Line, rule.Kind);
        Assert.Equal(BlockJoin.ClosedBlock, rule.BlockJoin);
        Assert.Equal("PL-casa", rule.LayerBase);

        // "e" exacto toma del catálogo lo de su código (EJE, alias e): sin lados, color 2; en bloques, uno por bloque.
        var axis = new CodeRule { Original = "e" };
        ClassificationProposer.ApplyDestination(axis, "e", CodeCatalog.Default, settings, blockMode: true, exact: true);
        Assert.Equal("e", axis.DestinationCode);
        Assert.Equal(BlockJoin.Block, axis.BlockJoin);
        Assert.False(axis.SplitBySide);
        Assert.Equal(2, axis.ColorIndex);

        var eje = new CodeRule { Original = "eje" };
        ClassificationProposer.ApplyDestination(eje, "eje", CodeCatalog.Default, settings, exact: true);
        var points = new[] { P(1, "eje", 0, 0), P(2, "eje B", 10, 0), P(3, "v", 5, 3) };
        Assert.Equal(2, AxisBuilder.SelectAxisPoints(points, new[] { eje, new CodeRule { Original = "v", DestinationCode = "VEREDA" } }, settings).Count);
    }

    [Fact]
    public void UseDescriptionAsCode_ResolvesUnknownAtLoad()
    {
        var settings = new Settings { UseDescriptionAsCode = true };
        var points = new[] { P(1, "zanjq B", 0, 0), P(2, "zanjq", 0, 0), P(3, "v", 0, 0), P(4, "", 0, 0), P(5, "3015396", 0, 0) };
        var rules = ClassificationProposer.BuildRules(points, CodeCatalog.Default, settings);

        var unknown = rules.Single(r => r.Original == "zanjq");
        Assert.Equal(2, unknown.Count); // "zanjq B" es la misma fila
        Assert.Equal("zanjq", unknown.DestinationCode);
        Assert.Equal(CodeKind.Line, unknown.Kind);
        Assert.Equal("PL-zanjq", unknown.LayerBase);
        Assert.Equal("VEREDA", rules.Single(r => r.Original == "v").DestinationCode); // el catálogo sigue mandando
        Assert.Equal(CodeKind.Ignore, rules.Single(r => r.Original == "").Kind);
        Assert.Equal(CodeKind.Ignore, rules.Single(r => r.Original == "3015396").Kind);
    }

    [Fact]
    public void UseNamesAsCodes_WithoutSelection_AllRows()
    {
        var settings = new Settings();
        var points = new[] { P(1, "zanjq B", 0, 0), P(2, "zanjq", 0, 0), P(3, "v", 0, 0), P(4, "zzz", 0, 0), P(5, "", 0, 0) };
        var rules = ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, settings);
        var vm = new ClassificationViewModel(rules, CodeCatalog.Default, settings) { BlockMode = true };
        Assert.Equal(3, vm.PointsUnresolved);

        Assert.Equal(3, vm.UseNamesAsCodes());

        Assert.Equal("zanjq", vm.Rows.Single(r => r.Rule.Original == "zanjq").DestinationCode);
        Assert.Equal("zzz", vm.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode);
        Assert.Equal(BlockJoin.Block, vm.Rows.Single(r => r.Rule.Original == "zzz").Rule.BlockJoin);
        Assert.Equal("v", vm.Rows.Single(r => r.Rule.Original == "v").DestinationCode); // también lo que conocía el catálogo
        Assert.Equal(CodeKind.Ignore, vm.Rows.Single(r => r.Rule.Original == "").Rule.Kind);
        Assert.Equal(0, vm.PointsUnresolved);
        Assert.Contains("3 fila(s)", vm.StatusMessage);
        Assert.Contains("zanjq", vm.AvailableCodes);

        Assert.Equal(0, vm.UseNamesAsCodes());
        Assert.Equal("Todas las filas ya usan su nombre como código.", vm.StatusMessage);
    }

    [Fact]
    public void UseNamesAsCodes_WorksAfterAnyOtherAction_NotOnlyFirst()
    {
        var settings = new Settings();
        var points = new[] { P(1, "cunetq B", 0, 0), P(2, "cunetq", 0, 0), P(3, "cumeta", 0, 0), P(4, "Cuneta", 0, 0) };
        var vm = new ClassificationViewModel(ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, settings), CodeCatalog.Default, settings) { BlockMode = true };

        // Primero otro botón: "Sugerir similares" resuelve las filas con códigos del catálogo.
        vm.SuggestSimilar();
        Assert.All(vm.Rows, r => Assert.True(r.IsResolved));

        // Una sola fila resaltada en la tabla (la fila actual tras un clic) no es una selección.
        var current = vm.Rows.Single(r => r.Rule.Original == "cumeta");
        Assert.Equal(3, vm.UseNamesAsCodes(new[] { current }));
        Assert.Equal(new[] { "cunetq", "Cuneta", "cumeta" }, vm.Rows.Select(r => r.DestinationCode));

        // Después de restablecer y de editar a mano, vuelve a funcionar igual.
        vm.ResetToInitial();
        vm.Rows.Single(r => r.Rule.Original == "Cuneta").DestinationCode = "ZANJA";
        Assert.Equal(3, vm.UseNamesAsCodes());
        Assert.Equal(new[] { "cunetq", "Cuneta", "cumeta" }, vm.Rows.Select(r => r.DestinationCode));
    }

    [Fact]
    public void ExactCode_IsNotUppercased_WhenTheCellIsReentered_AndBulkEditCopiesItExactly()
    {
        var settings = new Settings();
        var points = new[] { P(1, "cunetq", 0, 0), P(2, "zzz", 0, 0) };
        var vm = new ClassificationViewModel(ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, settings), CodeCatalog.Default, settings) { BlockMode = true };
        vm.UseNamesAsCodes();
        var row = vm.Rows.Single(r => r.Rule.Original == "cunetq");

        // La celda editable devuelve el mismo texto al perder el foco: no debe pasar a mayúsculas.
        row.DestinationCode = "cunetq";
        Assert.Equal("cunetq", row.DestinationCode);

        // Edición en bloque: el código se copia tal cual a la otra fila seleccionada.
        var other = vm.Rows.Single(r => r.Rule.Original == "zzz");
        Assert.True(other.CopyFrom(row, nameof(ClassificationRowViewModel.DestinationCode)));
        Assert.Equal("cunetq", other.DestinationCode);
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

        Assert.Equal("v", v.DestinationCode);
        Assert.False(v.IsSelected);
        Assert.Equal("VEREDA", vm.Rows.Single(r => r.Rule.Original == "ver").DestinationCode);
        Assert.False(vm.Rows.Single(r => r.Rule.Original == "zzz").IsResolved); // había selección: lo demás no se toca

        // Varias filas seleccionadas en la tabla: alternativa a la casilla Sel.
        var grid = new[] { vm.Rows.Single(r => r.Rule.Original == "zzz"), vm.Rows.Single(r => r.Rule.Original == "ver") };
        Assert.Equal(2, vm.UseNamesAsCodes(grid));
        Assert.Equal("zzz", vm.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode);
        Assert.Equal("ver", vm.Rows.Single(r => r.Rule.Original == "ver").DestinationCode);

        // Una fila en Ignorar cambia solo si se marca.
        var ignored = vm.Rows.Single(r => r.Rule.Original == "zzz");
        ignored.KindLabel = CodeKind.Ignore.ToSpanish();
        Assert.Equal(0, vm.UseNamesAsCodes());
        Assert.Equal(CodeKind.Ignore, ignored.Rule.Kind);
        ignored.IsSelected = true;
        Assert.Equal(1, vm.UseNamesAsCodes());
        Assert.Equal(CodeKind.Line, ignored.Rule.Kind);
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

        vm.SingleLayerColor = 250;
        Assert.Equal(250, settings.SingleLayerColor);
        vm.SingleLayerColor = 0; // fuera de rango: se conserva
        Assert.Equal(250, settings.SingleLayerColor);
        Assert.Contains("(color 250)", vm.SettingsSummary);
    }

    [Fact]
    public void SettingsViewModel_ValidatesSingleLayerName_AndCopiesNewFields()
    {
        var target = new Settings();
        var vm = new SettingsViewModel(target) { SingleLayer = true, SingleLayerName = " ", UseDescriptionAsCode = true };
        Assert.Contains(vm.Validate(), e => e.Contains("capa única"));
        Assert.False(vm.Apply());

        vm.SingleLayerName = "Planimetría";
        vm.SingleLayerColor = 300;
        Assert.Contains(vm.Validate(), e => e.Contains("color"));
        vm.SingleLayerColor = 7;
        Assert.True(vm.Apply());
        Assert.True(target.SingleLayer);
        Assert.Equal("Planimetría", target.SingleLayerName);
        Assert.Equal(7, target.SingleLayerColor);
        Assert.True(target.UseDescriptionAsCode);

        var clone = target.Clone();
        Assert.True(clone.SingleLayer);
        Assert.True(clone.UseDescriptionAsCode);
    }
}
