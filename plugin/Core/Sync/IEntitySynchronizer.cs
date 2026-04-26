using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;

namespace HSync.Core.Sync
{
    /// <summary>
    /// Sprint 14: Contrato universal para la sincronización de cualquier tipo de entidad AutoCAD.
    /// Cada implementación encapsula las 5 operaciones críticas (Serializar, Crear Ghost, Aplicar Delta, Instanciar Puro, Transferir Geometría)
    /// para un tipo geométrico específico (Line, Circle, Arc, etc.).
    /// </summary>
    public interface IEntitySynchronizer
    {
        // ── Identidad ──
        Type NativeType { get; }
        string TypeTag { get; }

        // ── Serialización (Salida → Red) ──
        Dictionary<string, object> SerializeGeometry(Entity ent, Transaction tr);

        // ── Ghost: Creación (Entrada ← Red) ──
        Entity CreateGhost(Entity nativeSource);
        Entity CreateGhostFromDelta(JsonElement geomProps);

        // ── Ghost: Actualización Visual en Vivo ──
        void ApplyDelta(Entity ghost, JsonElement geomProps);

        // ── Horneado / Bake (Ghost → DB) ──
        Entity InstantiatePure(Entity ghost);
        void TransferGeometry(Entity source, Entity target);
    }

    /// <summary>
    /// Sprint 14: Despachador central O(1) sin Reflection.
    /// Reemplaza todos los if-else por tipo de entidad en GhostManager, PayloadBuilder y HSyncPlugin.
    /// </summary>
    public static class SyncRegistry
    {
        private static readonly Dictionary<Type, IEntitySynchronizer> _byType = new Dictionary<Type, IEntitySynchronizer>();
        private static readonly Dictionary<string, IEntitySynchronizer> _byTag = new Dictionary<string, IEntitySynchronizer>(StringComparer.OrdinalIgnoreCase);

        static SyncRegistry()
        {
            Register(new LineSynchronizer());
            Register(new CircleSynchronizer());
            Register(new PolylineSynchronizer());
            Register(new ArcSynchronizer());
            Register(new TextSynchronizer());
            Register(new MTextSynchronizer());
        }

        public static void Register(IEntitySynchronizer sync)
        {
            _byType[sync.NativeType] = sync;
            _byTag[sync.TypeTag] = sync;
        }

        public static IEntitySynchronizer Get(Type t)
        {
            return _byType.TryGetValue(t, out var s) ? s : null;
        }

        public static IEntitySynchronizer Get(string tag)
        {
            return _byTag.TryGetValue(tag, out var s) ? s : null;
        }

        public static IEnumerable<IEntitySynchronizer> All => _byType.Values;

        public static bool IsSupported(Type t) => _byType.ContainsKey(t);

        /// <summary>
        /// Aplica propiedades comunes heredadas de Entity (Color, Layer, Linetype).
        /// Se invoca una sola vez en el motor, no en cada Synchronizer individual.
        /// </summary>
        public static void ApplyCommonProps(Entity target, Entity source)
        {
            target.ColorIndex = source.ColorIndex;
            try { target.Layer = source.Layer; } catch { }
            try { target.Linetype = source.Linetype; } catch { }
        }

        public static void ApplyCommonPropsFromJson(Entity target, JsonElement props)
        {
            if (props.TryGetProperty("color", out var colorProp))
                target.ColorIndex = colorProp.GetInt32();
            else
                target.ColorIndex = 256; // ByLayer
        }
    }
}
