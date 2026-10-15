using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.Acad;

/// <summary>Convierte CogoPoint en SurveyPoint (solo lectura; nunca modifica el punto).</summary>
public static class CogoPointReader
{
    public static List<SurveyPoint> Read(Transaction tr, IEnumerable<ObjectId> ids)
    {
        var points = new List<SurveyPoint>();
        foreach (var id in ids)
        {
            if (id.IsNull || id.IsErased)
                continue;
            if (tr.GetObject(id, OpenMode.ForRead, false) is not CogoPoint cogo)
                continue;

            points.Add(new SurveyPoint
            {
                Number = cogo.PointNumber,
                Name = cogo.PointName ?? "",
                X = cogo.Easting,
                Y = cogo.Northing,
                Z = cogo.Elevation,
                // Siempre la descripción cruda, no la expandida por description keys (§6).
                RawDescription = cogo.RawDescription ?? "",
                SourceId = id.Handle.ToString()
            });
        }
        return points;
    }
}
