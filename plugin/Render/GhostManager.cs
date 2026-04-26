using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using HSync.Core.Network;
using HSync.Core.Sync;

namespace HSync.Render
{
    /// <summary>
    /// Sprint 14: Administrador de geometrías efímeras (Hologramas), refactorizado con SyncRegistry.
    /// Toda la lógica específica por tipo de entidad ha sido extraída a los Synchronizers individuales.
    /// </summary>
    public static class GhostManager
    {
        private static readonly Dictionary<string, Entity> _activeGhosts = new Dictionary<string, Entity>();
        private static readonly IntegerCollection _viewportIds = new IntegerCollection();

        /// <summary>
        /// Inyecta instantáneamente un holograma en la RAM del viewport. (AC-101)
        /// </summary>
        public static void AddOrUpdateGhost(string globalId, Entity entity)
        {
            var tm = TransientManager.CurrentTransientManager;

            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                tm.EraseTransient(existing, _viewportIds);
                existing.Dispose();
                _activeGhosts.Remove(globalId);
            }

            // Sprint 13: Blindaje Geométrico Total (Anti eDegenerateGeometry)
            bool isValid = true;
            if (entity is Autodesk.AutoCAD.DatabaseServices.Polyline p && p.NumberOfVertices < 2) isValid = false;
            if (entity is Circle c && c.Radius < 0.001) isValid = false;
            if (entity is Line l && l.Length < 0.001) isValid = false;

            if (isValid)
            {
                try { tm.AddTransient(entity, TransientDrawingMode.DirectTopmost, 128, _viewportIds); }
                catch { }
            }

            _activeGhosts[globalId] = entity;
        }

        public static void RemoveGhost(string globalId)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                TransientManager.CurrentTransientManager.EraseTransient(existing, _viewportIds);
                existing.Dispose();
                _activeGhosts.Remove(globalId);
            }
        }

        public static void ClearAllGhosts()
        {
            var tm = TransientManager.CurrentTransientManager;
            foreach (var ghost in _activeGhosts.Values)
            {
                tm.EraseTransient(ghost, _viewportIds);
                ghost.Dispose();
            }
            _activeGhosts.Clear();
        }

        public static IEnumerable<Entity> GetAllActiveGhosts() => _activeGhosts.Values;
        public static IReadOnlyDictionary<string, Entity> GetActiveGhostMap() => _activeGhosts;

        public static Entity GetGhost(string globalId)
        {
            return _activeGhosts.TryGetValue(globalId, out Entity ghost) ? ghost : null;
        }

        // AC-402: Notificación visual para conflictos LWW
        public static void SetGlowRed(string globalId)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                existing.ColorIndex = 1;
                TransientManager.CurrentTransientManager.UpdateTransient(existing, _viewportIds);
            }
            else if (OwnershipRegistry.IsOwnedLocally(globalId))
            {
                ObjectId nativeId = OwnershipRegistry.GetNativeHandle(globalId);
                ShadowRegistry.Shadow(nativeId);

                Entity ghost = null;
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (var docLock = doc.LockDocument())
                using (var tr = nativeId.Database.TransactionManager.StartTransaction())
                {
                    var nativeEnt = tr.GetObject(nativeId, OpenMode.ForWrite) as Entity;
                    if (nativeEnt != null)
                    {
                        nativeEnt.RecordGraphicsModified(true);

                        // Sprint 14: Delegación al SyncRegistry
                        var sync = SyncRegistry.Get(nativeEnt.GetType());
                        if (sync != null)
                            ghost = sync.CreateGhost(nativeEnt);
                        else
                        {
                            ghost = nativeEnt.Clone() as Entity;
                            if (ghost != null) ghost.ColorIndex = 1;
                        }
                    }
                    tr.Commit();
                }

                if (ghost != null)
                    AddOrUpdateGhost(globalId, ghost);
            }
        }

        /// <summary>
        /// Sprint 14: Aplica estado fusionado desde la red usando SyncRegistry.
        /// </summary>
        public static void ApplyMergedState(string globalId, System.Text.Json.JsonElement winnerState)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                // Propiedades comunes (una sola vez)
                SyncRegistry.ApplyCommonPropsFromJson(existing, winnerState);

                // Geometría específica: delegar al Synchronizer correcto
                if (winnerState.TryGetProperty("geom", out var geom))
                {
                    var sync = SyncRegistry.Get(existing.GetType());
                    if (sync != null)
                    {
                        try { sync.ApplyDelta(existing, geom); } catch { }
                    }
                }

                // Sprint 13: Actualización visual fluida (Anti-Flicker)
                var tm = TransientManager.CurrentTransientManager;
                bool canRender = true;
                if (existing is Autodesk.AutoCAD.DatabaseServices.Polyline p && p.NumberOfVertices < 2) canRender = false;
                if (existing is Circle c && c.Radius < 0.001) canRender = false;
                if (existing is Line l && l.Length < 0.001) canRender = false;

                if (canRender)
                {
                    try { tm.UpdateTransient(existing, _viewportIds); }
                    catch
                    {
                        try { tm.AddTransient(existing, TransientDrawingMode.DirectTopmost, 128, _viewportIds); } catch { }
                    }
                }
                else
                {
                    try { tm.EraseTransient(existing, _viewportIds); } catch { }
                }

                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.UpdateScreen();
            }
        }

        /// <summary>
        /// Sprint 14: Inyecta o actualiza un holograma basado en un Delta de red, usando SyncRegistry.
        /// </summary>
        public static void ApplyIncomingDelta(string globalId, System.Text.Json.JsonElement delta)
        {
            if (!_activeGhosts.TryGetValue(globalId, out Entity ghost))
            {
                // 1. Instanciar el tipo correcto via SyncRegistry
                if (!delta.TryGetProperty("type", out var typeProp)) return;
                string type = typeProp.GetString().ToUpper();

                var sync = SyncRegistry.Get(type);
                if (sync == null) return;

                // 2. Crear ghost desde delta
                if (delta.TryGetProperty("props", out var props) && props.TryGetProperty("geom", out var geom))
                {
                    ghost = sync.CreateGhostFromDelta(geom);
                    SyncRegistry.ApplyCommonPropsFromJson(ghost, props);
                }
                else
                {
                    return; // Sin geometría, no podemos crear nada
                }

                // 3. Registrar y mostrar
                AddOrUpdateGhost(globalId, ghost);
            }
            else
            {
                // Update de objeto existente
                if (delta.TryGetProperty("props", out var props))
                {
                    ApplyMergedState(globalId, props);
                    var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
                    ed?.WriteMessage($"\n[H-SYNC] >> Holograma actualizado: {globalId}");
                }
            }
        }
    }
}
