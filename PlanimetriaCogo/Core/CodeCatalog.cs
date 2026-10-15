using System;
using System.Collections.Generic;
using System.Linq;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Tabla de códigos conocidos con sus alias (sección 9.2 del planteamiento).
/// Todo se compara en forma normalizada. En la fase 1 la tabla vive en código; en la fase 2
/// pasará a JSON.
/// </summary>
public sealed class CodeCatalog
{
    public sealed record Entry(
        string Code,
        CodeKind Kind,
        bool SplitBySide,
        short ColorIndex,
        IReadOnlyList<string> Aliases);

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _byCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _byAlias = new(StringComparer.Ordinal);

    public CodeCatalog(IEnumerable<Entry> entries)
    {
        foreach (var raw in entries)
        {
            var code = DescriptionNormalizer.Normalize(raw.Code);
            var aliases = raw.Aliases.Select(DescriptionNormalizer.Normalize)
                .Where(a => a.Length > 0 && a != code)
                .Distinct()
                .ToList();
            var entry = raw with { Code = code, Aliases = aliases };
            _entries.Add(entry);
            _byCode[code] = entry;
            foreach (var alias in aliases)
                _byAlias.TryAdd(alias, entry);
        }
    }

    public IReadOnlyList<Entry> Entries => _entries;

    public IEnumerable<string> Codes => _entries.Select(e => e.Code);

    /// <summary>Catálogo vacío (útil para pruebas de similitud pura).</summary>
    public static CodeCatalog Empty => new(Array.Empty<Entry>());

    /// <summary>Catálogo inicial derivado del LandXML del proyecto (§9.2).</summary>
    public static CodeCatalog Default { get; } = CreateDefault();

    private static CodeCatalog CreateDefault()
    {
        static Entry Line(string code, short color, params string[] aliases) =>
            new(code, CodeKind.Line, true, color, aliases);

        static Entry Point(string code, params string[] aliases) =>
            new(code, CodeKind.Point, true, 7, aliases);

        return new CodeCatalog(new[]
        {
            Line("TN", 8, "tn", "t", "tm"),
            Line("CUNETA", 4, "cuneta", "CNT", "cumeta", "cuenta", "cunet", "cunetq", "cunera", "cunetw", "cumet", "vunet", "vuneta"),
            Line("VEREDA", 3, "v", "ver", "VRD", "ve"),
            Line("BORDE", 1, "b", "BRD", "BD"),
            new Entry("EJE", CodeKind.Line, SplitBySide: false, ColorIndex: 2, Aliases: new[] { "e", "CL" }),
            Line("CARRETERA", 5, "crr", "CRC"),
            Point("CASA"),
            Point("MURO"),
            Point("PILON", "pilona"),
            Point("BM"),
            Point("PG"),
            Point("BZ"),
            Point("AGUA"),
            Point("ALC"),
            Point("TUBO"),
            Point("LOSA"),
            Point("SH"),
            Point("GP"),
            Point("Q"),
            Point("PT"),
        });
    }

    /// <summary>Entrada por código exacto (normalizado) o null.</summary>
    public Entry? Find(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        return _byCode.TryGetValue(DescriptionNormalizer.Normalize(code), out var entry) ? entry : null;
    }

    /// <summary>
    /// Resuelve una descripción (cruda o normalizada) a un código del catálogo por igualdad o por alias.
    /// Devuelve null si no está en la tabla. No aplica similitud: eso lo hace <see cref="SimilarityGrouper"/>.
    /// </summary>
    public string? Resolve(string? description)
    {
        var normalized = DescriptionNormalizer.Normalize(description);
        if (normalized.Length == 0)
            return null;
        if (_byCode.TryGetValue(normalized, out var exact))
            return exact.Code;
        if (_byAlias.TryGetValue(normalized, out var byAlias))
            return byAlias.Code;
        return null;
    }

    /// <summary>true si la descripción corresponde (por código o alias) a un objeto puntual.</summary>
    public bool IsPointObject(string? description)
    {
        var code = Resolve(description);
        return code != null && _byCode[code].Kind == CodeKind.Point;
    }
}
