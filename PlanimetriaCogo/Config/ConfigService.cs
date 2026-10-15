using System;
using System.IO;
using System.Text.Json;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Config;

/// <summary>
/// Lee y escribe <c>config.json</c> (parámetros) y <c>catalogo.json</c> (tabla de códigos).
/// Si un archivo no existe se crea con los valores por defecto. Si está dañado, se usan los valores
/// por defecto y el motivo queda en <see cref="LastError"/> para mostrarlo en la línea de comandos;
/// el archivo dañado no se sobreescribe.
/// </summary>
public sealed class ConfigService
{
    public ConfigService(ConfigPaths paths)
    {
        Paths = paths;
    }

    public ConfigPaths Paths { get; }

    /// <summary>Último error de lectura (archivo dañado), o null.</summary>
    public string? LastError { get; private set; }

    public Settings LoadSettings()
    {
        LastError = null;
        if (!File.Exists(Paths.ConfigFile))
        {
            var defaults = new Settings();
            TrySave(() => SaveSettings(defaults));
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(Paths.ConfigFile);
            var settings = JsonSerializer.Deserialize<Settings>(json, JsonDefaults.Options) ?? new Settings();
            Sanitize(settings);
            return settings;
        }
        catch (Exception ex)
        {
            LastError = $"No se pudo leer {Paths.ConfigFile}: {ex.Message}. Se usan los valores por defecto.";
            return new Settings();
        }
    }

    public void SaveSettings(Settings settings)
    {
        Paths.EnsureDirectories();
        File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(settings, JsonDefaults.Options));
    }

    public CodeCatalog LoadCatalog()
    {
        LastError = null;
        if (!File.Exists(Paths.CatalogFile))
        {
            TrySave(() => SaveCatalog(CodeCatalog.Default));
            return CodeCatalog.Default;
        }

        try
        {
            var catalog = CatalogSerializer.FromJson(File.ReadAllText(Paths.CatalogFile));
            return catalog.Entries.Count == 0 ? CodeCatalog.Default : catalog;
        }
        catch (Exception ex)
        {
            LastError = $"No se pudo leer {Paths.CatalogFile}: {ex.Message}. Se usa el catálogo por defecto.";
            return CodeCatalog.Default;
        }
    }

    public void SaveCatalog(CodeCatalog catalog)
    {
        Paths.EnsureDirectories();
        File.WriteAllText(Paths.CatalogFile, CatalogSerializer.ToJson(catalog));
    }

    /// <summary>Valores imposibles (negativos, listas nulas) vuelven al valor por defecto.</summary>
    public static void Sanitize(Settings s)
    {
        var d = new Settings();
        if (s.OffsetMax < 0 || double.IsNaN(s.OffsetMax)) s.OffsetMax = d.OffsetMax;
        if (s.DistMax < 0 || double.IsNaN(s.DistMax)) s.DistMax = d.DistMax;
        if (s.StationGapMax < 0 || double.IsNaN(s.StationGapMax)) s.StationGapMax = d.StationGapMax;
        if (s.AxisTolerance < 0 || double.IsNaN(s.AxisTolerance)) s.AxisTolerance = d.AxisTolerance;
        if (s.AxisSampleStep <= 0 || double.IsNaN(s.AxisSampleStep)) s.AxisSampleStep = d.AxisSampleStep;
        if (string.IsNullOrWhiteSpace(s.AxisCode)) s.AxisCode = d.AxisCode;
        if (string.IsNullOrWhiteSpace(s.GeneratedAxisLayer)) s.GeneratedAxisLayer = d.GeneratedAxisLayer;
        s.StartMarks ??= d.StartMarks;
        s.EndMarks ??= d.EndMarks;
        s.LeftMarks ??= d.LeftMarks;
        s.RightMarks ??= d.RightMarks;
        s.ClosedBlockCodes ??= d.ClosedBlockCodes;
        s.ReportFolder ??= "";
        if (string.IsNullOrWhiteSpace(s.SingleLayerName)) s.SingleLayerName = d.SingleLayerName;
        if (string.IsNullOrWhiteSpace(s.LineLayerPrefix)) s.LineLayerPrefix = d.LineLayerPrefix;
        if (string.IsNullOrWhiteSpace(s.PointLayerPrefix)) s.PointLayerPrefix = d.PointLayerPrefix;
        s.LeftLayerSuffix ??= d.LeftLayerSuffix;
        s.RightLayerSuffix ??= d.RightLayerSuffix;
    }

    private void TrySave(Action save)
    {
        try
        {
            save();
        }
        catch (Exception ex)
        {
            LastError = $"No se pudo escribir en {Paths.Root}: {ex.Message}";
        }
    }
}
