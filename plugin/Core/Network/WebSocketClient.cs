using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.Geometry;

namespace HSync.Core.Network
{
    /// <summary>
    /// Cliente puro de WebSockets para .NET y AutoCAD. (AC-201)
    /// </summary>
    public class SyncSocketClient
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts; // Controla el ciclo de vida del ReceiveLoop
        private readonly string _url;
        private readonly string _userId;
        private System.Timers.Timer _heartbeatTimer;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public string UserId => _userId;

        public SyncSocketClient(string host, string userId)
        {
            _url = host;
            _userId = userId;

            // AC-305: Inicializar el latido (30s)
            _heartbeatTimer = new System.Timers.Timer(30000);
            _heartbeatTimer.Elapsed += async (s, e) => await SendHeartbeat();
        }

        private async Task SendHeartbeat()
        {
            if (IsConnected)
            {
                try { await SendDeltaAsync("{\"type\":\"ALIVE_HEARTBEAT\"}"); }
                catch { /* Fallo silencioso */ }
            }
        }

        public async Task ConnectAsync()
        {
            // BUGFIX: Cancelar el ReceiveLoop anterior para evitar loops zombi
            _cts?.Cancel();
            _heartbeatTimer.Stop();
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconectando", CancellationToken.None); }
                catch { /* Ignorar si ya estaba muerto */ }
            }

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            
            // WAN OPTIMIZATION (AC-201):
            _ws.Options.KeepAliveInterval = TimeSpan.Zero; 

            try
            {
                await _ws.ConnectAsync(new Uri(_url), CancellationToken.None);
                
                // AC-305: Iniciar latido
                _heartbeatTimer.Start();

                // Iniciamos la oreja asíncrona con el token de cancelación
                _ = ReceiveLoopAsync(_ws, _cts.Token);
            }
            catch (Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC] Error de red: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket localWs, CancellationToken ct)
        {
            var buffer = new byte[8192];
            
            while (localWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await localWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    using (var doc = System.Text.Json.JsonDocument.Parse(msg))
                    {
                        string type = null;
                        if (doc.RootElement.TryGetProperty("type", out var typeProp))
                            type = typeProp.GetString();

                        if (type == "RECONCILE_FIX")
                        {
                            string entityId = doc.RootElement.GetProperty("id").GetString();
                            string winnerStateRaw = doc.RootElement.GetProperty("state").GetRawText();

                            AppIdleManager.EnqueueAction(() => {
                                HSync.Render.GhostManager.SetGlowRed(entityId);
                            });

                            Task.Delay(2000).ContinueWith(_ => {
                                AppIdleManager.EnqueueAction(() => {
                                    using (var stateDoc = System.Text.Json.JsonDocument.Parse(winnerStateRaw))
                                        HSync.Render.GhostManager.ApplyMergedState(entityId, stateDoc.RootElement);
                                });
                            });
                        }
                        else if (type == "SESSION_INIT")
                        {
                            string rawPayload = doc.RootElement.GetRawText();
                            _ = Task.Run(async () => {
                                using (var initDoc = System.Text.Json.JsonDocument.Parse(rawPayload))
                                    await HandshakeManager.HandleSessionInitAsync(initDoc.RootElement, this);
                            });
                        }
                        else if (type == "CURSOR")
                        {
                            string senderId = doc.RootElement.GetProperty("user").GetString();
                            if (senderId == _userId) continue; // No procesar nuestro propio cursor

                            var pos = doc.RootElement.GetProperty("pos");
                            var point = new Point3d(pos[0].GetDouble(), pos[1].GetDouble(), pos[2].GetDouble());
                            
                            // Actualizar visualización del cursor (Thread-safe via AppIdle)
                            AppIdleManager.EnqueueAction(() => {
                                HSync.Render.CursorManager.UpdateRemoteCursor(senderId, point);
                            });
                        }
                        else if (doc.RootElement.TryGetProperty("op", out var opProp))
                        {
                            // ¡ESTE ES EL MOTOR DE SYNC EN VIVO!
                            // Si el mensaje tiene una "op" (CREATE, UPDATE, DELETE), es un Delta de geometría.
                            string op = opProp.GetString();
                            string id = doc.RootElement.GetProperty("id").GetString();
                            string rawDelta = doc.RootElement.GetRawText();

                            AppIdleManager.EnqueueAction(() => {
                                try {
                                    using (var deltaDoc = System.Text.Json.JsonDocument.Parse(rawDelta))
                                    {
                                        if (op == "DELETE") {
                                            HSync.Render.GhostManager.RemoveGhost(id);
                                        } else {
                                            // Para CREATE y UPDATE, inyectamos o actualizamos el holograma
                                            var deltaElem = deltaDoc.RootElement;
                                            if (deltaElem.TryGetProperty("props", out var props) && props.TryGetProperty("geom", out var geom)) {
                                                // Reutilizamos ApplyMergedState para actualizar la geometría del ghost
                                                // Si el ghost no existe, primero lo creamos (lógica simple para Fase 2)
                                                HSync.Render.GhostManager.ApplyIncomingDelta(id, deltaElem);
                                            }
                                        }
                                    }
                                } catch (Exception ex) {
                                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC ERR] Delta Sync Error: {ex.Message}");
                                }
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Cancelación limpia por reconexión
                }
                catch (Exception ex)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager
                        .MdiActiveDocument?.Editor.WriteMessage($"\n[H-SYNC NET] Loop Crash: {ex.Message}");
                    break;
                }
            }
        }

        /// <summary>
        /// Emisión asíncrona agresiva de Deltas.
        /// </summary>
        public async Task SendDeltaAsync(string rawJson)
        {
            if (!IsConnected) return;
            
            var bytes = Encoding.UTF8.GetBytes(rawJson);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
