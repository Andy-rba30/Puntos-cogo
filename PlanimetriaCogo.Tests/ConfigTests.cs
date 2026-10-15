using System;
using System.IO;
using System.Linq;
using PlanimetriaCogo.Config;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using Xunit;

namespace PlanimetriaCogo.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlanimetriaCogoTests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { /* sin importancia en pruebas */ }
    }
}

public class ConfigServiceTests
{
    [Fact]
    public void LoadSettings_MissingFile_WritesDefaults()
    {
        using var dir = new TempDir();
        var service = new ConfigService(new ConfigPaths(dir.Path));

        var settings = service.LoadSettings();

        Assert.Equal(40.0, settings.OffsetMax);
        Assert.True(File.Exists(service.Paths.ConfigFile));
        Assert.Null(service.LastError);
        var json = File.ReadAllText(service.Paths.ConfigFile);
        Assert.Contains("\"offsetMax\": 40", json);
        Assert.Contains("\"order\": \"number\"", json);
    }

    [Fact]
    public void SaveAndLoadSettings_RoundTrip()
    {
        using var dir = new TempDir();
        var service = new ConfigService(new ConfigPaths(dir.Path));
        var settings = new Settings
        {
            OffsetMax = 55.5, DistMax = 0, StationGapMax = 120, Order = PointOrder.Station, SplitSeries = true,
            UpdateDescriptionsInDrawing = true, ReportToFile = true, ReportFolder = @"D:\reportes", AxisCode = "CL"
        };
        settings.EndMarks.Add("END");

        service.SaveSettings(settings);
        var loaded = service.LoadSettings();

        Assert.Equal(55.5, loaded.OffsetMax);
        Assert.Equal(0, loaded.DistMax);
        Assert.Equal(120, loaded.StationGapMax);
        Assert.Equal(PointOrder.Station, loaded.Order);
        Assert.True(loaded.SplitSeries);
        Assert.True(loaded.UpdateDescriptionsInDrawing);
        Assert.True(loaded.ReportToFile);
        Assert.Equal(@"D:\reportes", loaded.ReportFolder);
        Assert.Equal("CL", loaded.AxisCode);
        Assert.Contains("END", loaded.EndMarks);
    }

    [Fact]
    public void LoadSettings_CorruptFile_UsesDefaultsAndReportsError()
    {
        using var dir = new TempDir();
        var paths = new ConfigPaths(dir.Path);
        File.WriteAllText(paths.ConfigFile, "{ esto no es json");
        var service = new ConfigService(paths);

        var settings = service.LoadSettings();

        Assert.Equal(30.0, settings.DistMax);
        Assert.NotNull(service.LastError);
        Assert.Equal("{ esto no es json", File.ReadAllText(paths.ConfigFile)); // no se sobreescribe
    }

    [Fact]
    public void LoadSettings_PartialFile_FillsMissingWithDefaults()
    {
        using var dir = new TempDir();
        var paths = new ConfigPaths(dir.Path);
        File.WriteAllText(paths.ConfigFile, "{ \"offsetMax\": 80, \"distMax\": -5 }");

        var settings = new ConfigService(paths).LoadSettings();

        Assert.Equal(80, settings.OffsetMax);
        Assert.Equal(30, settings.DistMax); // negativo → por defecto
        Assert.Equal(0.15, settings.AxisTolerance);
        Assert.NotEmpty(settings.EndMarks);
    }

    [Fact]
    public void LoadCatalog_MissingFile_WritesDefaultCatalog()
    {
        using var dir = new TempDir();
        var service = new ConfigService(new ConfigPaths(dir.Path));

        var catalog = service.LoadCatalog();

        Assert.Equal("CUNETA", catalog.Resolve("cumeta"));
        Assert.True(File.Exists(service.Paths.CatalogFile));
        Assert.Contains("\"code\": \"CUNETA\"", File.ReadAllText(service.Paths.CatalogFile));
    }

    [Fact]
    public void Catalog_EditedByHand_IsHonoured()
    {
        using var dir = new TempDir();
        var paths = new ConfigPaths(dir.Path);
        File.WriteAllText(paths.CatalogFile, """
            {
              "entries": [
                { "code": "ZANJA", "kind": "line", "splitBySide": true, "colorIndex": 6, "aliases": ["zj", "zanj"] },
                { "code": "POSTE", "kind": "point", "aliases": [] },
                { "code": "EJE", "kind": "line", "splitBySide": false, "colorIndex": 2, "aliases": ["e"] }
              ]
            }
            """);

        var catalog = new ConfigService(paths).LoadCatalog();

        Assert.Equal("ZANJA", catalog.Resolve("ZJ"));
        Assert.Equal(CodeKind.Point, catalog.Find("POSTE")!.Kind);
        Assert.False(catalog.Find("EJE")!.SplitBySide);
        Assert.Null(catalog.Resolve("cuneta")); // ya no está el catálogo por defecto
    }
}

public class CatalogSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEntries()
    {
        var json = CatalogSerializer.ToJson(CodeCatalog.Default);
        var catalog = CatalogSerializer.FromJson(json);

        Assert.Equal(CodeCatalog.Default.Entries.Count, catalog.Entries.Count);
        foreach (var entry in CodeCatalog.Default.Entries)
        {
            var loaded = catalog.Find(entry.Code);
            Assert.NotNull(loaded);
            Assert.Equal(entry.Kind, loaded!.Kind);
            Assert.Equal(entry.SplitBySide, loaded.SplitBySide);
            Assert.Equal(entry.ColorIndex, loaded.ColorIndex);
            Assert.Equal(entry.Aliases, loaded.Aliases);
        }
    }

    [Fact]
    public void Merge_AddsNewCodesAndAliases_SkipsKnown()
    {
        var rules = new[]
        {
            new CodeRule { Original = "cumeta", DestinationCode = "CUNETA", Kind = CodeKind.Line },        // alias ya conocido
            new CodeRule { Original = "cunetta", DestinationCode = "CUNETA", Kind = CodeKind.Line },       // alias nuevo
            new CodeRule { Original = "poste", DestinationCode = "POSTE", Kind = CodeKind.Point, ColorIndex = 3 }, // código nuevo
            new CodeRule { Original = "pst", DestinationCode = "POSTE", Kind = CodeKind.Point },           // alias del nuevo
            new CodeRule { Original = "zzz", DestinationCode = "", Kind = CodeKind.Line },                 // sin resolver
            new CodeRule { Original = "123", DestinationCode = "", Kind = CodeKind.Ignore },               // ignorar
        };

        var merged = CatalogSerializer.Merge(CodeCatalog.Default, rules, out var codes, out var aliases);

        Assert.Equal(1, codes);
        Assert.Equal(2, aliases);
        Assert.Equal("CUNETA", merged.Resolve("CUNETTA"));
        Assert.Equal("POSTE", merged.Resolve("pst"));
        Assert.Equal(CodeKind.Point, merged.Find("POSTE")!.Kind);
        Assert.Equal(3, merged.Find("POSTE")!.ColorIndex);
        Assert.Null(merged.Resolve("zzz"));
    }
}

public class ProfileTests
{
    private static CodeRule Rule(string original, string destination, CodeKind kind = CodeKind.Line, string layer = "", short color = 7) =>
        new() { Original = original, Count = 1, DestinationCode = destination, Kind = kind, LayerBase = layer, ColorIndex = color };

    [Fact]
    public void FromRules_SkipsUnresolvedKeepsIgnored()
    {
        var profile = Profile.FromRules("obra", new[]
        {
            Rule("cumeta", "CUNETA", layer: "PL-CUNETA", color: 4),
            Rule("zzz", ""),
            Rule("123", "", CodeKind.Ignore),
        });

        Assert.Equal("obra", profile.Name);
        Assert.Equal(2, profile.Entries.Count);
        Assert.Contains(profile.Entries, e => e.Original == "cumeta" && e.DestinationCode == "CUNETA" && e.ColorIndex == 4);
        Assert.Contains(profile.Entries, e => e.Original == "123" && e.Kind == CodeKind.Ignore);
    }

    [Fact]
    public void ApplyTo_MatchesExactThenNormalized_CountsChanges()
    {
        var profile = Profile.FromRules("obra", new[]
        {
            Rule("cumeta", "CUNETA", layer: "PL-CUNETA", color: 4),
            Rule("V", "VEREDA", layer: "PL-VEREDA", color: 3),
        });
        var rules = new[]
        {
            Rule("cumeta", ""),            // coincide exacto
            Rule("v", ""),                 // coincide normalizado ("V")
            Rule("otro", ""),              // sin coincidencia
            Rule("Cumeta", "CUNETA", layer: "PL-CUNETA", color: 4), // ya igual: no cuenta
        };

        var changed = profile.ApplyTo(rules);

        Assert.Equal(2, changed);
        Assert.Equal("CUNETA", rules[0].DestinationCode);
        Assert.Equal("PL-CUNETA", rules[0].LayerBase);
        Assert.Equal("VEREDA", rules[1].DestinationCode);
        Assert.Equal(3, rules[1].ColorIndex);
        Assert.Equal("", rules[2].DestinationCode);
    }

    [Fact]
    public void Store_SaveListLoad_RoundTrip()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(new ConfigPaths(dir.Path));
        var profile = Profile.FromRules("Vía norte / 2026", new[] { Rule("cumeta", "CUNETA", layer: "PL-CUNETA", color: 4) });

        var path = store.Save(profile);

        Assert.True(File.Exists(path));
        Assert.Equal("Vía norte _ 2026", Path.GetFileNameWithoutExtension(path));
        Assert.Equal(new[] { "Vía norte _ 2026" }, store.ListNames());

        var loaded = store.Load("Vía norte / 2026");
        Assert.Equal("Vía norte / 2026", loaded.Name);
        var entry = Assert.Single(loaded.Entries);
        Assert.Equal("CUNETA", entry.DestinationCode);
        Assert.Equal(4, entry.ColorIndex);

        var byPath = store.Load(path);
        Assert.Single(byPath.Entries);
    }

    [Fact]
    public void Json_UsesCamelCaseAndEnumNames()
    {
        var json = Profile.FromRules("p", new[] { Rule("casa", "CASA", CodeKind.Point, "PT-CASA") }).ToJson();
        Assert.Contains("\"kind\": \"point\"", json);
        Assert.Contains("\"destinationCode\": \"CASA\"", json);
    }
}
