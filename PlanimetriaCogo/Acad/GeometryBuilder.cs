using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Acad;

/// <summary>Crea las Polyline3d de salida.</summary>
public static class GeometryBuilder
{
    /// <summary>Polyline3d por la secuencia (color ByLayer); cerrada si <see cref="LineSequence.Closed"/> (bloques cerrados del modo CSV).</summary>
    public static ObjectId AddPolyline3d(Transaction tr, BlockTableRecord space, LineSequence line, ObjectId layerId)
    {
        var vertices = new Point3dCollection();
        foreach (var p in line.Points)
            vertices.Add(new Point3d(p.Point.X, p.Point.Y, p.Point.Z));
        return AddPolyline3d(tr, space, vertices, layerId, line.Closed);
    }

    /// <summary>Polyline3d por los puntos dados (p. ej. el eje generado desde puntos, §10.1).</summary>
    public static ObjectId AddPolyline3d(Transaction tr, BlockTableRecord space, IEnumerable<SurveyPoint> points, ObjectId layerId)
    {
        var vertices = new Point3dCollection();
        foreach (var p in points)
            vertices.Add(new Point3d(p.X, p.Y, p.Z));
        return AddPolyline3d(tr, space, vertices, layerId, closed: false);
    }

    private static ObjectId AddPolyline3d(Transaction tr, BlockTableRecord space, Point3dCollection vertices, ObjectId layerId, bool closed)
    {
        var polyline = new Polyline3d(Poly3dType.SimplePoly, vertices, closed)
        {
            LayerId = layerId
        };

        var id = space.AppendEntity(polyline);
        tr.AddNewlyCreatedDBObject(polyline, true);
        return id;
    }
}
