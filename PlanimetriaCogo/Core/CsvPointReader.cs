using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Core;

/// <summary>Resultado de leer un archivo de puntos: los puntos en orden de archivo y lo que no se pudo interpretar.</summary>
public sealed class CsvReadResult
{
    /// <summary>Puntos en el orden del archivo (<see cref="SurveyPoint.Sequence"/> 1, 2, 3…).</summary>
    public List<SurveyPoint> Points { get; } = new();

    /// <summary>Separador detectado: ',', ';' o tabulador.</summary>
    public char Separator { get; set; } = ',';

    /// <summary>Filas que no se pudieron interpretar (encabezado, columnas insuficientes, coordenadas no numéricas).</summary>
    public int SkippedLines { get; set; }

    /// <summary>Filas sin cota (se usa 0).</summary>
    public int LinesWithoutElevation { get; set; }

    /// <summary>Filas cuyo número de punto no es entero: se usa la posición en el archivo.</summary>
    public int LinesWithoutNumber { get; set; }

    /// <summary>Detalle de las primeras filas omitidas, para la línea de comandos.</summary>
    public List<string> Warnings { get; } = new();

    public string SeparatorName => Separator switch { '\t' => "tabulador", ';' => "punto y coma", _ => "coma" };
}

/// <summary>
/// Lee un archivo de puntos de campo (CSV o texto) con columnas número, norte, este, cota, descripción
/// (<see cref="CsvColumnOrder.PNEZD"/>) o número, este, norte, cota, descripción (<see cref="CsvColumnOrder.PENZD"/>),
/// tal como lo exporta el colector. Conserva el orden del archivo: es la base del modo por bloques.
/// <list type="bullet">
///   <item>Separador detectado entre coma, punto y coma y tabulador (el más frecuente en las primeras filas).</item>
///   <item>Con separador ';' o tabulador se admite coma decimal.</item>
///   <item>Campos entre comillas (una descripción con el separador dentro).</item>
///   <item>Las filas que no se pueden interpretar (encabezado, vacías, coordenadas no numéricas) se omiten y se cuentan.</item>
///   <item>Sin cota: 0. Número de punto no entero: se usa la posición en el archivo.</item>
///   <item>Con más de cinco columnas, las sobrantes se unen a la descripción con un espacio.</item>
/// </list>
/// </summary>
public static class CsvPointReader
{
    private const int MaxWarnings = 10;
    private const int DetectionLines = 20;

    public static CsvReadResult ReadFile(string path, CsvColumnOrder order)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(ReadLines(reader), order);
    }

    private static IEnumerable<string> ReadLines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
            yield return line;
    }

    public static CsvReadResult Parse(IEnumerable<string> lines, CsvColumnOrder order)
    {
        var all = new List<string>(lines);
        var result = new CsvReadResult { Separator = DetectSeparator(all) };
        var allowDecimalComma = result.Separator != ',';

        for (var i = 0; i < all.Count; i++)
        {
            var lineNumber = i + 1;
            var line = all[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = Split(line, result.Separator);
            if (fields.Count < 4)
            {
                Skip(result, lineNumber, line, "menos de 4 columnas");
                continue;
            }

            var northIndex = order == CsvColumnOrder.PNEZD ? 1 : 2;
            var eastIndex = order == CsvColumnOrder.PNEZD ? 2 : 1;
            if (!TryParseDouble(fields[northIndex], allowDecimalComma, out var north) ||
                !TryParseDouble(fields[eastIndex], allowDecimalComma, out var east))
            {
                Skip(result, lineNumber, line, "coordenadas no numéricas");
                continue;
            }

            var elevation = 0.0;
            if (!TryParseDouble(fields[3], allowDecimalComma, out elevation))
            {
                elevation = 0.0;
                result.LinesWithoutElevation++;
            }

            var sequence = result.Points.Count + 1;
            if (!TryParseNumber(fields[0], allowDecimalComma, out var number))
            {
                number = (uint)sequence;
                result.LinesWithoutNumber++;
            }

            var description = fields.Count > 4 ? JoinDescription(fields) : "";

            result.Points.Add(new SurveyPoint
            {
                Number = number,
                Name = fields[0].Trim(),
                X = east,
                Y = north,
                Z = elevation,
                RawDescription = description,
                SourceId = lineNumber.ToString("D7", CultureInfo.InvariantCulture),
                Sequence = sequence
            });
        }

        return result;
    }

    /// <summary>Separador más frecuente en las primeras filas no vacías; en empate gana ';', luego tabulador, luego ','.</summary>
    public static char DetectSeparator(IEnumerable<string> lines)
    {
        int semicolons = 0, tabs = 0, commas = 0, seen = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            foreach (var ch in line)
            {
                if (ch == ';') semicolons++;
                else if (ch == '\t') tabs++;
                else if (ch == ',') commas++;
            }
            if (++seen >= DetectionLines)
                break;
        }
        if (semicolons >= tabs && semicolons >= commas && semicolons > 0) return ';';
        if (tabs >= commas && tabs > 0) return '\t';
        return ',';
    }

    /// <summary>Divide una fila respetando campos entre comillas ("" dentro de un campo = comilla literal).</summary>
    public static List<string> Split(string line, char separator)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"' && sb.Length == 0)
                inQuotes = true;
            else if (ch == separator)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static string JoinDescription(List<string> fields)
    {
        var sb = new StringBuilder();
        for (var i = 4; i < fields.Count; i++)
        {
            var part = fields[i].Trim();
            if (part.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(part);
        }
        return sb.ToString();
    }

    private static bool TryParseDouble(string text, bool allowDecimalComma, out double value)
    {
        var t = text.Trim();
        if (allowDecimalComma)
            t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool TryParseNumber(string text, bool allowDecimalComma, out uint number)
    {
        var t = text.Trim();
        if (uint.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return true;
        // "23.0" exportado por una hoja de cálculo.
        if (TryParseDouble(t, allowDecimalComma, out var d) && d >= 0 && d <= uint.MaxValue && Math.Abs(d - Math.Round(d)) < 1e-9)
        {
            number = (uint)Math.Round(d);
            return true;
        }
        number = 0;
        return false;
    }

    private static void Skip(CsvReadResult result, int lineNumber, string line, string reason)
    {
        result.SkippedLines++;
        if (result.Warnings.Count < MaxWarnings)
        {
            var shown = line.Length > 60 ? line.Substring(0, 60) + "…" : line;
            result.Warnings.Add($"fila {lineNumber} omitida ({reason}): {shown}");
        }
    }
}
