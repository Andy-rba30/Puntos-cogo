using System;
using Autodesk.AutoCAD.EditorInput;
using PlanimetriaCogo.Config;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Acad;

/// <summary>Resumen del comando en la línea de comandos y, si está activo, en archivos .txt y .csv.</summary>
public static class ReportWriter
{
    private const int MaxListed = 20;

    public static void Write(Editor ed, PlanimetryResult result)
    {
        foreach (var line in ReportFormatter.Summary(result, MaxListed))
            ed.WriteMessage("\n" + line);
        ed.WriteMessage("\n");
    }

    /// <summary>Escribe los archivos del reporte si <see cref="Settings.ReportToFile"/> está activo e informa las rutas.</summary>
    public static void WriteFiles(Editor ed, PlanimetryResult result, Settings settings, ConfigPaths paths, string? drawingPath)
    {
        if (!settings.ReportToFile)
            return;
        try
        {
            var (textPath, csvPath) = ReportFiles.Write(result, settings, paths, drawingPath, DateTime.Now);
            ed.WriteMessage($"\nReporte guardado en:\n  {textPath}\n  {csvPath}\n");
        }
        catch (Exception ex)
        {
            ed.WriteMessage($"\nNo se pudo escribir el reporte en archivo: {ex.Message}\n");
        }
    }
}
