using System;
using System.Globalization;
using System.IO;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Config;

/// <summary>Escritura del reporte a archivo (.txt completo y .csv), fase 3.</summary>
public static class ReportFiles
{
    /// <summary>
    /// Carpeta del reporte: <see cref="Settings.ReportFolder"/> si está definida; si no, la carpeta del DWG;
    /// si el DWG no está guardado, <c>%APPDATA%\PlanimetriaCogo\reportes</c>.
    /// </summary>
    public static string ResolveFolder(Settings settings, ConfigPaths paths, string? drawingPath)
    {
        if (!string.IsNullOrWhiteSpace(settings.ReportFolder))
            return settings.ReportFolder;

        if (!string.IsNullOrWhiteSpace(drawingPath))
        {
            var dir = Path.GetDirectoryName(drawingPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }

        return paths.ReportsDir;
    }

    /// <summary>Nombre base: &lt;dibujo&gt;_planimetria_&lt;fecha&gt;.</summary>
    public static string BaseName(string? drawingPath, DateTime when)
    {
        var drawing = string.IsNullOrWhiteSpace(drawingPath) ? "dibujo" : Path.GetFileNameWithoutExtension(drawingPath);
        return $"{ConfigPaths.SafeFileName(drawing)}_planimetria_{when.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Escribe ambos archivos y devuelve sus rutas.</summary>
    public static (string TextPath, string CsvPath) Write(PlanimetryResult result, Settings settings, ConfigPaths paths, string? drawingPath, DateTime when)
    {
        var folder = ResolveFolder(settings, paths, drawingPath);
        Directory.CreateDirectory(folder);
        var baseName = BaseName(drawingPath, when);
        var textPath = Path.Combine(folder, baseName + ".txt");
        var csvPath = Path.Combine(folder, baseName + ".csv");

        var drawingName = string.IsNullOrWhiteSpace(drawingPath) ? "(sin guardar)" : drawingPath;
        File.WriteAllText(textPath, ReportFormatter.FullText(result, drawingName, when), System.Text.Encoding.UTF8);
        File.WriteAllText(csvPath, ReportFormatter.Csv(result), System.Text.Encoding.UTF8);
        return (textPath, csvPath);
    }
}
