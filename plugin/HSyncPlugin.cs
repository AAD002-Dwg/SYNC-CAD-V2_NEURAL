using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using HSync.Core;
using HSync.Core.Network;
using HSync.Core.Sync;
using HSync.Render;

[assembly: ExtensionApplication(typeof(HSync.HSyncPlugin))]

namespace HSync
{
    /// <summary>
    /// Punto de Entrada principal del ecosistema SYNC-CAD NEURAL.
    /// Registra los módulos de seguridad visual y OSNAP (Fase 1: Motor Local).
    /// </summary>
    public class HSyncPlugin : IExtensionApplication
    {
        public static SyncSocketClient SocketClient { get; private set; }
        public static string ServerUrl { get; set; } = "ws://localhost:3000";

        public void Initialize()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            
            // Inicializar Subsistemas del Sprint 1
            if (doc != null)
            {
                UndoInterceptor.Initialize(doc);
            }

            // Inicializar Overrule matemático de geometrías efímeras
            HologramOsnapOverrule.Initialize();
            
            // AC-601: Overrule de Ocultamiento de Nativos (Canónico vs Proyectado)
            var entityClass = RXObject.GetClass(typeof(Entity));
            Overrule.AddOverrule(entityClass, ShadowDrawOverrule.Instance, false);
            Overrule.AddOverrule(entityClass, ShadowOsnapOverrule.Instance, false);
            Overrule.AddOverrule(entityClass, ShadowGripOverrule.Instance, false);

            // Optimizacion masiva: empezamos con filtros vacíos
            ShadowDrawOverrule.Instance.SetIdFilter(new ObjectId[0]);
            ShadowOsnapOverrule.Instance.SetIdFilter(new ObjectId[0]);
            ShadowGripOverrule.Instance.SetIdFilter(new ObjectId[0]);
            Overrule.Overruling = true;

            // AC-401 (Diffing pre-comando)
            EventMonitor.Initialize(); 

            var editor = Application.DocumentManager.MdiActiveDocument?.Editor;
            editor?.WriteMessage("\n[H-SYNC] Motor Holográfico Neural Inicializado (v2.0) - ¡Listo para AC-10x y Red!");

            // AC-304: Vigilar la muerte súbita del documento para avisarle al Servidor
            Application.DocumentManager.DocumentDestroyed += OnDocumentDestroyed;
        }

        private void OnDocumentDestroyed(object sender, DocumentDestroyedEventArgs e)
        {
            // TODO: Enviar DISCONNECT_REQ de rescate al WebSocket antes de que el AppDomain se cierre.
            // WebSocketClient.SendDeltaAsync("{ 'type': 'DISCONNECT_REQ' }").Wait(500);
            GhostManager.ClearAllGhosts();
        }

        public void Terminate()
        {
            if (Application.DocumentManager.MdiActiveDocument != null)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.PointMonitor -= OnPointMonitor;
            }

            Application.DocumentManager.DocumentDestroyed -= OnDocumentDestroyed;
            
            // Limpieza segura requerida por el motor nativo de AutoCAD
            UndoInterceptor.Terminate();
            HologramOsnapOverrule.Terminate();
            EventMonitor.Terminate();
            GhostManager.ClearAllGhosts();
            CursorManager.ClearAllCursors();
        }

        [CommandMethod("HSYNC_SERVER")]
        public void SetServerUrl()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var prompt = new PromptStringOptions($"\nURL del Hub actual: {ServerUrl}\nNueva URL (ej: wss://hsync-hub.onrender.com): ");
            prompt.AllowSpaces = false;
            prompt.DefaultValue = ServerUrl;
            
            var res = ed.GetString(prompt);
            if (res.Status == PromptStatus.OK)
            {
                string newUrl = res.StringResult.Trim();
                if (newUrl.StartsWith("https://")) newUrl = newUrl.Replace("https://", "wss://");
                else if (newUrl.StartsWith("http://")) newUrl = newUrl.Replace("http://", "ws://");
                
                ServerUrl = newUrl;
                ed.WriteMessage($"\n[H-SYNC] Servidor configurado: {ServerUrl}");
            }
        }

        [CommandMethod("HSYNC_CURSORS")]
        public void ToggleCursors()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var opt = new PromptKeywordOptions("\nVisualización de cursores remotos:");
            opt.Keywords.Add("ON");
            opt.Keywords.Add("OFF");
            opt.Keywords.Default = HSync.Render.CursorManager.ShowCursors ? "ON" : "OFF";

            var res = ed.GetKeywords(opt);
            if (res.Status == PromptStatus.OK)
            {
                bool show = res.StringResult == "ON";
                HSync.Render.CursorManager.ShowCursors = show;
                if (!show) HSync.Render.CursorManager.ClearAllCursors();
                ed.WriteMessage($"\n[H-SYNC] Cursores remotos: {(show ? "ACTIVADOS" : "DESACTIVADOS")}");
            }
        }

        [CommandMethod("HSYNC_FOLLOW")]
        public void FollowUser()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var opt = new PromptStringOptions("\nUsuario a seguir (vacío para dejar de seguir)");
            opt.AllowSpaces = true;
            if (!string.IsNullOrEmpty(HSync.Render.CursorManager.FollowUserId))
                opt.DefaultValue = HSync.Render.CursorManager.FollowUserId;

            var res = ed.GetString(opt);
            if (res.Status == PromptStatus.OK)
            {
                string user = res.StringResult;
                HSync.Render.CursorManager.FollowUserId = string.IsNullOrEmpty(user) ? null : user;
                ed.WriteMessage(string.IsNullOrEmpty(user) ? "\n[H-SYNC] Modo seguimiento desactivado." : $"\n[H-SYNC] Siguiendo a: {user}");
            }
        }

        [CommandMethod("HSYNC_TEST_GHOST")]
        public void TestGhostInjection()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Creamos una entidad efímera mock para validar AC-101 y AC-102
            using (var line = new Autodesk.AutoCAD.DatabaseServices.Line(
                new Autodesk.AutoCAD.Geometry.Point3d(0, 0, 0),
                new Autodesk.AutoCAD.Geometry.Point3d(100, 100, 0)))
            {
                line.ColorIndex = 3; // Verde

                // Inyección
                GhostManager.AddOrUpdateGhost("mock_uuid_123", (Autodesk.AutoCAD.DatabaseServices.Entity)line.Clone());
            }

            doc.Editor.WriteMessage("\n[H-SYNC] Holograma de prueba inyectado. Intenta hacer Ctrl+Z para validar la persistencia (AC-102).");
        }
        
        [CommandMethod("HSYNC_CLEAR")]
        public void TestGhostClear()
        {
            GhostManager.ClearAllGhosts();
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[H-SYNC] Hologramas purgados.");
        }

        private DateTime _lastCursorSend = DateTime.MinValue;

        [CommandMethod("HSYNC_CONNECT")]
        public async void ConnectToHub()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var uiContext = System.Threading.SynchronizationContext.Current;
            doc.Editor.WriteMessage($"\n[H-SYNC] Iniciando enlace con {ServerUrl}...");
            
            try 
            {
                if (SocketClient == null || !SocketClient.IsConnected) 
                {
                    SocketClient = new SyncSocketClient(ServerUrl, "ALAN-ACAD");
                }

                await SocketClient.ConnectAsync();
                await HandshakeManager.InitiateConnectAsync(SocketClient, 0);
                
                uiContext.Post(_ => {
                    try {
                        doc.Editor.WriteMessage("\n[H-SYNC] >> CONEXION EXITOSA. Modo Multi-Usuario Activado.");
                        
                        // Sprint 12: Activar Sincronización de Cursores
                        doc.Editor.PointMonitor += OnPointMonitor;

                        int discovered = RunAutoDiscovery(doc);
                        if (discovered > 0)
                        {
                            doc.Editor.WriteMessage($"\n[H-SYNC] Auto-Discovery: {discovered} entidades sincronizadas.");
                        }
                    } catch (System.Exception ex) {
                        doc.Editor.WriteMessage($"\n[H-SYNC] Error post-conexion: {ex.Message}");
                    }
                }, null);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[H-SYNC] Error de enlace: {ex.Message}");
            }
        }

        private void OnPointMonitor(object sender, PointMonitorEventArgs e)
        {
            if (SocketClient == null || !SocketClient.IsConnected) return;

            // Throttling: Solo enviamos cursor cada 60ms (aprox 16fps) para no saturar
            if ((DateTime.UtcNow - _lastCursorSend).TotalMilliseconds < 60) return;

            var pt = e.Context.RawPoint;
            string json = $"{{\"type\":\"CURSOR\",\"user\":\"{SocketClient.UserId}\",\"pos\":[{pt.X},{pt.Y},{pt.Z}]}}";
            
            _ = SocketClient.SendDeltaAsync(json);
            _lastCursorSend = DateTime.UtcNow;
        }

        /// <summary>
        /// Sprint 12: Escanea el BlockTableRecord del espacio modelo y registra
        /// todas las entidades compatibles (Line, Circle, Polyline) en el OwnershipRegistry,
        /// enviando un delta CREATE al Hub por cada una.
        /// </summary>
        private int RunAutoDiscovery(Document doc)
        {
            int count = 0;
            try
            {
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var bt = tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var modelSpace = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        // Solo entidades compatibles (Sprint 14: delegación a SyncRegistry)
                        bool isValid = SyncRegistry.IsSupported(ent.GetType()) ||
                                      ent is Polyline2d || ent is Polyline3d;
                        
                        if (ent is BlockBegin || ent is BlockEnd) isValid = false;
                        if (!isValid) continue;

                        string uuid = id.Handle.ToString().ToLowerInvariant();

                        // No enviamos si ya lo conocemos
                        if (OwnershipRegistry.IsOwnedLocally(uuid))
                            continue;

                        // 1. Registrar en el OwnershipRegistry
                        OwnershipRegistry.RegisterLocalEntity(uuid, id);

                        // 2. Enviar delta CREATE al Hub (fire-and-forget)
                        if (SocketClient != null && SocketClient.IsConnected)
                        {
                            string json = PayloadBuilder.BuildCreate(uuid, ent, SocketClient.UserId);
                            _ = SocketClient.SendDeltaAsync(json);
                            count++;
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[H-SYNC] Error en Auto-Discovery: {ex.Message}");
            }
            return count;
        }

        /// <summary>
        /// Sprint 12: Comando de Test por Selección.
        /// Permite seleccionar entidades con el mouse y enviar sus IDs al servidor
        /// para que dispare mutaciones remotas de prueba automáticamente.
        /// </summary>
        [CommandMethod("HSYNC_TEST_ME")]
        public async void TestMeCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            if (SocketClient == null || !SocketClient.IsConnected)
            {
                ed.WriteMessage("\n[H-SYNC] Error: No estás conectado al Hub.");
                return;
            }

            var selResult = ed.GetSelection();
            if (selResult.Status != PromptStatus.OK) return;

            var ids = new System.Collections.Generic.List<string>();
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var selection = selResult.Value;
                ed.WriteMessage($"\n[H-SYNC DEBUG] Analizando {selection.Count} objetos seleccionados...");
                
                foreach (ObjectId objId in selection.GetObjectIds())
                {
                    if (objId.IsErased) continue;
                    
                    using (var ent = tr.GetObject(objId, OpenMode.ForRead) as Entity)
                    {
                        if (ent != null)
                        {
                            string className = ent.GetType().Name;
                            string uuid = objId.Handle.ToString().ToLowerInvariant();
                            ed.WriteMessage($"\n - Detectado: {className} [Handle: {uuid}]");
                            ids.Add(uuid);
                        }
                    }
                }
                tr.Commit();
            }

            if (ids.Count > 0)
            {
                ed.WriteMessage($"\n[H-SYNC] Selección local: {ids.Count} objetos detectados.");
                var request = new
                {
                    type = "TEST_MUTATE_REQ",
                    user = SocketClient.UserId,
                    entities = ids
                };
                string json = System.Text.Json.JsonSerializer.Serialize(request);
                await SocketClient.SendDeltaAsync(json);
                ed.WriteMessage($"\n[H-SYNC] >> TEST_MUTATE_REQ enviado. Esperando respuesta del Hub...");
            }
            else
            {
                ed.WriteMessage("\n[H-SYNC] Error: No se encontraron entidades compatibles en la selección.");
            }
        }

        [CommandMethod("HSYNC_TEST_CURSORS")]
        public async void TestRemoteCursors()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            if (SocketClient == null || !SocketClient.IsConnected)
            {
                ed.WriteMessage("\n[H-SYNC] Error: Debes estar conectado (HSYNC_CONNECT) para probar cursores.");
                return;
            }

            ed.WriteMessage("\n[H-SYNC] >> Invocando colaborador virtual (MARIA-BOT)...");
            
            var request = new
            {
                type = "TEST_CURSOR_START",
                center = new double[] { 0, 0, 0 } // Orbitar cerca del origen
            };

            string json = System.Text.Json.JsonSerializer.Serialize(request);
            await SocketClient.SendDeltaAsync(json);
        }

        [CommandMethod("HSYNC_TEST_DRAW")]
        public async void TestDrawSimulation()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            if (SocketClient == null || !SocketClient.IsConnected)
            {
                ed.WriteMessage("\n[H-SYNC] Error: Debes estar conectado (HSYNC_CONNECT) para probar el bot de dibujo.");
                return;
            }

            var ppr = ed.GetPoint("\nCentro de la simulación de dibujo: ");
            if (ppr.Status != PromptStatus.OK) return;

            ed.WriteMessage($"\n[H-SYNC] >> Enviando petición de dibujo al Hub (Tipo: TEST_DRAW_START)...");
            
            var request = new
            {
                type = "TEST_DRAW_START",
                user = "ALAN-ACAD",
                center = new double[] { ppr.Value.X, ppr.Value.Y, ppr.Value.Z }
            };

            string json = System.Text.Json.JsonSerializer.Serialize(request);
            await SocketClient.SendDeltaAsync(json);
            ed.WriteMessage("\n[H-SYNC] >> Petición enviada. Esperando a MARIA-BOT...");
        }

        [CommandMethod("HSYNC_HEAVY_TEST")]
        public void TestHeavyGhostInjection()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            
            doc.Editor.WriteMessage("\n[H-SYNC] Generando 5,000 geometrías pesadas (Círculos y MText)... esto probará el verdadero límite.");
            GhostManager.ClearAllGhosts();
            
            Random rnd = new Random();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 5000; i++)
            {
                double x = (rnd.NextDouble() * 20000) - 10000;
                double y = (rnd.NextDouble() * 20000) - 10000;
                
                // Entidad 1: Círculo (Matemáticamente más complejo que una línea)
                var circle = new Autodesk.AutoCAD.DatabaseServices.Circle();
                circle.Center = new Autodesk.AutoCAD.Geometry.Point3d(x, y, 0);
                circle.Radius = rnd.NextDouble() * 100 + 10;
                circle.ColorIndex = (short)rnd.Next(1, 255);
                GhostManager.AddOrUpdateGhost($"circle_{i}", circle);
                
                // Entidad 2: Texto Multilínea MText (Pone a prueba el motor de rasterización de fuentes TTS de AutoCAD)
                var mtext = new Autodesk.AutoCAD.DatabaseServices.MText();
                mtext.Location = new Autodesk.AutoCAD.Geometry.Point3d(x, y, 0);
                mtext.Contents = "H-SYNC\\PNEURAL\\PCARGAPESADA";
                mtext.TextHeight = 50;
                mtext.ColorIndex = 2; // Amarillo
                GhostManager.AddOrUpdateGhost($"text_{i}", mtext);
            }
            
            sw.Stop();
            doc.Editor.WriteMessage($"\n[H-SYNC] 10,000 entidades pesadas combinadas inyectadas en {sw.ElapsedMilliseconds}ms. ¡Haz Pan/Zoom ahora!");
        }

        [CommandMethod("HSYNC_TEST_COMPLEX")]
        public void TestComplexEntities()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\n[H-SYNC] Generando entidades complejas locales (Arc, Text, MText) para validación de Red...");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // 1. Arc
                var arc = new Arc(new Point3d(100, 0, 0), 50, 0.5, 2.5);
                arc.ColorIndex = 4;
                btr.AppendEntity(arc);
                tr.AddNewlyCreatedDBObject(arc, true);

                // 2. Text
                var text = new DBText();
                text.Position = new Point3d(200, 0, 0);
                text.TextString = "H-SYNC NATIVE TEXT";
                text.Height = 10;
                text.ColorIndex = 5;
                btr.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);

                // 3. MText
                var mtext = new MText();
                mtext.Location = new Point3d(350, 0, 0);
                mtext.Contents = "H-SYNC\\PNATIVE MTEXT";
                mtext.TextHeight = 10;
                mtext.ColorIndex = 6;
                btr.AppendEntity(mtext);
                tr.AddNewlyCreatedDBObject(mtext, true);

                tr.Commit();
            }
            ed.WriteMessage("\n[H-SYNC] Entidades creadas. Revisa la consola del Hub para ver los deltas CREATE.");
        }

        [CommandMethod("HSYNC_BAKE")]
        public void BakeHolograms()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            
            var shadedIds = HSync.Render.ShadowRegistry.GetAllShaded();
            var activeGhostsCount = GhostManager.GetActiveGhostMap().Count;
            
            if (shadedIds.Length == 0 && activeGhostsCount == 0)
            {
                doc.Editor.WriteMessage("\n[H-SYNC] No hay hologramas pendientes de Bake.");
                return;
            }

            int bakedCount = 0;
            
            // Mitigación Anti-Eco
            EventMonitor.IsBaking = true;
            
            try
            {
                using (var docLock = doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var bt = tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    // 1. Procesar sombras (Objetos locales actualizados remotamente)
                    foreach (var id in shadedIds)
                    {
                        if (id.IsErased) continue;
                        string uuid = id.Handle.ToString().ToLowerInvariant();
                        var ghost = GhostManager.GetGhost(uuid);
                        if (ghost == null) continue;

                        var nativeEnt = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (nativeEnt != null)
                        {
                            UpdateNativeFromGhost(nativeEnt, ghost);
                            nativeEnt.RecordGraphicsModified(true);
                            ShadowRegistry.Unshadow(id);
                            GhostManager.RemoveGhost(uuid);
                            bakedCount++;
                        }
                    }

                    // 2. Procesar fantasmas remotos (Objetos de compañeros / bots)
                    var allGhosts = GhostManager.GetAllActiveGhosts().ToList();
                    foreach (var ghost in allGhosts)
                    {
                        // Buscamos el ID global del ghost
                        string gId = null;
                        foreach (var kvp in GhostManager.GetActiveGhostMap()) if (kvp.Value == ghost) gId = kvp.Key;
                        if (gId == null) continue;

                        // Si es un objeto nativo (sombreado), ya lo procesamos arriba
                        if (OwnershipRegistry.IsOwnedLocally(gId)) continue;

                        // Sprint 14: Instanciación pura via SyncRegistry (Anti-Corrupción)
                        var sync = SyncRegistry.Get(ghost.GetType());
                        Entity newEnt = null;

                        if (sync != null)
                        {
                            newEnt = sync.InstantiatePure(ghost);
                        }

                        if (newEnt != null)
                        {
                            SyncRegistry.ApplyCommonProps(newEnt, ghost);
                            btr.AppendEntity(newEnt);
                            tr.AddNewlyCreatedDBObject(newEnt, true);
                            GhostManager.RemoveGhost(gId);
                            bakedCount++;
                        }
                    }

                    tr.Commit();
                    doc.Editor.UpdateScreen();
                }
            }
            finally
            {
                EventMonitor.IsBaking = false;
            }

            doc.Editor.WriteMessage($"\n[H-SYNC] BAKE completado: {bakedCount} hologramas persistidos en el DWG local.");
        }
        [CommandMethod("HSYNC_DEBUG")]
        public void HSyncDebug()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            
            ed.WriteMessage("\n--- H-SYNC DEBUG ---");
            ed.WriteMessage($"\nSocket Connected: {SocketClient?.IsConnected}");
            ed.WriteMessage($"\nUUIDs en OwnershipRegistry: {OwnershipRegistry.DumpAll()}");
            
            // Probar el Overrule directamente
            var res = ed.GetEntity("\nSelecciona entidad para forzar Shadowing: ");
            if (res.Status == PromptStatus.OK)
            {
                var id = res.ObjectId;
                HSync.Render.ShadowRegistry.Shadow(id);
                ed.WriteMessage($"\nSombreado aplicado a: {id.Handle.ToString().ToLowerInvariant()}");
                
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    ent.RecordGraphicsModified(true); // Forzar regeneración
                    tr.Commit();
                }
            }
        }
        /// <summary>
        /// Sprint 14: Transferencia de geometría Ghost → Nativa via SyncRegistry.
        /// </summary>
        private void UpdateNativeFromGhost(Entity nativeEnt, Entity ghost)
        {
            var sync = SyncRegistry.Get(ghost.GetType());
            if (sync != null)
            {
                sync.TransferGeometry(ghost, nativeEnt);
            }
            SyncRegistry.ApplyCommonProps(nativeEnt, ghost);
        }
    }
}
