using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Sync
{
    public class LineSynchronizer : IEntitySynchronizer
    {
        public Type NativeType => typeof(Line);
        public string TypeTag => "LINE";

        public Dictionary<string, object> SerializeGeometry(Entity ent, Transaction tr)
        {
            var ln = (Line)ent;
            return new Dictionary<string, object>
            {
                ["start"] = new double[] { ln.StartPoint.X, ln.StartPoint.Y, ln.StartPoint.Z },
                ["end"] = new double[] { ln.EndPoint.X, ln.EndPoint.Y, ln.EndPoint.Z }
            };
        }

        public Entity CreateGhost(Entity nativeSource)
        {
            var ln = (Line)nativeSource;
            return new Line(ln.StartPoint, ln.EndPoint) { ColorIndex = 1 };
        }

        public Entity CreateGhostFromDelta(JsonElement geom)
        {
            var ln = new Line();
            ApplyDelta(ln, geom);
            ln.ColorIndex = 1;
            return ln;
        }

        public void ApplyDelta(Entity ghost, JsonElement geom)
        {
            var ln = (Line)ghost;
            try
            {
                if (geom.TryGetProperty("start", out var s) && geom.TryGetProperty("end", out var e))
                {
                    var p1 = new Point3d(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble());
                    var p2 = new Point3d(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());
                    if (p1.DistanceTo(p2) > 0.001)
                    {
                        ln.StartPoint = p1;
                        ln.EndPoint = p2;
                    }
                }
            }
            catch { }
        }

        public Entity InstantiatePure(Entity ghost)
        {
            var ln = (Line)ghost;
            return new Line(ln.StartPoint, ln.EndPoint);
        }

        public void TransferGeometry(Entity source, Entity target)
        {
            var s = (Line)source;
            var t = (Line)target;
            t.StartPoint = s.StartPoint;
            t.EndPoint = s.EndPoint;
        }
    }
}
