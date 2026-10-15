using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace PlanimetriaCogo.Acad;

/// <summary>Crea capas si no existen (dentro de la transacción del comando).</summary>
public static class LayerManager
{
    public static ObjectId EnsureLayer(Database db, Transaction tr, string name, short colorIndex)
    {
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name))
            return layers[name];

        layers.UpgradeOpen();
        var record = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex is > 0 and < 256 ? colorIndex : (short)7)
        };
        var id = layers.Add(record);
        tr.AddNewlyCreatedDBObject(record, true);
        return id;
    }
}
