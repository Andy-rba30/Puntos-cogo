using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using PlanimetriaCogo.Acad;
using PlanimetriaCogo.Config;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using PlanimetriaCogo.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(PlanimetriaCogo.Commands))]

namespace PlanimetriaCogo;

/// <summary>Comandos del plugin: PLANIMETRIA, PLANIMETRIACONFIG, PLANIMETRIAEJE y PLANIMETRIACSV.</summary>
public class Commands
{
    private enum AxisKind { Alignment, Polyline, Points }

    /// <summary>
    /// PLANIMETRIA: elige el eje (Alignment, polilínea o puntos EJE), los puntos COGO, abre la ventana de
    /// clasificación, calcula lado con modo continuidad y dibuja una Polyline3d por (código, lado) en su capa.
    /// Una sola transacción. Solo modifica CogoPoint si el usuario activa esas opciones.
    /// </summary>
    [CommandMethod("PLANIMETRIA", CommandFlags.Modal)]
    public void Planimetria()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        // 0. Configuración (config.json y catalogo.json; se crean con valores por defecto si no existen).
        var config = new ConfigService(ConfigPaths.Default);
        var settings = config.LoadSettings();
        if (config.LastError != null) ed.WriteMessage("\n" + config.LastError);
        var catalog = config.LoadCatalog();
        if (config.LastError != null) ed.WriteMessage("\n" + config.LastError);

        // 1. Eje de referencia.
        var axisKind = PromptForAxisKind(ed);
        if (axisKind == null)
            return;
        var axisId = ObjectId.Null;
        if (axisKind != AxisKind.Points)
        {
            var axisResult = PromptForAxisEntity(ed, axisKind.Value);
            if (axisResult == null)
                return;
            axisId = axisResult.Value;
        }

        // 2. Puntos: todos o selección en pantalla.
        var pointIds = PromptForPoints(ed);
        if (pointIds == null)
            return;
        if (pointIds.Length == 0)
        {
            ed.WriteMessage("\nNo hay puntos COGO que procesar.");
            return;
        }

        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            var points = CogoPointReader.Read(tr, pointIds);
            var idByHandle = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
            foreach (var id in pointIds)
                idByHandle[id.Handle.ToString()] = id;
            ed.WriteMessage($"\nPuntos leídos: {points.Count}.");

            // 3. Ventana de clasificación de descripciones.
            var rules = ClassificationProposer.BuildRules(points, catalog, settings);
            var viewModel = new ClassificationViewModel(rules, catalog, settings, new ProfileStore(config.Paths), config)
            {
                AxisFromPoints = axisKind == AxisKind.Points
            };
            var window = new ClassificationWindow(viewModel);
            if (AcadApp.ShowModalWindow(window) != true)
            {
                ed.WriteMessage("\nPLANIMETRIA cancelado.");
                return; // el using descarta la transacción sin cambios
            }
            TrySaveSettings(ed, config, settings);

            // 4. Eje.
            IAxis axis;
            BuiltAxis? builtAxis = null;
            string axisDescription;
            switch (axisKind)
            {
                case AxisKind.Alignment:
                {
                    var alignment = (Alignment)tr.GetObject(axisId, OpenMode.ForRead);
                    axis = new AlignmentAxis(alignment, settings);
                    axisDescription = $"alineamiento {alignment.Name}";
                    break;
                }
                case AxisKind.Polyline:
                {
                    var curve = (Curve)tr.GetObject(axisId, OpenMode.ForRead);
                    var polylineAxis = new PolylineAxis(curve, settings);
                    axis = polylineAxis;
                    axisDescription = $"polilínea {curve.GetType().Name} ({polylineAxis.Length:0.00} m, progresiva 0 en el primer vértice)";
                    break;
                }
                default:
                {
                    builtAxis = AxisBuilder.FromPoints(points, viewModel.ToRules(), settings);
                    axis = builtAxis.Axis;
                    axisDescription = $"{builtAxis.Points.Count} puntos {settings.AxisCode} ({builtAxis.Length:0.00} m)"
                                      + (builtAxis.DroppedDuplicates > 0 ? $", {builtAxis.DroppedDuplicates} duplicados omitidos" : "");
                    break;
                }
            }
            ed.WriteMessage($"\nEje: {axisDescription}.");

            // 5. Cálculo: proyección con continuidad, lado, agrupación, orden y cortes.
            var result = new PlanimetryEngine(settings).Run(points, viewModel.ToRules(), axis);
            result.AxisDescription = axisDescription;

            // 6. Dibujo en la misma transacción.
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            foreach (var line in result.Lines)
            {
                // Capa única: un solo color (el de la capa) para todas las líneas, que se dibujan ByLayer.
                var layerId = LayerManager.EnsureLayer(db, tr, line.Layer, settings.SingleLayer ? settings.SingleLayerColor : line.ColorIndex);
                GeometryBuilder.AddPolyline3d(tr, modelSpace, line, layerId);
            }

            if (builtAxis != null && settings.KeepGeneratedAxis)
            {
                var layerId = LayerManager.EnsureLayer(db, tr, LayerNames.Sanitize(settings.GeneratedAxisLayer), 2);
                GeometryBuilder.AddPolyline3d(tr, modelSpace, builtAxis.Points, layerId);
                ed.WriteMessage($"\nEje generado conservado en la capa {settings.GeneratedAxisLayer}.");
            }

            // 7. Acciones opcionales sobre los CogoPoint.
            if (settings.MovePointObjectsToLayer && result.PointObjects.Count > 0)
            {
                var moved = CogoPointUpdater.MoveToLayers(tr, db, idByHandle, result.PointObjects);
                ed.WriteMessage($"\nObjetos puntuales movidos a su capa: {moved}.");
            }
            if (settings.UpdateDescriptionsInDrawing && result.DescriptionChanges.Count > 0)
            {
                var updated = CogoPointUpdater.UpdateDescriptions(tr, idByHandle, result.DescriptionChanges);
                ed.WriteMessage($"\nDescripciones actualizadas en el dibujo: {updated}.");
            }

            tr.Commit();
            ReportWriter.Write(ed, result);
            ReportWriter.WriteFiles(ed, result, settings, config.Paths, db.Filename);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nError en PLANIMETRIA: {ex.Message}");
        }
    }

    /// <summary>PLANIMETRIACONFIG: edita los parámetros (tolerancias, reglas de corte, marcas, opciones) y los guarda en config.json.</summary>
    [CommandMethod("PLANIMETRIACONFIG", CommandFlags.Modal)]
    public void PlanimetriaConfig()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null)
            return;
        var ed = doc.Editor;

        var config = new ConfigService(ConfigPaths.Default);
        var settings = config.LoadSettings();
        if (config.LastError != null) ed.WriteMessage("\n" + config.LastError);

        var viewModel = new SettingsViewModel(settings, config.Paths);
        var window = new SettingsWindow(viewModel);
        if (AcadApp.ShowModalWindow(window) != true)
        {
            ed.WriteMessage("\nPLANIMETRIACONFIG cancelado.");
            return;
        }

        if (TrySaveSettings(ed, config, settings))
            ed.WriteMessage($"\nParámetros guardados en {config.Paths.ConfigFile}");
    }

    /// <summary>
    /// PLANIMETRIAEJE (§9.3): construye el eje desde los puntos cuyo código es el del eje (por defecto EJE,
    /// con sus alias del catálogo) ordenados por número y lo dibuja como Polyline3d. No crea Alignment.
    /// </summary>
    [CommandMethod("PLANIMETRIAEJE", CommandFlags.Modal)]
    public void PlanimetriaEje()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        var config = new ConfigService(ConfigPaths.Default);
        var settings = config.LoadSettings();
        var catalog = config.LoadCatalog();

        var pointIds = PromptForPoints(ed);
        if (pointIds == null)
            return;

        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            var points = CogoPointReader.Read(tr, pointIds);
            var rules = ClassificationProposer.BuildRules(points, catalog, settings);
            var built = AxisBuilder.FromPoints(points, rules, settings);

            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var layerId = LayerManager.EnsureLayer(db, tr, LayerNames.Sanitize(settings.GeneratedAxisLayer), 2);
            GeometryBuilder.AddPolyline3d(tr, modelSpace, built.Points, layerId);
            tr.Commit();

            ed.WriteMessage($"\nEje dibujado en {settings.GeneratedAxisLayer}: {built.Points.Count} puntos {settings.AxisCode}, {built.Length:0.00} m"
                            + (built.DroppedDuplicates > 0 ? $", {built.DroppedDuplicates} duplicados omitidos" : "") + ".");
            ed.WriteMessage("\nPuede usarlo como eje en PLANIMETRIA (opción Polilinea) o convertirlo en alineamiento con las herramientas de Civil 3D.");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nError en PLANIMETRIAEJE: {ex.Message}");
        }
    }

    /// <summary>
    /// PLANIMETRIACSV (modo por bloques): lee un archivo de puntos tal como se tomó en campo (número, norte, este,
    /// cota, descripción), abre la ventana de clasificación en modo bloques y dibuja una Polyline3d por cada bloque
    /// de filas consecutivas con el mismo código destino, en el orden del archivo, sin eje ni lados. Los bloques de
    /// los códigos configurados (CASA…) se cierran como polígono. No crea ni modifica CogoPoint.
    /// </summary>
    [CommandMethod("PLANIMETRIACSV", CommandFlags.Modal)]
    public void PlanimetriaCsv()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;

        var config = new ConfigService(ConfigPaths.Default);
        var settings = config.LoadSettings();
        if (config.LastError != null) ed.WriteMessage("\n" + config.LastError);
        var catalog = config.LoadCatalog();
        if (config.LastError != null) ed.WriteMessage("\n" + config.LastError);

        // 1. Archivo y orden de columnas.
        var path = PromptForCsvFile(ed);
        if (path == null)
            return;
        var order = PromptForColumnOrder(ed, settings.CsvColumnOrder);
        if (order == null)
            return;
        settings.CsvColumnOrder = order.Value;

        // 2. Lectura en orden de archivo.
        CsvReadResult csv;
        try
        {
            csv = CsvPointReader.ReadFile(path, order.Value);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nNo se pudo leer el archivo: {ex.Message}");
            return;
        }
        ed.WriteMessage($"\nArchivo: {path}");
        ed.WriteMessage($"\nPuntos leídos: {csv.Points.Count} (separador: {csv.SeparatorName}, orden {order.Value})"
                        + (csv.SkippedLines > 0 ? $"; filas omitidas: {csv.SkippedLines}" : "")
                        + (csv.LinesWithoutElevation > 0 ? $"; sin cota (se usa 0): {csv.LinesWithoutElevation}" : "")
                        + (csv.LinesWithoutNumber > 0 ? $"; sin número de punto (se usa la posición): {csv.LinesWithoutNumber}" : "") + ".");
        foreach (var warning in csv.Warnings)
            ed.WriteMessage("\n  " + warning);
        if (csv.Points.Count == 0)
        {
            ed.WriteMessage("\nEl archivo no contiene puntos interpretables. Revise el separador y el orden de columnas.");
            return;
        }

        // 3. Ventana de clasificación en modo bloques.
        var rules = ClassificationProposer.BuildBlockRules(csv.Points, catalog, settings);
        var viewModel = new ClassificationViewModel(rules, catalog, settings, new ProfileStore(config.Paths), config) { BlockMode = true };
        viewModel.SetBlockStats(BlockStats.ByDescription(csv.Points));
        var window = new ClassificationWindow(viewModel);
        if (AcadApp.ShowModalWindow(window) != true)
        {
            ed.WriteMessage("\nPLANIMETRIACSV cancelado.");
            return;
        }
        TrySaveSettings(ed, config, settings);

        // 4. Cálculo por bloques y dibujo en una sola transacción.
        var result = new BlockPlanimetryEngine(settings).Run(csv.Points, viewModel.ToRules());
        result.SourceDescription = $"{Path.GetFileName(path)} ({csv.Points.Count} puntos, orden {order.Value})";

        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            foreach (var line in result.Lines)
            {
                // Capa única: un solo color (el de la capa) para todas las líneas, que se dibujan ByLayer.
                var layerId = LayerManager.EnsureLayer(db, tr, line.Layer, settings.SingleLayer ? settings.SingleLayerColor : line.ColorIndex);
                GeometryBuilder.AddPolyline3d(tr, modelSpace, line, layerId);
            }
            tr.Commit();
            ReportWriter.Write(ed, result);
            ReportWriter.WriteFiles(ed, result, settings, config.Paths, db.Filename);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nError en PLANIMETRIACSV: {ex.Message}");
        }
    }

    private static string? PromptForCsvFile(Editor ed)
    {
        var options = new PromptOpenFileOptions("\nArchivo de puntos (número, norte, este, cota, descripción)")
        {
            Filter = "Archivos de puntos (*.csv;*.txt)|*.csv;*.txt|Todos los archivos (*.*)|*.*",
            DialogCaption = "PLANIMETRIACSV: archivo de puntos de campo"
        };
        var result = ed.GetFileNameForOpen(options);
        if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult))
            return null;
        if (!File.Exists(result.StringResult))
        {
            ed.WriteMessage($"\nNo existe el archivo {result.StringResult}.");
            return null;
        }
        return result.StringResult;
    }

    private static CsvColumnOrder? PromptForColumnOrder(Editor ed, CsvColumnOrder current)
    {
        var defaultKeyword = current == CsvColumnOrder.PENZD ? "PENZD" : "PNEZD";
        var options = new PromptKeywordOptions($"\nOrden de columnas [PNEZD/PENZD] <{defaultKeyword}>: ");
        options.Keywords.Add("PNEZD");
        options.Keywords.Add("PENZD");
        options.Keywords.Default = defaultKeyword;
        options.AllowNone = true;

        var keyword = ed.GetKeywords(options);
        if (keyword.Status == PromptStatus.Cancel)
            return null;
        if (keyword.Status != PromptStatus.OK)
            return current;
        return keyword.StringResult == "PENZD" ? CsvColumnOrder.PENZD : CsvColumnOrder.PNEZD;
    }

    private static bool TrySaveSettings(Editor ed, ConfigService config, Settings settings)
    {
        try
        {
            config.SaveSettings(settings);
            return true;
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nNo se pudo guardar config.json: {ex.Message}");
            return false;
        }
    }

    private static AxisKind? PromptForAxisKind(Editor ed)
    {
        var options = new PromptKeywordOptions("\nEje de referencia [Alineamiento/Polilinea/Puntos] <Alineamiento>: ");
        options.Keywords.Add("Alineamiento");
        options.Keywords.Add("Polilinea");
        options.Keywords.Add("Puntos");
        options.Keywords.Default = "Alineamiento";
        options.AllowNone = true;

        var keyword = ed.GetKeywords(options);
        if (keyword.Status == PromptStatus.Cancel)
            return null;
        if (keyword.Status != PromptStatus.OK)
            return AxisKind.Alignment;
        return keyword.StringResult switch
        {
            "Polilinea" => AxisKind.Polyline,
            "Puntos" => AxisKind.Points,
            _ => AxisKind.Alignment
        };
    }

    private static ObjectId? PromptForAxisEntity(Editor ed, AxisKind kind)
    {
        PromptEntityOptions options;
        if (kind == AxisKind.Alignment)
        {
            options = new PromptEntityOptions("\nSeleccione el alineamiento (eje de referencia): ");
            options.SetRejectMessage("\nLa entidad debe ser un Alignment de Civil 3D.");
            options.AddAllowedClass(typeof(Alignment), true);
        }
        else
        {
            options = new PromptEntityOptions("\nSeleccione la polilínea (eje de referencia; la progresiva crece en el sentido de la polilínea): ");
            options.SetRejectMessage("\nLa entidad debe ser una polilínea (2D, ligera o 3D).");
            options.AddAllowedClass(typeof(Polyline), true);
            options.AddAllowedClass(typeof(Polyline2d), true);
            options.AddAllowedClass(typeof(Polyline3d), true);
        }

        var result = ed.GetEntity(options);
        return result.Status == PromptStatus.OK ? result.ObjectId : null;
    }

    /// <summary>Devuelve los ObjectId de los puntos, o null si el usuario cancela.</summary>
    private static ObjectId[]? PromptForPoints(Editor ed)
    {
        var options = new PromptKeywordOptions("\nPuntos a procesar [Todos/Seleccion] <Todos>: ");
        options.Keywords.Add("Todos");
        options.Keywords.Add("Seleccion");
        options.Keywords.Default = "Todos";
        options.AllowNone = true;

        var keyword = ed.GetKeywords(options);
        if (keyword.Status == PromptStatus.Cancel)
            return null;

        var all = keyword.Status != PromptStatus.OK || keyword.StringResult == "Todos";
        if (all)
        {
            var civilDoc = CivilApplication.ActiveDocument;
            return civilDoc.CogoPoints.Cast<ObjectId>().ToArray();
        }

        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "AECC_COGO_POINT") });
        var selectionOptions = new PromptSelectionOptions { MessageForAdding = "\nSeleccione los puntos COGO: " };
        var selection = ed.GetSelection(selectionOptions, filter);
        if (selection.Status != PromptStatus.OK)
            return null;
        return selection.Value.GetObjectIds();
    }
}
