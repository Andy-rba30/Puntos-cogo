using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using PlanimetriaCogo.Core;

namespace PlanimetriaCogo.Acad;

/// <summary>
/// Las dos únicas modificaciones opcionales sobre los CogoPoint (apagadas por defecto, §10.1 y §10.2):
/// mover objetos puntuales a su capa y reescribir la descripción cruda con el código destino.
/// Nunca cambia número, nombre ni cota. Todo dentro de la transacción del comando.
/// </summary>
public static class CogoPointUpdater
{
    /// <summary>Mueve cada objeto puntual a la capa de su fila (la crea si no existe). Devuelve cuántos cambiaron.</summary>
    public static int MoveToLayers(Transaction tr, Database db, IReadOnlyDictionary<string, ObjectId> idByHandle, IEnumerable<PointObject> pointObjects)
    {
        var moved = 0;
        var layerIds = new Dictionary<string, ObjectId>(System.StringComparer.Ordinal);

        foreach (var po in pointObjects)
        {
            if (string.IsNullOrWhiteSpace(po.Layer))
                continue;
            if (!idByHandle.TryGetValue(po.Point.SourceId, out var id) || id.IsNull || id.IsErased)
                continue;
            if (tr.GetObject(id, OpenMode.ForRead, false) is not CogoPoint cogo)
                continue;

            if (!layerIds.TryGetValue(po.Layer, out var layerId))
            {
                layerId = LayerManager.EnsureLayer(db, tr, po.Layer, po.ColorIndex);
                layerIds[po.Layer] = layerId;
            }
            if (cogo.LayerId == layerId)
                continue;

            cogo.UpgradeOpen();
            cogo.LayerId = layerId;
            moved++;
        }
        return moved;
    }

    /// <summary>Reescribe la descripción cruda de los puntos indicados. Devuelve cuántos cambiaron.</summary>
    public static int UpdateDescriptions(Transaction tr, IReadOnlyDictionary<string, ObjectId> idByHandle, IEnumerable<DescriptionChange> changes)
    {
        var updated = 0;
        foreach (var change in changes)
        {
            if (!idByHandle.TryGetValue(change.Point.SourceId, out var id) || id.IsNull || id.IsErased)
                continue;
            if (tr.GetObject(id, OpenMode.ForRead, false) is not CogoPoint cogo)
                continue;
            if (cogo.RawDescription == change.NewDescription)
                continue;

            cogo.UpgradeOpen();
            cogo.RawDescription = change.NewDescription;
            updated++;
        }
        return updated;
    }
}
