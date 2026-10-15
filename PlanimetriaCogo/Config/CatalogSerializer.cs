using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Config;

/// <summary>Una entrada del catálogo en <c>catalogo.json</c>.</summary>
public sealed class CatalogEntryDto
{
    public string Code { get; set; } = "";
    public CodeKind Kind { get; set; } = CodeKind.Line;
    public bool SplitBySide { get; set; } = true;
    public short ColorIndex { get; set; } = 7;
    public List<string> Aliases { get; set; } = new();
}

/// <summary>Archivo <c>catalogo.json</c> completo.</summary>
public sealed class CatalogFileDto
{
    /// <summary>Texto libre para quien edite el archivo a mano.</summary>
    public string Comment { get; set; } =
        "Catálogo de códigos de PlanimetriaCogo. kind: line | point | ignore. splitBySide: false para códigos sin lados (EJE). colorIndex: color ACI 1-255 de la capa.";

    public List<CatalogEntryDto> Entries { get; set; } = new();
}

/// <summary>Convierte <see cref="CodeCatalog"/> a JSON y viceversa (fase 2: la tabla de §9.2 deja de vivir en código).</summary>
public static class CatalogSerializer
{
    public static string ToJson(CodeCatalog catalog)
    {
        var dto = new CatalogFileDto
        {
            Entries = catalog.Entries.Select(e => new CatalogEntryDto
            {
                Code = e.Code,
                Kind = e.Kind,
                SplitBySide = e.SplitBySide,
                ColorIndex = e.ColorIndex,
                Aliases = e.Aliases.ToList()
            }).ToList()
        };
        return JsonSerializer.Serialize(dto, JsonDefaults.Options);
    }

    public static CodeCatalog FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<CatalogFileDto>(json, JsonDefaults.Options) ?? new CatalogFileDto();
        return new CodeCatalog(dto.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Code))
            .Select(e => new CodeCatalog.Entry(e.Code, e.Kind, e.SplitBySide, e.ColorIndex, e.Aliases ?? new List<string>())));
    }

    /// <summary>
    /// Devuelve un catálogo nuevo con las reglas resueltas que no estén aún en él (botón "Agregar al catálogo").
    /// La descripción original pasa a ser alias del código destino; los códigos nuevos se crean con el tipo,
    /// lados y color de la fila. Las filas Ignorar y sin resolver no se agregan.
    /// </summary>
    public static CodeCatalog Merge(CodeCatalog catalog, IEnumerable<CodeRule> rules, out int addedCodes, out int addedAliases)
    {
        var entries = catalog.Entries.ToDictionary(e => e.Code, e => (e.Kind, e.SplitBySide, e.ColorIndex, Aliases: e.Aliases.ToList()), System.StringComparer.Ordinal);
        addedCodes = 0;
        addedAliases = 0;

        foreach (var rule in rules.Where(r => r.IsResolved && r.Kind != CodeKind.Ignore))
        {
            var code = DescriptionNormalizer.Normalize(rule.DestinationCode);
            if (!entries.TryGetValue(code, out var entry))
            {
                entry = (rule.Kind, rule.SplitBySide, rule.ColorIndex, new List<string>());
                entries[code] = entry;
                addedCodes++;
            }

            var alias = DescriptionNormalizer.Normalize(rule.Original);
            if (alias.Length > 0 && alias != code && !DescriptionNormalizer.IsEmptyOrNumeric(alias)
                && catalog.Resolve(alias) == null && !entry.Aliases.Contains(alias))
            {
                entry.Aliases.Add(alias);
                addedAliases++;
            }
        }

        return new CodeCatalog(entries.Select(kv => new CodeCatalog.Entry(kv.Key, kv.Value.Kind, kv.Value.SplitBySide, kv.Value.ColorIndex, kv.Value.Aliases)));
    }
}
