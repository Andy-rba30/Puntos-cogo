using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Config;

/// <summary>Una fila de la ventana de clasificación guardada en un perfil.</summary>
public sealed class ProfileEntry
{
    public string Original { get; set; } = "";
    public string DestinationCode { get; set; } = "";
    public CodeKind Kind { get; set; } = CodeKind.Line;
    public bool SplitBySide { get; set; } = true;
    public string LayerBase { get; set; } = "";
    public short ColorIndex { get; set; } = 7;
    public bool Draw { get; set; } = true;

    /// <summary>Unión en el modo por bloques (perfiles antiguos sin este campo: por bloque).</summary>
    public BlockJoin BlockJoin { get; set; } = BlockJoin.Block;
}

/// <summary>
/// Perfil de clasificación (§10.2 "Guardar / cargar perfil"): el mapa descripción original → código destino
/// con tipo, lados, capa y color, para reutilizarlo en otros DWG del mismo proyecto.
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public List<ProfileEntry> Entries { get; set; } = new();

    public static Profile FromRules(string name, IEnumerable<CodeRule> rules)
    {
        return new Profile
        {
            Name = name,
            SavedAt = DateTime.Now,
            Entries = rules
                .Where(r => r.IsResolved || r.Kind == CodeKind.Ignore)
                .Select(r => new ProfileEntry
                {
                    Original = r.Original,
                    DestinationCode = r.DestinationCode,
                    Kind = r.Kind,
                    SplitBySide = r.SplitBySide,
                    LayerBase = r.LayerBase,
                    ColorIndex = r.ColorIndex,
                    Draw = r.Draw,
                    BlockJoin = r.BlockJoin
                })
                .ToList()
        };
    }

    /// <summary>
    /// Aplica el perfil a las filas actuales. Coincidencia por descripción original exacta y, si no hay,
    /// por forma normalizada ("Cuneta" aplica a "cuneta"). Devuelve cuántas filas cambiaron. Las filas
    /// sin coincidencia quedan como estaban.
    /// </summary>
    public int ApplyTo(IEnumerable<CodeRule> rules)
    {
        var exact = new Dictionary<string, ProfileEntry>(StringComparer.Ordinal);
        var normalized = new Dictionary<string, ProfileEntry>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            exact.TryAdd(entry.Original, entry);
            normalized.TryAdd(DescriptionNormalizer.Normalize(entry.Original), entry);
        }

        var changed = 0;
        foreach (var rule in rules)
        {
            if (!exact.TryGetValue(rule.Original, out var entry) &&
                !normalized.TryGetValue(DescriptionNormalizer.Normalize(rule.Original), out entry))
                continue;

            // Tal cual se guardó: normalizado, o exacto si vino de "Usar nombre como código".
            var destination = (entry.DestinationCode ?? "").Trim();
            var layer = LayerNames.Sanitize(entry.LayerBase);
            var color = entry.ColorIndex is > 0 and < 256 ? entry.ColorIndex : (short)7;

            if (rule.DestinationCode == destination && rule.Kind == entry.Kind && rule.SplitBySide == entry.SplitBySide &&
                rule.LayerBase == layer && rule.ColorIndex == color && rule.Draw == entry.Draw && rule.BlockJoin == entry.BlockJoin)
                continue;

            rule.DestinationCode = destination;
            rule.Kind = entry.Kind;
            rule.SplitBySide = entry.SplitBySide;
            rule.LayerBase = layer;
            rule.ColorIndex = color;
            rule.Draw = entry.Draw;
            rule.BlockJoin = entry.BlockJoin;
            changed++;
        }
        return changed;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Options);

    public static Profile FromJson(string json) =>
        JsonSerializer.Deserialize<Profile>(json, JsonDefaults.Options) ?? new Profile();
}

/// <summary>Guarda y carga perfiles como <c>perfiles\&lt;nombre&gt;.json</c>.</summary>
public sealed class ProfileStore
{
    private readonly ConfigPaths _paths;

    public ProfileStore(ConfigPaths paths)
    {
        _paths = paths;
    }

    public string Directory => _paths.ProfilesDir;

    public string PathFor(string name) => Path.Combine(_paths.ProfilesDir, ConfigPaths.SafeFileName(name) + ".json");

    public IReadOnlyList<string> ListNames()
    {
        if (!System.IO.Directory.Exists(_paths.ProfilesDir))
            return Array.Empty<string>();
        return System.IO.Directory.GetFiles(_paths.ProfilesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string Save(Profile profile)
    {
        _paths.EnsureDirectories();
        var path = PathFor(profile.Name);
        File.WriteAllText(path, profile.ToJson());
        return path;
    }

    public Profile Load(string nameOrPath)
    {
        var path = File.Exists(nameOrPath) ? nameOrPath : PathFor(nameOrPath);
        var profile = Profile.FromJson(File.ReadAllText(path));
        if (string.IsNullOrWhiteSpace(profile.Name))
            profile.Name = Path.GetFileNameWithoutExtension(path);
        return profile;
    }
}
