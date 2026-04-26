using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Sync
{
    public class CircleSynchronizer : IEntitySynchronizer
    {
        public Type NativeType => typeof(Circle);
        public string TypeTag => "CIRCLE";

        public Dictionary<string, object> SerializeGeometry(Entity ent, Transaction tr)
        {
            var c = (Circle)ent;
            return new Dictionary<string, object>
            {
                ["center"] = new double[] { c.Center.X, c.Center.Y, c.Center.Z },
                ["radius"] = c.Radius
            };
        }

        public Entity CreateGhost(Entity nativeSource)
        {
            var c = (Circle)nativeSource;
            return new Circle() { Center = c.Center, Radius = c.Radius, ColorIndex = 1 };
        }

        public Entity CreateGhostFromDelta(JsonElement geom)
        {
            var c = new Circle { Radius = 1.0 }; // Default seguro
            ApplyDelta(c, geom);
            c.ColorIndex = 1;
            return c;
        }

        public void ApplyDelta(Entity ghost, JsonElement geom)
        {
            var c = (Circle)ghost;
            try
            {
                if (geom.TryGetProperty("center", out var cArr))
                    c.Center = new Point3d(cArr[0].GetDouble(), cArr[1].GetDouble(), cArr[2].GetDouble());
                if (geom.TryGetProperty("radius", out var rProp))
                {
                    double r = rProp.GetDouble();
                    if (r > 0.001) c.Radius = r;
                }
            }
            catch { }
        }

        public Entity InstantiatePure(Entity ghost)
        {
            var c = (Circle)ghost;
            return new Circle() { Center = c.Center, Radius = c.Radius };
        }

        public void TransferGeometry(Entity source, Entity target)
        {
            var s = (Circle)source;
            var t = (Circle)target;
            t.Center = s.Center;
            t.Radius = s.Radius;
        }
    }
}
