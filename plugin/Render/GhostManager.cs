using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using HSync.Core.Network;

namespace HSync.Render
{
    /// <summary>
    /// Administrador nuclear de geometrías efímeras (Hologramas). 
    /// Encripta la complejidad nativa de TransientManager para proveer una capa segura y sin fugas de memoria (AC-104).
    /// </summary>
    public static class GhostManager
    {
        private static readonly Dictionary<string, Entity> _activeGhosts = new Dictionary<string, Entity>();
        private static readonly IntegerCollection _viewportIds = new IntegerCollection(); // Vacío = Todos los viewports

        /// <summary>
        /// Inyecta instantáneamente un holograma en la RAM del viewport. (AC-101)
        /// </summary>
        /// <param name="globalId">UUID único global del sistema H-Sync</param>
        /// <param name="entity">Entidad cruda de AutoCAD generada a partir del Delta de red</param>
        public static void AddOrUpdateGhost(string globalId, Entity entity)
        {
            var tm = TransientManager.CurrentTransientManager;

            // Si ya existe el fantasma, lo destruimos físicamente del motor para evitar artefactos (memory leaks)
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                tm.EraseTransient(existing, _viewportIds);
                existing.Dispose();
                _activeGhosts.Remove(globalId);
            }

            // Inyectamos el nuevo estado físico
            tm.AddTransient(entity, TransientDrawingMode.DirectShortTerm, 128, _viewportIds);
            _activeGhosts[globalId] = entity;
        }

        /// <summary>
        /// Elimina un holograma del espectro visual y purga su memoria.
        /// </summary>
        public static void RemoveGhost(string globalId)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                TransientManager.CurrentTransientManager.EraseTransient(existing, _viewportIds);
                existing.Dispose();
                _activeGhosts.Remove(globalId);
            }
        }

        /// <summary>
        /// Destrucción segura de todos los hologramas al cerrar sesión o apagar el core.
        /// </summary>
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

        public static IEnumerable<Entity> GetAllActiveGhosts()
        {
            return _activeGhosts.Values;
        }

        /// <summary>
        /// Obtiene un fantasma específico por su ID global para lectura (ej: Bake Engine).
        /// </summary>
        public static Entity GetGhost(string globalId)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity ghost))
                return ghost;
            return null;
        }

        // AC-402: Notificación visual no bloqueante para conflictos LWW perdidos
        public static void SetGlowRed(string globalId)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                existing.ColorIndex = 1;
                TransientManager.CurrentTransientManager.UpdateTransient(existing, _viewportIds);
            }
            // AC-601: Si es una entidad nativa nuestra, la ocultamos y levantamos un fantasma
            else if (OwnershipRegistry.IsOwnedLocally(globalId))
            {
                ObjectId nativeId = OwnershipRegistry.GetNativeHandle(globalId);
                
                // 1. Ocultar la entidad real visualmente (sin tocar la BD)
                ShadowRegistry.Shadow(nativeId);

                // 2. Crear ghost FRESCO (no Clone — Clone retiene estado de BD que impide renderizado Transient)
                Entity ghost = null;
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (var docLock = doc.LockDocument())
                using (var tr = nativeId.Database.TransactionManager.StartTransaction())
                {
                    var nativeEnt = tr.GetObject(nativeId, OpenMode.ForWrite) as Entity;
                    if (nativeEnt != null)
                    {
                        nativeEnt.RecordGraphicsModified(true);
                        
                        // Construir ghost fresco según tipo geométrico
                        if (nativeEnt is Circle c)
                        {
                            ghost = new Circle() { Center = c.Center, Radius = c.Radius, ColorIndex = 1 };
                        }
                        else if (nativeEnt is Line ln)
                        {
                            ghost = new Line(ln.StartPoint, ln.EndPoint) { ColorIndex = 1 };
                        }
                        else if (nativeEnt is Autodesk.AutoCAD.DatabaseServices.Polyline poly)
                        {
                            var ghostPoly = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                            for (int i = 0; i < poly.NumberOfVertices; i++)
                            {
                                ghostPoly.AddVertexAt(i, poly.GetPoint2dAt(i), poly.GetBulgeAt(i), poly.GetStartWidthAt(i), poly.GetEndWidthAt(i));
                            }
                            ghostPoly.Closed = poly.Closed;
                            ghostPoly.ColorIndex = 1;
                            ghost = ghostPoly;
                        }
                        else
                        {
                            // Fallback: intentar Clone para tipos no mapeados
                            ghost = nativeEnt.Clone() as Entity;
                            if (ghost != null) ghost.ColorIndex = 1;
                        }
                    }
                    tr.Commit();
                }
                
                // 3. Inyectar el ghost FUERA de la transacción
                if (ghost != null)
                {
                    AddOrUpdateGhost(globalId, ghost);
                }
            }
        }

        public static void ApplyMergedState(string globalId, System.Text.Json.JsonElement winnerState)
        {
            if (_activeGhosts.TryGetValue(globalId, out Entity existing))
            {
                if (winnerState.TryGetProperty("color", out var colorProp))
                    existing.ColorIndex = colorProp.GetInt32();
                else
                    existing.ColorIndex = 256; // ByLayer (fallback)
                
                // AC-601: Parseo rápido de estado para el Test BDD (Prueba de concepto)
                if (existing is Circle circ && winnerState.TryGetProperty("geom", out var geom))
                {
                    if (geom.TryGetProperty("center", out var centerArr))
                    {
                        circ.Center = new Point3d(
                            centerArr[0].GetDouble(), 
                            centerArr[1].GetDouble(), 
                            centerArr[2].GetDouble()
                        );
                    }
                }
                else if (existing is Line ln && winnerState.TryGetProperty("geom", out var lGeom))
                {
                    if (lGeom.TryGetProperty("start", out var startArr) && lGeom.TryGetProperty("end", out var endArr))
                    {
                        ln.StartPoint = new Point3d(startArr[0].GetDouble(), startArr[1].GetDouble(), startArr[2].GetDouble());
                        ln.EndPoint = new Point3d(endArr[0].GetDouble(), endArr[1].GetDouble(), endArr[2].GetDouble());
                    }
                }
                else if (existing is Autodesk.AutoCAD.DatabaseServices.Polyline poly && winnerState.TryGetProperty("geom", out var pGeom))
                {
                    if (pGeom.TryGetProperty("nodes", out var nodesArr))
                    {
                        // LWW Estricto: Reconstruimos toda la geometría de la polilínea desde 0
                        int currentCount = poly.NumberOfVertices;
                        for (int i = 0; i < currentCount; i++) poly.RemoveVertexAt(0);

                        int idx = 0;
                        foreach (var node in nodesArr.EnumerateArray())
                        {
                            poly.AddVertexAt(idx++, new Point2d(node[0].GetDouble(), node[1].GetDouble()), 0, 0, 0);
                        }
                    }
                }

                TransientManager.CurrentTransientManager.UpdateTransient(existing, _viewportIds);
            }
        }
        /// <summary>
        /// Sprint 12: Inyecta o actualiza un holograma basado en un Delta de red.
        /// </summary>
        public static void ApplyIncomingDelta(string globalId, System.Text.Json.JsonElement delta)
        {
            if (!_activeGhosts.TryGetValue(globalId, out Entity ghost))
            {
                // Si el ghost no existe, lo creamos según su tipo
                string type = delta.GetProperty("type").GetString().ToUpper();
                if (type == "LINE") ghost = new Line();
                else if (type == "CIRCLE") ghost = new Circle();
                else if (type == "POLYLINE") ghost = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                else return; // Tipo no soportado

                AddOrUpdateGhost(globalId, ghost);
            }

            // Aplicamos las propiedades del delta (geom, color, layer)
            if (delta.TryGetProperty("props", out var props))
            {
                ApplyMergedState(globalId, props);
            }
        }
    }
}
