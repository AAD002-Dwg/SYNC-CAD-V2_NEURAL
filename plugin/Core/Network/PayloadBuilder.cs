using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.AutoCAD.DatabaseServices;
using HSync.Core.Sync;

namespace HSync.Core.Network
{
    // Estructura oficial compatible con NEURAL_DATA_SCHEMA.md
    public class EntityDelta
    {
        public string id { get; set; }
        public string projectId { get; set; } = "PROJ-TEST-AC601";
        public string user { get; set; }
        public long client_seq { get; set; }
        public string op { get; set; } // "CREATE", "UPDATE", "DELETE"
        public string type { get; set; } // "LINE", "CIRCLE"
        public EntityProps props { get; set; }
    }

    public class EntityProps
    {
        public Dictionary<string, object> geom { get; set; }
        public string layer { get; set; }
        public int? color { get; set; }
    }

    /// <summary>
    /// Motor de Serialización de Alto Rendimiento compatible con el Hub.
    /// </summary>
    public static class PayloadBuilder
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static long _localClientSequence = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;

        public static string BuildCreate(string globalId, Entity ent, string userId, Transaction tr = null)
        {
            var sync = SyncRegistry.Get(ent.GetType());
            var delta = new EntityDelta
            {
                id = globalId,
                user = userId,
                client_seq = ++_localClientSequence,
                op = "CREATE",
                type = sync?.TypeTag ?? ent.GetType().Name.ToUpper(),
                props = new EntityProps
                {
                    layer = ent.Layer,
                    color = ent.ColorIndex,
                    geom = sync != null ? sync.SerializeGeometry(ent, tr) : ExtractGeometryLegacy(ent)
                }
            };

            return JsonSerializer.Serialize(delta, _options);
        }

        public static string BuildUpdate(string globalId, Dictionary<string, object> changedProps, string userId)
        {
            var delta = new EntityDelta
            {
                id = globalId,
                user = userId,
                client_seq = ++_localClientSequence,
                op = "UPDATE",
                props = new EntityProps
                {
                    geom = changedProps
                }
            };

            return JsonSerializer.Serialize(delta, _options);
        }

        public static string BuildDelete(string globalId, string userId)
        {
            var delta = new EntityDelta
            {
                id = globalId,
                user = userId,
                client_seq = ++_localClientSequence,
                op = "DELETE"
            };

            return JsonSerializer.Serialize(delta, _options);
        }

        /// <summary>
        /// Sprint 14: Fallback legacy para Polyline2d/3d que no tienen Synchronizer dedicado.
        /// </summary>
        private static Dictionary<string, object> ExtractGeometryLegacy(Entity ent)
        {
            var geom = new Dictionary<string, object>();
            if (ent is Polyline2d p2d)
            {
                var nodes = new List<double[]>();
                foreach (ObjectId vId in p2d)
                {
                    using (var v = vId.Open(OpenMode.ForRead) as Vertex2d)
                    {
                        if (v != null) nodes.Add(new double[] { v.Position.X, v.Position.Y });
                    }
                }
                geom["nodes"] = nodes;
                geom["isClosed"] = p2d.Closed;
            }
            else if (ent is Polyline3d p3d)
            {
                var nodes = new List<double[]>();
                foreach (ObjectId vId in p3d)
                {
                    using (var v = vId.Open(OpenMode.ForRead) as PolylineVertex3d)
                    {
                        if (v != null) nodes.Add(new double[] { v.Position.X, v.Position.Y, v.Position.Z });
                    }
                }
                geom["nodes"] = nodes;
                geom["isClosed"] = p3d.Closed;
            }
            return geom;
        }
    }
}
