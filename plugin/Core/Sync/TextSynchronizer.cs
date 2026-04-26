using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Sync
{
    public class TextSynchronizer : IEntitySynchronizer
    {
        public Type NativeType => typeof(DBText);
        public string TypeTag => "TEXT";

        public Dictionary<string, object> SerializeGeometry(Entity ent, Transaction tr)
        {
            var txt = (DBText)ent;
            return new Dictionary<string, object>
            {
                ["position"] = new double[] { txt.Position.X, txt.Position.Y, txt.Position.Z },
                ["textString"] = txt.TextString,
                ["height"] = txt.Height,
                ["rotation"] = txt.Rotation
            };
        }

        public Entity CreateGhost(Entity nativeSource)
        {
            var txt = (DBText)nativeSource;
            return new DBText
            {
                Position = txt.Position,
                TextString = txt.TextString,
                Height = txt.Height,
                Rotation = txt.Rotation,
                ColorIndex = 1
            };
        }

        public Entity CreateGhostFromDelta(JsonElement geom)
        {
            var txt = new DBText { Height = 2.5, TextString = "" };
            ApplyDelta(txt, geom);
            txt.ColorIndex = 1;
            return txt;
        }

        public void ApplyDelta(Entity ghost, JsonElement geom)
        {
            var txt = (DBText)ghost;
            try
            {
                if (geom.TryGetProperty("position", out var pArr))
                    txt.Position = new Point3d(pArr[0].GetDouble(), pArr[1].GetDouble(), pArr[2].GetDouble());
                if (geom.TryGetProperty("textString", out var tsProp))
                    txt.TextString = tsProp.GetString() ?? "";
                if (geom.TryGetProperty("height", out var hProp))
                {
                    double h = hProp.GetDouble();
                    if (h > 0.001) txt.Height = h;
                }
                if (geom.TryGetProperty("rotation", out var rProp))
                    txt.Rotation = rProp.GetDouble();
            }
            catch { }
        }

        public Entity InstantiatePure(Entity ghost)
        {
            var txt = (DBText)ghost;
            return new DBText
            {
                Position = txt.Position,
                TextString = txt.TextString,
                Height = txt.Height,
                Rotation = txt.Rotation
            };
        }

        public void TransferGeometry(Entity source, Entity target)
        {
            var s = (DBText)source;
            var t = (DBText)target;
            t.Position = s.Position;
            t.TextString = s.TextString;
            t.Height = s.Height;
            t.Rotation = s.Rotation;
        }
    }
}
