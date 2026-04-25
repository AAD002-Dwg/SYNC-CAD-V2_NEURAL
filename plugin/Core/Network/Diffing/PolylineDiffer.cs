using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Network.Diffing
{
    public class PolylineSnapshot : EntitySnapshot
    {
        public List<Point2d> Nodes { get; set; } = new List<Point2d>();
        public bool IsClosed { get; set; }
    }

    public class PolylineDiffer : IEntityDiffer
    {
        public EntityType Type => EntityType.POLYLINE;

        public EntitySnapshot CaptureSnapshot(Entity entity, Transaction tr)
        {
            var poly = (Polyline)entity;
            var snap = new PolylineSnapshot
            {
                Layer = poly.Layer,
                Color = poly.ColorIndex,
                Linetype = poly.Linetype,
                Visible = poly.Visible,
                IsClosed = poly.Closed
            };

            for (int i = 0; i < poly.NumberOfVertices; i++)
            {
                snap.Nodes.Add(poly.GetPoint2dAt(i));
            }
            return snap;
        }

        public Dictionary<string, object> Diff(EntitySnapshot before, EntitySnapshot after)
        {
            var b = (PolylineSnapshot)before;
            var a = (PolylineSnapshot)after;

            var deltas = new Dictionary<string, object>();

            // Scalar-Mergeables
            if (b.Layer != a.Layer)
                deltas["layer"] = a.Layer;
            if (b.Color != a.Color)
                deltas["color"] = a.Color;

            // Atomic-Group (Geometría completa de la polilínea, todo o nada)
            bool geomChanged = b.IsClosed != a.IsClosed || b.Nodes.Count != a.Nodes.Count;
            
            if (!geomChanged)
            {
                for (int i = 0; i < b.Nodes.Count; i++)
                {
                    if (!b.Nodes[i].IsEqualTo(a.Nodes[i], Tolerance.Global))
                    {
                        geomChanged = true;
                        break;
                    }
                }
            }

            if (geomChanged)
            {
                var nodes = new List<double[]>();
                foreach (var pt in a.Nodes)
                {
                    nodes.Add(new double[] { pt.X, pt.Y });
                }
                deltas["nodes"] = nodes;
                deltas["isClosed"] = a.IsClosed;
            }

            return deltas;
        }
    }
}
