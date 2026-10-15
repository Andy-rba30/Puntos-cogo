using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using PlanimetriaCogo.Acad;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;
using PlanimetriaCogo.UI;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(PlanimetriaCogo.Commands))]

namespace PlanimetriaCogo;

/// <summary>Comandos del plugin.</summary>
public class Commands
{
    /// <summary>
    /// PLANIMETRIA: selecciona un Alignment y puntos COGO, abre la ventana de clasificación,
    /// calcula lado por StationOffset con modo continuidad y dibuja una Polyline3d por
    /// (código, lado) en su capa. Una sola transacción; no modifica ningún CogoPoint.
    /// </summary>
    [CommandMethod("PLANIMETRIA", CommandFlags.Modal)]
    public void Planimetria()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null)
            return;
        var ed = doc.Editor;
        var db = doc.Database;
        var settings = new Settings();

        // 1. Eje de referencia.
        var axisOptions = new PromptEntityOptions("\nSeleccione el alineamiento (eje de referencia): ");
        axisOptions.SetRejectMessage("\nLa entidad debe ser un Alignment de Civil 3D.");
        axisOptions.AddAllowedClass(typeof(Alignment), true);
        var axisResult = ed.GetEntity(axisOptions);
        if (axisResult.Status != PromptStatus.OK)
            return;

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
            var alignment = (Alignment)tr.GetObject(axisResult.ObjectId, OpenMode.ForRead);
            var points = CogoPointReader.Read(tr, pointIds);
            ed.WriteMessage($"\nAlineamiento: {alignment.Name}. Puntos leídos: {points.Count}.");

            // 3. Ventana de clasificación de descripciones.
            var catalog = CodeCatalog.Default;
            var rules = ClassificationProposer.BuildRules(points, catalog, settings);
            var viewModel = new ClassificationViewModel(rules, catalog, settings);
            var window = new ClassificationWindow(viewModel);
            if (AcadApp.ShowModalWindow(window) != true)
            {
                ed.WriteMessage("\nPLANIMETRIA cancelado.");
                return; // el using descarta la transacción sin cambios
            }

            // 4. Cálculo: proyección con continuidad, lado, agrupación y cortes.
            var axis = new AlignmentAxis(alignment, settings);
            var result = new PlanimetryEngine(settings).Run(points, viewModel.ToRules(), axis);

            // 5. Dibujo en una sola transacción.
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            foreach (var line in result.Lines)
            {
                var layerId = LayerManager.EnsureLayer(db, tr, line.Layer, line.ColorIndex);
                GeometryBuilder.AddPolyline3d(tr, modelSpace, line, layerId);
            }

            tr.Commit();
            ReportWriter.Write(ed, result);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nError en PLANIMETRIA: {ex.Message}");
        }
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
