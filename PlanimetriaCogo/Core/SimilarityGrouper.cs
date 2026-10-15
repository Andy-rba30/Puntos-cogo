using System;
using System.Collections.Generic;
using System.Linq;

namespace PlanimetriaCogo.Core;

/// <summary>Grupo sugerido: varias descripciones originales que irían a un mismo código destino.</summary>
public sealed record SimilarityGroup(string DestinationCode, IReadOnlyList<string> Members, string Reason);

/// <summary>
/// Sugiere qué descripciones parecidas deberían unificarse (botón "Sugerir similares").
///
/// Reglas, en este orden:
///  1. Igualdad sin distinguir mayúsculas ni tildes (normalización).
///  2. Código o alias del catálogo.
///  3. Distancia de edición ≤ <see cref="MaxEditDistance"/> respecto a un código del catálogo
///     (solo para textos de 4 o más letras; con 3 letras se admite 1 error; con menos, ninguno,
///     porque "v"/"b"/"e" están a distancia 1 entre sí y se mezclarían).
///  4. Prefijo de exactamente un código del catálogo ("v" → VEREDA; "c" es ambiguo: CUNETA/CARRETERA/CASA).
///  5. Entre las descripciones que siguen sin destino, agrupación por distancia de edición o prefijo
///     ("ver" → "vereda"); el destino propuesto es el miembro con más puntos, descartando los que
///     son prefijo de otro miembro (empate: el más largo).
///  6. Una descripción suelta de 2 o más letras se propone a sí misma como código nuevo.
///     Las de una sola letra, las vacías y las numéricas quedan sin resolver.
/// Nunca se decide en silencio: el usuario revisa cada propuesta en la ventana.
/// </summary>
public sealed class SimilarityGrouper
{
    public int MaxEditDistance { get; init; } = 2;

    private sealed class NormEntry
    {
        public string Norm = "";
        public readonly List<string> Raws = new();
        public int Count;
        public string? Destination;
        public string Reason = "";
    }

    public IReadOnlyList<SimilarityGroup> Suggest(IEnumerable<(string Description, int Count)> entries, CodeCatalog catalog)
    {
        // 1. Agrupar por forma normalizada (mayúsculas, tildes, espacios).
        var byNorm = new Dictionary<string, NormEntry>(StringComparer.Ordinal);
        foreach (var (description, count) in entries)
        {
            var norm = DescriptionNormalizer.Normalize(description);
            if (DescriptionNormalizer.IsEmptyOrNumeric(norm))
                continue; // vacías y numéricas no se sugieren: van a Ignorar
            if (!byNorm.TryGetValue(norm, out var entry))
            {
                entry = new NormEntry { Norm = norm };
                byNorm[norm] = entry;
            }
            if (!entry.Raws.Contains(description))
                entry.Raws.Add(description);
            entry.Count += count;
        }

        var all = byNorm.Values.OrderByDescending(e => e.Count).ThenBy(e => e.Norm, StringComparer.Ordinal).ToList();
        var codes = catalog.Codes.ToList();

        // 2. Catálogo (código exacto o alias).
        foreach (var entry in all)
        {
            var resolved = catalog.Resolve(entry.Norm);
            if (resolved != null)
            {
                entry.Destination = resolved;
                entry.Reason = "catálogo";
            }
        }

        // 3 y 4. Similitud y prefijo contra el catálogo.
        foreach (var entry in all.Where(e => e.Destination == null))
        {
            var closest = ClosestCode(entry.Norm, codes);
            if (closest != null)
            {
                entry.Destination = closest;
                entry.Reason = "similar a código conocido";
                continue;
            }

            var prefixed = codes.Where(c => c.Length > entry.Norm.Length && c.StartsWith(entry.Norm, StringComparison.Ordinal)).ToList();
            if (prefixed.Count == 1)
            {
                entry.Destination = prefixed[0];
                entry.Reason = "prefijo de código conocido";
            }
        }

        // 5. Agrupación entre las que siguen sin destino.
        var pending = all.Where(e => e.Destination == null).ToList();
        var parent = Enumerable.Range(0, pending.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);

        // 5a. Distancia de edición y prefijos de 3 o más letras.
        for (var i = 0; i < pending.Count; i++)
        {
            for (var j = i + 1; j < pending.Count; j++)
            {
                if (AreSimilar(pending[i].Norm, pending[j].Norm))
                    Union(i, j);
            }
        }

        // 5b. Prefijos cortos ("v", "ve"): se unen solo si todo lo que empieza así cae en un único
        //     grupo ya formado y ningún código del catálogo compite ("v" → {ver, vereda} sí; "c" no).
        for (var i = 0; i < pending.Count; i++)
        {
            var shortNorm = pending[i].Norm;
            if (shortNorm.Length >= 3)
                continue;
            var roots = new HashSet<int>();
            for (var j = 0; j < pending.Count; j++)
            {
                if (j != i && pending[j].Norm.Length > shortNorm.Length && pending[j].Norm.StartsWith(shortNorm, StringComparison.Ordinal))
                    roots.Add(Find(j));
            }
            var competingCodes = codes.Count(c => c.Length > shortNorm.Length && c.StartsWith(shortNorm, StringComparison.Ordinal));
            if (roots.Count == 1 && competingCodes == 0)
                Union(i, roots.First());
        }

        foreach (var cluster in Enumerable.Range(0, pending.Count).GroupBy(Find))
        {
            var members = cluster.Select(i => pending[i]).ToList();
            if (members.Count >= 2)
            {
                // Un miembro que es prefijo estricto de otro no puede ser el destino ("v" nunca gana a "vereda").
                var complete = members
                    .Where(m => !members.Any(o => o.Norm.Length > m.Norm.Length && o.Norm.StartsWith(m.Norm, StringComparison.Ordinal)))
                    .ToList();
                var leader = complete
                    .OrderByDescending(m => m.Count)
                    .ThenByDescending(m => m.Norm.Length)
                    .ThenBy(m => m.Norm, StringComparer.Ordinal)
                    .First();
                foreach (var m in members)
                {
                    m.Destination = leader.Norm;
                    m.Reason = "similares entre sí";
                }
            }
            else if (members[0].Norm.Length >= 2)
            {
                // 6. Código nuevo propuesto tal cual.
                members[0].Destination = members[0].Norm;
                members[0].Reason = "código nuevo";
            }
        }

        // Salida agrupada por destino.
        return all
            .Where(e => e.Destination != null)
            .GroupBy(e => e.Destination!)
            .Select(g => new SimilarityGroup(
                g.Key,
                g.SelectMany(e => e.Raws).ToList(),
                string.Join(", ", g.Select(e => e.Reason).Distinct())))
            .OrderBy(g => g.DestinationCode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Mapa descripción original → código destino a partir de los grupos.</summary>
    public static IReadOnlyDictionary<string, string> ToMap(IEnumerable<SimilarityGroup> groups)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var member in group.Members)
                map[member] = group.DestinationCode;
        }
        return map;
    }

    /// <summary>Umbral de distancia según la longitud más corta de la pareja.</summary>
    private int Threshold(int minLength) => minLength >= 4 ? MaxEditDistance : minLength == 3 ? 1 : 0;

    private string? ClosestCode(string norm, List<string> codes)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var code in codes)
        {
            var threshold = Threshold(Math.Min(norm.Length, code.Length));
            if (threshold == 0)
                continue;
            var distance = Levenshtein.Distance(norm, code);
            if (distance <= threshold && distance < bestDistance)
            {
                bestDistance = distance;
                best = code;
            }
        }
        return best;
    }

    private bool AreSimilar(string a, string b)
    {
        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;

        var threshold = Threshold(shorter.Length);
        if (threshold > 0 && Levenshtein.Distance(a, b) <= threshold)
            return true;

        // Prefijo de 3 o más letras: "ver" → "vereda". Los prefijos más cortos se tratan aparte.
        return shorter.Length >= 3 && longer.StartsWith(shorter, StringComparison.Ordinal);
    }
}
