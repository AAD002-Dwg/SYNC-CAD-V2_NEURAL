using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Sync
{
    public class PolylineSynchronizer : IEntitySynchronizer
    {
        public Type NativeType => typeof(Autodesk.AutoCAD.DatabaseServices.Polyline);
        public string TypeTag => "POLYLINE";

        public Dictionary<string, object> SerializeGeometry(Entity ent, Transaction tr)
        {
            var poly = (Autodesk.AutoCAD.DatabaseServices.Polyline)ent;
            var nodes = new List<double[]>();
            for (int i = 0; i < poly.NumberOfVertices; i++)
            {
                var pt = poly.GetPoint2dAt(i);
                nodes.Add(new double[] { pt.X, pt.Y });
            }
            return new Dictionary<string, object>
            {
                ["nodes"] = nodes,
                ["isClosed"] = poly.Closed
            };
        }

        public Entity CreateGhost(Entity nativeSource)
        {
            var poly = (Autodesk.AutoCAD.DatabaseServices.Polyline)nativeSource;
            var ghost = new Autodesk.AutoCAD.DatabaseServices.Polyline();
            for (int i = 0; i < poly.NumberOfVertices; i++)
                ghost.AddVertexAt(i, poly.GetPoint2dAt(i), poly.GetBulgeAt(i), poly.GetStartWidthAt(i), poly.GetEndWidthAt(i));
            ghost.Closed = poly.Closed;
            ghost.ColorIndex = 1;
            return ghost;
        }

        public Entity CreateGhostFromDelta(JsonElement geom)
        {
            var poly = new Autodesk.AutoCAD.DatabaseServices.Polyline();
            ApplyDelta(poly, geom);
            poly.ColorIndex = 1;
            return poly;
        }

        public void ApplyDelta(Entity ghost, JsonElement geom)
        {
            var poly = (Autodesk.AutoCAD.DatabaseServices.Polyline)ghost;
            try
            {
                if (geom.TryGetProperty("nodes", out var nodesArr))
                {
                    var nodesList = nodesArr.EnumerateArray().ToList();
                    if (nodesList.Count >= 2)
                    {
                        int currentCount = poly.NumberOfVertices;
                        int newCount = nodesList.Count;

                        // 1. Recortar sobrantes
                        if (newCount < currentCount)
                            for (int i = currentCount - 1; i >= newCount; i--) poly.RemoveVertexAt(i);

                        // 2. Actualizar existentes o añadir nuevos
                        for (int i = 0; i < newCount; i++)
                        {
                            var node = nodesList[i];
                            var pt = new Point2d(node[0].GetDouble(), node[1].GetDouble());
                            if (i < poly.NumberOfVertices)
                                poly.SetPointAt(i, pt);
                            else
                                poly.AddVertexAt(i, pt, 0, 0, 0);
                        }

                        if (geom.TryGetProperty("isClosed", out var closedProp))
                            poly.Closed = closedProp.GetBoolean();
                    }
                }
            }
            catch { }
        }

        public Entity InstantiatePure(Entity ghost)
        {
            var ghPoly = (Autodesk.AutoCAD.DatabaseServices.Polyline)ghost;
            var p = new Autodesk.AutoCAD.DatabaseServices.Polyline();
            for (int i = 0; i < ghPoly.NumberOfVertices; i++)
                p.AddVertexAt(i, ghPoly.GetPoint2dAt(i), ghPoly.GetBulgeAt(i), 0, 0);
            p.Closed = ghPoly.Closed;
            return p;
        }

        public void TransferGeometry(Entity source, Entity target)
        {
            var s = (Autodesk.AutoCAD.DatabaseServices.Polyline)source;
            var t = (Autodesk.AutoCAD.DatabaseServices.Polyline)target;

            int currentCount = t.NumberOfVertices;
            int newCount = s.NumberOfVertices;

            if (newCount < currentCount)
                for (int i = currentCount - 1; i >= newCount; i--) t.RemoveVertexAt(i);

            for (int i = 0; i < newCount; i++)
            {
                if (i < t.NumberOfVertices)
                    t.SetPointAt(i, s.GetPoint2dAt(i));
                else
                    t.AddVertexAt(i, s.GetPoint2dAt(i), s.GetBulgeAt(i), 0, 0);
            }
            t.Closed = s.Closed;
        }
    }
}
