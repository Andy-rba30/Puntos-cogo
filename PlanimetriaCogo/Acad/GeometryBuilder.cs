using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PlanimetriaCogo.Core;

namespace PlanimetriaCogo.Acad;

/// <summary>Crea las Polyline3d de salida.</summary>
public static class GeometryBuilder
{
    public static ObjectId AddPolyline3d(Transaction tr, BlockTableRecord space, LineSequence line, ObjectId layerId)
    {
        var vertices = new Point3dCollection();
        foreach (var p in line.Points)
            vertices.Add(new Point3d(p.Point.X, p.Point.Y, p.Point.Z));

        var polyline = new Polyline3d(Poly3dType.SimplePoly, vertices, false)
        {
            LayerId = layerId
        };

        var id = space.AppendEntity(polyline);
        tr.AddNewlyCreatedDBObject(polyline, true);
        return id;
    }
}
