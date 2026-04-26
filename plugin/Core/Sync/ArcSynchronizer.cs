using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Sync
{
    public class ArcSynchronizer : IEntitySynchronizer
    {
        public Type NativeType => typeof(Arc);
        public string TypeTag => "ARC";

        public Dictionary<string, object> SerializeGeometry(Entity ent, Transaction tr)
        {
            var arc = (Arc)ent;
            return new Dictionary<string, object>
            {
                ["center"] = new double[] { arc.Center.X, arc.Center.Y, arc.Center.Z },
                ["radius"] = arc.Radius,
                ["startAngle"] = arc.StartAngle,
                ["endAngle"] = arc.EndAngle
            };
        }

        public Entity CreateGhost(Entity nativeSource)
        {
            var arc = (Arc)nativeSource;
            return new Arc(arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle) { ColorIndex = 1 };
        }

        public Entity CreateGhostFromDelta(JsonElement geom)
        {
            var arc = new Arc();
            ApplyDelta(arc, geom);
            arc.ColorIndex = 1;
            return arc;
        }

        public void ApplyDelta(Entity ghost, JsonElement geom)
        {
            var arc = (Arc)ghost;
            try
            {
                if (geom.TryGetProperty("center", out var cArr))
                    arc.Center = new Point3d(cArr[0].GetDouble(), cArr[1].GetDouble(), cArr[2].GetDouble());
                if (geom.TryGetProperty("radius", out var rProp))
                {
                    double r = rProp.GetDouble();
                    if (r > 0.001) arc.Radius = r;
                }
                if (geom.TryGetProperty("startAngle", out var saProp))
                    arc.StartAngle = saProp.GetDouble();
                if (geom.TryGetProperty("endAngle", out var eaProp))
                    arc.EndAngle = eaProp.GetDouble();
            }
            catch { }
        }

        public Entity InstantiatePure(Entity ghost)
        {
            var arc = (Arc)ghost;
            return new Arc(arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle);
        }

        public void TransferGeometry(Entity source, Entity target)
        {
            var s = (Arc)source;
            var t = (Arc)target;
            t.Center = s.Center;
            t.Radius = s.Radius;
            t.StartAngle = s.StartAngle;
            t.EndAngle = s.EndAngle;
        }
    }
}
