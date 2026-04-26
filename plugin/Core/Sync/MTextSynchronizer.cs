using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Sync
{
    public class MTextSynchronizer : IEntitySynchronizer
    {
        public Type NativeType => typeof(MText);
        public string TypeTag => "MTEXT";

        public Dictionary<string, object> SerializeGeometry(Entity ent, Transaction tr)
        {
            var mt = (MText)ent;
            return new Dictionary<string, object>
            {
                ["location"] = new double[] { mt.Location.X, mt.Location.Y, mt.Location.Z },
                ["contents"] = mt.Contents ?? "",
                ["textHeight"] = mt.TextHeight,
                ["width"] = mt.Width,
                ["rotation"] = mt.Rotation
            };
        }

        public Entity CreateGhost(Entity nativeSource)
        {
            var mt = (MText)nativeSource;
            var ghost = new MText();
            ghost.Location = mt.Location;
            ghost.Contents = mt.Contents;
            ghost.TextHeight = mt.TextHeight;
            ghost.Width = mt.Width;
            ghost.Rotation = mt.Rotation;
            ghost.ColorIndex = 1;
            return ghost;
        }

        public Entity CreateGhostFromDelta(JsonElement geom)
        {
            var mt = new MText { TextHeight = 2.5, Contents = "" };
            ApplyDelta(mt, geom);
            mt.ColorIndex = 1;
            return mt;
        }

        public void ApplyDelta(Entity ghost, JsonElement geom)
        {
            var mt = (MText)ghost;
            try
            {
                if (geom.TryGetProperty("location", out var locArr))
                    mt.Location = new Point3d(locArr[0].GetDouble(), locArr[1].GetDouble(), locArr[2].GetDouble());
                if (geom.TryGetProperty("contents", out var cProp))
                    mt.Contents = cProp.GetString() ?? "";
                if (geom.TryGetProperty("textHeight", out var thProp))
                {
                    double h = thProp.GetDouble();
                    if (h > 0.001) mt.TextHeight = h;
                }
                if (geom.TryGetProperty("width", out var wProp))
                    mt.Width = wProp.GetDouble();
                if (geom.TryGetProperty("rotation", out var rProp))
                    mt.Rotation = rProp.GetDouble();
            }
            catch { }
        }

        public Entity InstantiatePure(Entity ghost)
        {
            var mt = (MText)ghost;
            var n = new MText();
            n.Location = mt.Location;
            n.Contents = mt.Contents;
            n.TextHeight = mt.TextHeight;
            n.Width = mt.Width;
            n.Rotation = mt.Rotation;
            return n;
        }

        public void TransferGeometry(Entity source, Entity target)
        {
            var s = (MText)source;
            var t = (MText)target;
            t.Location = s.Location;
            t.Contents = s.Contents;
            t.TextHeight = s.TextHeight;
            t.Width = s.Width;
            t.Rotation = s.Rotation;
        }
    }
}
