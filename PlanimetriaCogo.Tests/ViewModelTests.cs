using System.IO;
using System.Linq;
using PlanimetriaCogo.Config;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using PlanimetriaCogo.UI;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class ClassificationViewModelTests
{
    private static SurveyPoint P(uint n, string desc) => new() { Number = n, RawDescription = desc, SourceId = n.ToString() };

    private static ClassificationViewModel Build(Settings? settings = null, ProfileStore? profiles = null, ConfigService? config = null)
    {
        settings ??= new Settings();
        var points = new[]
        {
            P(1, "cumeta"), P(2, "cumeta"), P(3, "CUNETA"), P(4, "v"), P(5, "casa"), P(6, "zzz"), P(7, "3015396"), P(8, "e"), P(9, "e"),
        };
        var catalog = config?.LoadCatalog() ?? CodeCatalog.Default;
        var rules = ClassificationProposer.BuildRules(points, catalog, settings);
        return new ClassificationViewModel(rules, catalog, settings, profiles, config);
    }

    [Fact]
    public void Totals_AndAxisPointCount()
    {
        var vm = Build();
        Assert.Equal(9, vm.TotalPoints);
        Assert.Equal(6, vm.PointsToLines);       // cumeta×2, CUNETA, v, e×2
        Assert.Equal(1, vm.PointsToPointObjects); // casa
        Assert.Equal(1, vm.PointsIgnored);        // 3015396
        Assert.Equal(1, vm.PointsUnresolved);     // zzz
        Assert.Equal(2, vm.AxisPointCount);
        Assert.True(vm.CanDraw);
    }

    [Fact]
    public void AxisFromPoints_RequiresTwoAxisPoints()
    {
        var vm = Build();
        vm.AxisFromPoints = true;
        Assert.True(vm.CanDraw);

        vm.Rows.Single(r => r.Rule.Original == "e").DestinationCode = "CUNETA";
        Assert.Equal(0, vm.AxisPointCount);
        Assert.False(vm.CanDraw);
        Assert.Contains("EJE", vm.CannotDrawReason);
    }

    [Fact]
    public void Filter_ByTextAndUnresolved()
    {
        var vm = Build();
        Assert.Equal(vm.Rows.Count, vm.VisibleRows.Count);

        vm.FilterText = "cun";
        Assert.Equal(new[] { "cumeta", "CUNETA" }, vm.VisibleRows.Select(r => r.Rule.Original));

        vm.FilterText = "";
        vm.ShowOnlyUnresolved = true;
        Assert.Equal(new[] { "zzz" }, vm.VisibleRows.Select(r => r.Rule.Original));
        Assert.Equal("1 de 7 descripciones", vm.VisibleCountText);
    }

    [Fact]
    public void SuggestSimilar_ResolvesUnknown()
    {
        var vm = Build();
        vm.SuggestSimilar();
        // "zzz" no se parece a nada: se propone a sí misma como código nuevo.
        Assert.Equal("ZZZ", vm.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode);
        Assert.Equal(0, vm.PointsUnresolved);
    }

    [Fact]
    public void PropagateEdit_CopiesToMarkedRows_WhenSourceIsMarked()
    {
        var vm = Build();
        var cumeta = vm.Rows.Single(r => r.Rule.Original == "cumeta");
        var cuneta = vm.Rows.Single(r => r.Rule.Original == "CUNETA");
        var v = vm.Rows.Single(r => r.Rule.Original == "v");
        var casa = vm.Rows.Single(r => r.Rule.Original == "casa");
        cumeta.IsSelected = cuneta.IsSelected = v.IsSelected = true;

        // Capa: se copia a las otras dos marcadas, no a las demás.
        cumeta.LayerBase = "PL-DRENAJE";
        Assert.Equal(2, vm.PropagateEdit(cumeta, nameof(ClassificationRowViewModel.LayerBase)));
        Assert.Equal("PL-DRENAJE", cuneta.LayerBase);
        Assert.Equal("PL-DRENAJE", v.LayerBase);
        Assert.NotEqual("PL-DRENAJE", casa.LayerBase);
        Assert.Contains("Capa", vm.StatusMessage);

        // Color y lado.
        cumeta.ColorIndex = 30;
        cumeta.SplitBySide = false;
        Assert.Equal(2, vm.PropagateEdit(cumeta, nameof(ClassificationRowViewModel.ColorIndex)));
        Assert.Equal(2, vm.PropagateEdit(cumeta, nameof(ClassificationRowViewModel.SplitBySide)));
        Assert.All(new[] { cuneta, v }, r => { Assert.Equal((short)30, r.ColorIndex); Assert.False(r.SplitBySide); });

        // Tipo: "Ignorar" en una marcada deja fuera a todas las marcadas.
        cumeta.KindLabel = CodeKind.Ignore.ToSpanish();
        Assert.Equal(2, vm.PropagateEdit(cumeta, nameof(ClassificationRowViewModel.KindLabel)));
        Assert.Equal(CodeKind.Ignore, cuneta.Rule.Kind);
        Assert.Equal(CodeKind.Ignore, v.Rule.Kind);
        Assert.Equal(5, vm.PointsIgnored); // 3015396 + cumeta×2 + CUNETA + v

        // Las marcas se conservan para seguir editando en bloque.
        Assert.True(cuneta.IsSelected && v.IsSelected && cumeta.IsSelected);
    }

    [Fact]
    public void PropagateEdit_DestinationCode_ReproposesKindAndLayer()
    {
        var vm = Build();
        var cumeta = vm.Rows.Single(r => r.Rule.Original == "cumeta");
        var zzz = vm.Rows.Single(r => r.Rule.Original == "zzz");
        cumeta.IsSelected = zzz.IsSelected = true;

        cumeta.DestinationCode = "CASA";
        Assert.Equal(1, vm.PropagateEdit(cumeta, nameof(ClassificationRowViewModel.DestinationCode)));
        Assert.Equal("CASA", zzz.DestinationCode);
        Assert.Equal(CodeKind.Point, zzz.Rule.Kind);
        Assert.Equal(cumeta.LayerBase, zzz.LayerBase);
        Assert.Equal(0, vm.PointsUnresolved);
    }

    [Fact]
    public void PropagateEdit_IgnoresUnmarkedSource_AndUnknownProperty()
    {
        var vm = Build();
        var cumeta = vm.Rows.Single(r => r.Rule.Original == "cumeta");
        var cuneta = vm.Rows.Single(r => r.Rule.Original == "CUNETA");
        var casa = vm.Rows.Single(r => r.Rule.Original == "casa");
        cumeta.IsSelected = cuneta.IsSelected = true;

        // Editar una fila que no está marcada no toca a las marcadas.
        casa.LayerBase = "PT-OTRA";
        Assert.Equal(0, vm.PropagateEdit(casa, nameof(ClassificationRowViewModel.LayerBase)));
        Assert.NotEqual("PT-OTRA", cumeta.LayerBase);

        // Una sola fila marcada: nada que propagar.
        cuneta.IsSelected = false;
        cumeta.LayerBase = "PL-X";
        Assert.Equal(0, vm.PropagateEdit(cumeta, nameof(ClassificationRowViewModel.LayerBase)));

        // Propiedades no editables (p. ej. la propia marca Sel.) no propagan nada.
        Assert.Equal(0, vm.PropagateEdit(cumeta, nameof(ClassificationRowViewModel.IsSelected)));
        Assert.Null(cumeta.GetEditableValue(nameof(ClassificationRowViewModel.IsSelected)));
    }

    [Fact]
    public void PropagateEdit_UsesGridSelection_WithoutMarks()
    {
        var vm = Build();
        var cumeta = vm.Rows.Single(r => r.Rule.Original == "cumeta");
        var cuneta = vm.Rows.Single(r => r.Rule.Original == "CUNETA");
        var v = vm.Rows.Single(r => r.Rule.Original == "v");

        // Selección de la tabla (Ctrl+clic + F2) sin marcas en Sel.
        cuneta.ColorIndex = 12;
        Assert.Equal(2, vm.PropagateEdit(cuneta, nameof(ClassificationRowViewModel.ColorIndex), new[] { cumeta, cuneta, v }));
        Assert.Equal((short)12, cumeta.ColorIndex);
        Assert.Equal((short)12, v.ColorIndex);

        // Si la fila editada no forma parte de la selección, no se propaga.
        var casa = vm.Rows.Single(r => r.Rule.Original == "casa");
        casa.ColorIndex = 40;
        Assert.Equal(0, vm.PropagateEdit(casa, nameof(ClassificationRowViewModel.ColorIndex), new[] { cumeta, v }));
        Assert.Equal((short)12, cumeta.ColorIndex);
    }

    [Fact]
    public void ResetToInitial_RestoresProposal()
    {
        var vm = Build();
        var row = vm.Rows.Single(r => r.Rule.Original == "cumeta");
        row.DestinationCode = "VEREDA";
        row.ColorIndex = 1;
        Assert.Equal("PL-VEREDA", row.LayerBase);

        vm.ResetToInitial();

        Assert.Equal("CUNETA", row.DestinationCode);
        Assert.Equal("PL-CUNETA", row.LayerBase);
        Assert.Equal(4, row.ColorIndex);
        Assert.Contains("restablecieron 1", vm.StatusMessage);
    }

    [Fact]
    public void Options_ProxySettings()
    {
        var settings = new Settings();
        var vm = Build(settings);
        vm.UpdateDescriptionsInDrawing = true;
        vm.ReportToFile = true;
        Assert.True(settings.UpdateDescriptionsInDrawing);
        Assert.True(settings.ReportToFile);
        Assert.False(settings.MovePointObjectsToLayer);
        Assert.Contains("offsetMax 40 m", vm.SettingsSummary);
    }

    [Fact]
    public void Profiles_SaveAndLoad_ThroughViewModel()
    {
        using var dir = new TempDir();
        var paths = new ConfigPaths(dir.Path);
        var store = new ProfileStore(paths);

        var first = Build(profiles: store);
        first.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode = "ZANJA";
        first.Rows.Single(r => r.Rule.Original == "casa").ColorIndex = 30;
        var path = first.SaveProfile("obra 1");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(new[] { "obra 1" }, first.ProfileNames);

        var second = Build(profiles: store);
        Assert.Equal(1, second.PointsUnresolved);
        var changed = second.LoadProfile("obra 1");
        Assert.Equal(2, changed);
        Assert.Equal("ZANJA", second.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode);
        Assert.Equal(30, second.Rows.Single(r => r.Rule.Original == "casa").ColorIndex);
        Assert.Equal(0, second.PointsUnresolved);
    }

    [Fact]
    public void Profiles_Unavailable_ReportsAndReturnsNull()
    {
        var vm = Build();
        Assert.False(vm.HasProfiles);
        Assert.Null(vm.SaveProfile("x"));
        Assert.Equal(-1, vm.LoadProfile("x"));
    }

    [Fact]
    public void AddToCatalog_PersistsNewCodesAndAliases()
    {
        using var dir = new TempDir();
        var config = new ConfigService(new ConfigPaths(dir.Path));
        var vm = Build(config: config);
        vm.Rows.Single(r => r.Rule.Original == "zzz").DestinationCode = "ZANJA";

        Assert.True(vm.AddToCatalog());

        var reloaded = config.LoadCatalog();
        Assert.Equal("ZANJA", reloaded.Resolve("zzz"));
        Assert.Equal("CUNETA", reloaded.Resolve("cumeta"));
        Assert.Contains("ZANJA", vm.AvailableCodes);
        Assert.False(vm.AddToCatalog()); // segunda vez: nada nuevo
    }
}

public class SettingsViewModelTests
{
    [Fact]
    public void Apply_CopiesOnlyWhenValid()
    {
        var target = new Settings();
        var vm = new SettingsViewModel(target);
        vm.OffsetMax = 80;
        vm.OrderLabel = "Progresiva";
        vm.EndMarksText = "F, FIN, END";

        Assert.True(vm.Apply());
        Assert.Equal(80, target.OffsetMax);
        Assert.Equal(PointOrder.Station, target.Order);
        Assert.Equal(new[] { "F", "FIN", "END" }, target.EndMarks);
    }

    [Fact]
    public void Validate_RejectsNegativeAndDuplicateMarks()
    {
        var vm = new SettingsViewModel(new Settings());
        vm.DistMax = -1;
        vm.LeftMarksText = "I, F"; // F ya es marca de fin
        var errors = vm.Validate();
        Assert.Contains(errors, e => e.Contains("distMax"));
        Assert.Contains(errors, e => e.Contains("dos listas"));
        Assert.False(vm.Apply());
    }

    [Fact]
    public void Cancel_DoesNotTouchTarget()
    {
        var target = new Settings();
        var vm = new SettingsViewModel(target);
        vm.OffsetMax = 99;
        Assert.Equal(40, target.OffsetMax);
    }

    [Fact]
    public void ResetDefaults_RestoresWorkingCopy()
    {
        var vm = new SettingsViewModel(new Settings { OffsetMax = 99 });
        Assert.Equal(99, vm.OffsetMax);
        vm.ResetDefaults();
        Assert.Equal(40, vm.OffsetMax);
    }
}

public class ReportFilesTests
{
    [Fact]
    public void ResolveFolder_PrefersSettingThenDrawingThenAppData()
    {
        using var dir = new TempDir();
        var paths = new ConfigPaths(dir.Path);
        var drawing = Path.Combine(dir.Path, "obra.dwg");
        File.WriteAllText(drawing, "");

        Assert.Equal(@"X:\rep", ReportFiles.ResolveFolder(new Settings { ReportFolder = @"X:\rep" }, paths, drawing));
        Assert.Equal(dir.Path, ReportFiles.ResolveFolder(new Settings(), paths, drawing));
        Assert.Equal(paths.ReportsDir, ReportFiles.ResolveFolder(new Settings(), paths, null));
        Assert.Equal(paths.ReportsDir, ReportFiles.ResolveFolder(new Settings(), paths, Path.Combine(dir.Path, "no-existe", "x.dwg")));
    }

    [Fact]
    public void Write_CreatesTxtAndCsv()
    {
        using var dir = new TempDir();
        var paths = new ConfigPaths(dir.Path);
        var result = new PlanimetryResult { TotalPoints = 3 };
        result.UnresolvedCodes["zzz"] = 3;

        var (txt, csv) = ReportFiles.Write(result, new Settings(), paths, null, new System.DateTime(2026, 10, 15, 8, 30, 0));

        Assert.EndsWith("dibujo_planimetria_20261015_083000.txt", txt);
        Assert.EndsWith(".csv", csv);
        Assert.Contains("Puntos procesados: 3", File.ReadAllText(txt));
        Assert.Contains("SIN_RESOLVER;zzz", File.ReadAllText(csv));
    }
}
