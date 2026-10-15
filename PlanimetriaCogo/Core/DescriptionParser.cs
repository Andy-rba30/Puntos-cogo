using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Descripción de campo descompuesta (§3.1): código base, lado explícito, marcas de inicio/fin y serie.
/// Todo normalizado (mayúsculas, sin tildes).
/// </summary>
public sealed record ParsedDescription(
    string BaseCode,
    Side? ExplicitSide,
    bool IsStart,
    bool IsEnd,
    int? Series,
    IReadOnlyList<string> Marks)
{
    public bool HasMarks => Marks.Count > 0 || Series.HasValue;
}

/// <summary>
/// Separa una descripción en código base + marcas, según las listas configurables de <see cref="Settings"/>:
/// <list type="bullet">
///   <item>"CUNETA I" → base CUNETA, lado explícito Izquierda.</item>
///   <item>"CUNETA F" → base CUNETA, marca de fin (corte con el siguiente punto del grupo).</item>
///   <item>"CUNETA INI" / "CUNETA.." → base CUNETA, marca de inicio (corte con el anterior).</item>
///   <item>"CUNETA2" / "CUNETA 2" → base CUNETA, serie 2 (solo si la base tiene 2 o más letras).</item>
/// </list>
/// El primer token siempre es el código base; los siguientes se interpretan solo si coinciden con una
/// marca configurada. Un token desconocido ("CUNETA ROTA") se conserva dentro del código base para que
/// el usuario lo vea tal cual en la ventana y decida.
/// Con <see cref="Settings.UseDescriptionMarks"/> apagado solo se separa la serie.
/// </summary>
public sealed class DescriptionParser
{
    private readonly HashSet<string> _start;
    private readonly HashSet<string> _end;
    private readonly HashSet<string> _left;
    private readonly HashSet<string> _right;
    private readonly bool _useMarks;
    private readonly bool _parseSeries;

    public DescriptionParser(Settings settings) : this(settings, parseSeries: true)
    {
    }

    private DescriptionParser(Settings settings, bool parseSeries)
    {
        _parseSeries = parseSeries;
        _useMarks = settings.UseDescriptionMarks;
        _start = ToSet(settings.StartMarks);
        _end = ToSet(settings.EndMarks);
        _left = ToSet(settings.LeftMarks);
        _right = ToSet(settings.RightMarks);
    }

    /// <summary>Parser que no interpreta marcas ni series: el código base es la descripción normalizada completa.</summary>
    public static DescriptionParser None { get; } = new(new Settings { UseDescriptionMarks = false }, parseSeries: false);

    private static HashSet<string> ToSet(IEnumerable<string> marks) =>
        new(marks.Select(DescriptionNormalizer.Normalize).Where(m => m.Length > 0), StringComparer.Ordinal);

    public ParsedDescription Parse(string? description)
    {
        var norm = DescriptionNormalizer.Normalize(description);
        if (norm.Length == 0 || DescriptionNormalizer.IsEmptyOrNumeric(norm))
            return new ParsedDescription(norm, null, false, false, null, Array.Empty<string>());

        var tokens = norm.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var marks = new List<string>();
        Side? side = null;
        bool isStart = false, isEnd = false;
        int? series = null;

        // Marcas pegadas al final del primer token: "CUNETA.." → base CUNETA + marca "..".
        if (_useMarks)
        {
            foreach (var mark in _start.Concat(_end).Where(m => !char.IsLetterOrDigit(m[0])).OrderByDescending(m => m.Length))
            {
                if (tokens[0].Length > mark.Length && tokens[0].EndsWith(mark, StringComparison.Ordinal))
                {
                    tokens[0] = tokens[0].Substring(0, tokens[0].Length - mark.Length);
                    tokens.Insert(1, mark);
                    break;
                }
            }
        }

        // Serie como sufijo numérico del primer token: "CUNETA2" → CUNETA, serie 2.
        var baseToken = tokens[0];
        var digits = 0;
        while (digits < baseToken.Length && char.IsDigit(baseToken[baseToken.Length - 1 - digits]))
            digits++;
        if (_parseSeries && digits > 0 && digits < baseToken.Length && baseToken.Length - digits >= 2)
        {
            series = int.Parse(baseToken.Substring(baseToken.Length - digits));
            tokens[0] = baseToken.Substring(0, baseToken.Length - digits);
        }

        // Tokens siguientes: marcas configuradas o serie numérica; lo demás queda en el código base.
        var baseTokens = new List<string> { tokens[0] };
        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (_useMarks && _start.Contains(token)) { isStart = true; marks.Add(token); continue; }
            if (_useMarks && _end.Contains(token)) { isEnd = true; marks.Add(token); continue; }
            if (_useMarks && _left.Contains(token)) { side = Side.Left; marks.Add(token); continue; }
            if (_useMarks && _right.Contains(token)) { side = Side.Right; marks.Add(token); continue; }
            if (_parseSeries && series == null && token.All(char.IsDigit) && baseTokens[0].Length >= 2)
            {
                series = int.Parse(token);
                continue;
            }
            baseTokens.Add(token);
        }

        return new ParsedDescription(string.Join(" ", baseTokens), side, isStart, isEnd, series, marks);
    }

    /// <summary>
    /// Reconstruye la descripción con otro código base conservando serie y marcas:
    /// Parse("cuneta f").Rebuild("CUNETA") → "CUNETA F"; Parse("cuneta2").Rebuild("CUNETA") → "CUNETA2".
    /// Se usa en la opción "actualizar descripciones en el dibujo".
    /// </summary>
    public static string Rebuild(ParsedDescription parsed, string newBaseCode)
    {
        var sb = new StringBuilder(DescriptionNormalizer.Normalize(newBaseCode));
        if (parsed.Series.HasValue)
            sb.Append(parsed.Series.Value);
        foreach (var mark in parsed.Marks)
        {
            if (char.IsLetterOrDigit(mark[0]))
                sb.Append(' ');
            sb.Append(mark);
        }
        return sb.ToString();
    }
}
