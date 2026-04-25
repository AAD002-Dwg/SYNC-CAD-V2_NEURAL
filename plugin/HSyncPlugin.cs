using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using HSync.Core;
using HSync.Core.Network;
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
            Application.DocumentManager.DocumentDestroyed -= OnDocumentDestroyed;
            
            // Limpieza segura requerida por el motor nativo de AutoCAD
            UndoInterceptor.Terminate();
            HologramOsnapOverrule.Terminate();
            EventMonitor.Terminate();
            GhostManager.ClearAllGhosts();
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
                string newUrl = res.StringResult;
                
                // Cerrar conexión anterior si existía y cambió la URL
                if (SocketClient != null && ServerUrl != newUrl)
                {
                    SocketClient = null; 
                }
                
                ServerUrl = newUrl;
                ed.WriteMessage($"\n[H-SYNC] Servidor configurado: {ServerUrl}");
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

        [CommandMethod("HSYNC_CONNECT")]
        public async void ConnectToHub()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage($"\n[H-SYNC] Conectando al Hub Transaccional ({ServerUrl})...");
            
            try 
            {
                if (SocketClient == null || !SocketClient.IsConnected) 
                {
                    SocketClient = new SyncSocketClient(ServerUrl, "ALAN-ACAD");
                }

                // Iniciamos la conexión WebSocket y el ciclo de vida (Handshake)
                await SocketClient.ConnectAsync();
                await HandshakeManager.InitiateConnectAsync(SocketClient, 0);
                doc.Editor.WriteMessage("\n[H-SYNC] Conexion WebSocket Establecida. Modo Multi-Usuario Activado.");

                // Sprint 12: Auto-Discovery — Escanear BD y registrar entidades existentes
                int discovered = RunAutoDiscovery(doc);
                if (discovered > 0)
                {
                    doc.Editor.WriteMessage($"\n[H-SYNC] Auto-Discovery: {discovered} entidades registradas y enviadas al Hub.");
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[H-SYNC] Error de conexion: {ex.Message}");
            }
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

                        // Solo entidades compatibles con nuestro motor de diffing
                        if (!(ent is Line || ent is Circle || ent is Autodesk.AutoCAD.DatabaseServices.Polyline))
                            continue;

                        string uuid = id.Handle.ToString().ToLowerInvariant();

                        // Evitar duplicados si ya estaba registrado (ej: reconexión)
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
                ed.WriteMessage("\n[H-SYNC] Error: No estás conectado al Hub. Ejecuta HSYNC_CONNECT primero.");
                return;
            }

            var selResult = ed.GetSelection();
            if (selResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[H-SYNC] Selección cancelada.");
                return;
            }

            var ids = new System.Collections.Generic.List<string>();
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                foreach (ObjectId objId in selResult.Value.GetObjectIds())
                {
                    if (objId.IsErased) continue;
                    string uuid = objId.Handle.ToString().ToLowerInvariant();
                    ids.Add(uuid);
                }
                tr.Commit();
            }

            if (ids.Count == 0)
            {
                ed.WriteMessage("\n[H-SYNC] No se encontraron entidades válidas en la selección.");
                return;
            }

            // Enviar solicitud al Hub
            var request = new
            {
                type = "TEST_MUTATE_REQ",
                user = SocketClient.UserId,
                ids = ids
            };
            string json = System.Text.Json.JsonSerializer.Serialize(request);
            await SocketClient.SendDeltaAsync(json);

            ed.WriteMessage($"\n[H-SYNC] TEST_MUTATE_REQ enviado para {ids.Count} entidades. Esperando mutaciones del servidor...");
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

        [CommandMethod("HSYNC_BAKE")]
        public void BakeHolograms()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            
            var shadedIds = HSync.Render.ShadowRegistry.GetAllShaded();
            if (shadedIds.Length == 0)
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
                    foreach (var id in shadedIds)
                    {
                        if (id.IsErased) continue;
                        
                        string uuid = id.Handle.ToString().ToLowerInvariant();
                        var ghost = GhostManager.GetGhost(uuid);
                        if (ghost == null) continue;

                        var nativeEnt = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (nativeEnt != null)
                        {
                            // Actualizar geometría nativa desde el holograma canónico
                            if (nativeEnt is Autodesk.AutoCAD.DatabaseServices.Line natLine && ghost is Autodesk.AutoCAD.DatabaseServices.Line ghLine)
                            {
                                natLine.StartPoint = ghLine.StartPoint;
                                natLine.EndPoint = ghLine.EndPoint;
                            }
                            else if (nativeEnt is Autodesk.AutoCAD.DatabaseServices.Circle natCirc && ghost is Autodesk.AutoCAD.DatabaseServices.Circle ghCirc)
                            {
                                natCirc.Center = ghCirc.Center;
                                natCirc.Radius = ghCirc.Radius;
                            }
                            
                            // Por ahora, para simplificar Fase 1, limpiamos el Overrule
                            ShadowRegistry.Unshadow(id);
                            GhostManager.RemoveGhost(uuid);
                            bakedCount++;
                        }
                    }
                    tr.Commit();
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
    }
}
