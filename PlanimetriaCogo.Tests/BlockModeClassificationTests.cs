using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlanimetriaCogo.Config;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using PlanimetriaCogo.UI;
using Xunit;

namespace PlanimetriaCogo.Tests;

public class BlockModeClassificationTests
{
    private static List<SurveyPoint> Points(params string[] descriptions) =>
        descriptions.Select((d, i) => new SurveyPoint { Number = (uint)(i + 1), RawDescription = d, Sequence = i + 1, SourceId = (i + 1).ToString() }).ToList();

    [Fact]
    public void BuildBlockRules_ProposesClosedForCasa_BlockForEveryOtherCode()
    {
        var settings = new Settings();
        var rules = ClassificationProposer.BuildBlockRules(Points("casa", "casa", "e", "v", "Pt", "3015396"), CodeCatalog.Default, settings);

        var casa = rules.Single(r => r.Original == "casa");
        Assert.Equal(CodeKind.Line, casa.Kind); // el catálogo lo tiene como Punto; en bloques se une
        Assert.Equal(BlockJoin.ClosedBlock, casa.BlockJoin);
        Assert.Equal("PL-CASA", casa.LayerBase);

        // Único criterio: el archivo. Cada bloque es un elemento separado, también para el eje y los objetos puntuales.
        Assert.Equal(BlockJoin.Block, rules.Single(r => r.Original == "e").BlockJoin);
        Assert.Equal(BlockJoin.Block, rules.Single(r => r.Original == "v").BlockJoin);
        var pt = rules.Single(r => r.Original == "Pt");
        Assert.Equal(CodeKind.Line, pt.Kind);
        Assert.Equal(BlockJoin.Block, pt.BlockJoin);
        Assert.Equal("PL-PT", pt.LayerBase);
        Assert.Equal(CodeKind.Ignore, rules.Single(r => r.Original == "3015396").Kind);
    }

    [Fact]
    public void BuildRules_BlockMarkDoesNotCreateARow()
    {
        var settings = new Settings();
        var rules = ClassificationProposer.BuildBlockRules(Points("casa B", "casa", "casa", "cuneta B", "cuneta", "b B", "PilonA B", "PilonA"), CodeCatalog.Default, settings);

        Assert.Equal(new[] { "casa", "PilonA", "cuneta", "b" }, rules.Select(r => r.Original));
        Assert.Equal(3, rules.Single(r => r.Original == "casa").Count);
        Assert.Equal("CASA", rules.Single(r => r.Original == "casa").DestinationCode);
        Assert.Equal("CUNETA", rules.Single(r => r.Original == "cuneta").DestinationCode);
        Assert.Equal("BORDE", rules.Single(r => r.Original == "b").DestinationCode);
        Assert.Equal(CodeKind.Line, rules.Single(r => r.Original == "PilonA").Kind);

        // Sin marcas configuradas, "casa B" vuelve a ser una descripción propia.
        var noMarks = ClassificationProposer.BuildBlockRules(Points("casa B", "casa"), CodeCatalog.Default, new Settings { BlockStartMarks = new List<string>() });
        Assert.Equal(2, noMarks.Count);
    }

    [Fact]
    public void ApplyDestination_InBlockMode_ReproposesJoin()
    {
        var settings = new Settings();
        var rule = new CodeRule { Original = "cas", Count = 3 };
        ClassificationProposer.ApplyDestination(rule, "CASA", CodeCatalog.Default, settings, blockMode: true);
        Assert.Equal(CodeKind.Line, rule.Kind);
        Assert.Equal(BlockJoin.ClosedBlock, rule.BlockJoin);

        ClassificationProposer.ApplyDestination(rule, "CASA", CodeCatalog.Default, settings, blockMode: false);
        Assert.Equal(CodeKind.Point, rule.Kind); // el modo por eje sigue proponiendo lo del catálogo
    }

    [Fact]
    public void ViewModel_BlockMode_PropagatesToRows_AndSummary()
    {
        var settings = new Settings();
        var points = Points("casa", "casa", "cas", "e");
        var rules = ClassificationProposer.BuildBlockRules(points, CodeCatalog.Default, settings);
        var vm = new ClassificationViewModel(rules, CodeCatalog.Default, settings) { BlockMode = true };
        vm.SetBlockStats(BlockStats.ByDescription(points));

        Assert.All(vm.Rows, r => Assert.True(r.BlockMode));
        Assert.Equal("1 (máx. 2)", vm.Rows.Single(r => r.Rule.Original == "casa").BlockInfo);
        Assert.Contains("bloques cerrados: CASA", vm.SettingsSummary);
        Assert.Equal(BlockJoinNames.All, vm.JoinOptions);

        // Resolver "cas" → CASA en modo bloques propone bloque cerrado.
        var cas = vm.Rows.Single(r => r.Rule.Original == "cas");
        cas.DestinationCode = "CASA";
        Assert.Equal(BlockJoinNames.ClosedBlock, cas.BlockJoinLabel);
        Assert.Equal("línea", cas.Status);
        Assert.Equal(4, vm.PointsToLines);
    }

    [Fact]
    public void ViewModel_JoinColumn_IsPropagatedAndReset()
    {
        var settings = new Settings();
        var rules = ClassificationProposer.BuildBlockRules(Points("tn", "tn", "b"), CodeCatalog.Default, settings);
        var vm = new ClassificationViewModel(rules, CodeCatalog.Default, settings) { BlockMode = true };
        var tn = vm.Rows.Single(r => r.Rule.Original == "tn");
        var b = vm.Rows.Single(r => r.Rule.Original == "b");

        tn.IsSelected = true;
        b.IsSelected = true;
        tn.BlockJoinLabel = BlockJoinNames.Continuous;
        Assert.Equal(1, vm.PropagateEdit(tn, nameof(ClassificationRowViewModel.BlockJoinLabel)));
        Assert.Equal(BlockJoin.Continuous, b.Rule.BlockJoin);
        Assert.Contains("Unión", vm.StatusMessage);

        vm.ResetToInitial();
        Assert.Equal(BlockJoin.Block, tn.Rule.BlockJoin);
        Assert.Equal(BlockJoin.Block, b.Rule.BlockJoin);
    }

    [Fact]
    public void Profile_KeepsBlockJoin_AndOldProfilesDefaultToBlock()
    {
        var rule = new CodeRule { Original = "casa", DestinationCode = "CASA", Kind = CodeKind.Line, BlockJoin = BlockJoin.ClosedBlock, LayerBase = "PL-CASA" };
        var json = Profile.FromRules("p", new[] { rule }).ToJson();
        Assert.Contains("\"blockJoin\": \"closedBlock\"", json);

        var target = new CodeRule { Original = "casa", DestinationCode = "CASA", Kind = CodeKind.Line, LayerBase = "PL-CASA" };
        Assert.Equal(1, Profile.FromJson(json).ApplyTo(new[] { target }));
        Assert.Equal(BlockJoin.ClosedBlock, target.BlockJoin);

        var old = Profile.FromJson("{\"name\":\"viejo\",\"entries\":[{\"original\":\"casa\",\"destinationCode\":\"CASA\",\"kind\":\"line\",\"layerBase\":\"PL-CASA\"}]}");
        Assert.Equal(BlockJoin.Block, old.Entries[0].BlockJoin);
    }

    [Fact]
    public void Settings_RoundTrip_ClosedBlockCodesAndCsvOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "PlanimetriaCogoTests_" + Path.GetRandomFileName());
        try
        {
            var service = new ConfigService(new ConfigPaths(root));
            var settings = new Settings { ClosedBlockCodes = new List<string> { "CASA", "LOSA" }, CsvColumnOrder = CsvColumnOrder.PENZD };
            service.SaveSettings(settings);
            var json = File.ReadAllText(service.Paths.ConfigFile);
            Assert.Contains("\"csvColumnOrder\": \"penzd\"", json);

            var loaded = service.LoadSettings();
            Assert.Equal(new[] { "CASA", "LOSA" }, loaded.ClosedBlockCodes);
            Assert.Equal(CsvColumnOrder.PENZD, loaded.CsvColumnOrder);

            // Un config.json anterior sin estos campos conserva los valores por defecto.
            File.WriteAllText(service.Paths.ConfigFile, "{ \"offsetMax\": 10 }");
            var older = service.LoadSettings();
            Assert.Equal(new[] { "CASA" }, older.ClosedBlockCodes);
            Assert.Equal(CsvColumnOrder.PNEZD, older.CsvColumnOrder);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SettingsViewModel_EditsBlockModeFields()
    {
        var target = new Settings();
        var vm = new SettingsViewModel(target);
        vm.ClosedBlockCodesText = "casa, losa; muro";
        vm.CsvColumnOrderLabel = vm.CsvColumnOrderOptions[1];
        Assert.True(vm.Apply());
        Assert.Equal(new[] { "casa", "losa", "muro" }, target.ClosedBlockCodes);
        Assert.Equal(CsvColumnOrder.PENZD, target.CsvColumnOrder);

        var clone = target.Clone();
        clone.ClosedBlockCodes.Add("x");
        Assert.Equal(3, target.ClosedBlockCodes.Count);
    }
}
