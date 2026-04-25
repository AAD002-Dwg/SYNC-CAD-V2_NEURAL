const { WebSocketServer, WebSocket } = require('ws');

const PORT = process.env.PORT || 3000;

// Estado en Memoria (RAM-based para Fase 1)
const seqCache = new Map(); // Para Idempotencia (AC-203)
const deltaHistory = [];    // Historial de la sala (Últimos 50 deltas para el Dashboard)
const stateMap = new Map(); // AC-401: Mapeo exacto UUID -> Estado Consolidado
let globalServerSeq = 1000;

// Temporizadores de latidos (Heartbeat - AC-305)
const aliveTimers = new Map();

// AC-403: Servidor HTTP para Validación de Estado
const http = require('http');
const server = http.createServer((req, res) => {
    // CORS para el Dashboard
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

    if (req.method === 'OPTIONS') {
        res.writeHead(204);
        res.end();
        return;
    }

    if (req.url === '/api/status' && req.method === 'GET') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
            history: deltaHistory.slice(-20).reverse(),
            stateCount: stateMap.size,
            activeUsers: Array.from(new Set([...wss.clients].map(c => c.userId).filter(Boolean)))
        }));
    } else if (req.url === '/api/snapshot' && req.method === 'GET') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(Array.from(stateMap.values())));
    } else {
        res.writeHead(404);
        res.end();
    }
});
server.listen(PORT, () => {
    console.log(`[H-SYNC HUB] Iniciando servidor HTTP/WS NEURAL en puerto ${PORT}...`);
});

const wss = new WebSocketServer({ server });

wss.on('connection', (ws, req) => {
    const ip = req.socket.remoteAddress;
    ws.userId = null; // Se asienta durante el CONNECT_REQ

    // Configurar Timeout inicial de 120s
    resetHeartbeatTimer(ws);

    ws.on('message', (data) => {
        resetHeartbeatTimer(ws);
        try {
            const payload = JSON.parse(data.toString('utf8'));

            // Handshake & Lifecycle Routing
            switch (payload.type) {
                case 'CONNECT_REQ':
                    handleConnectReq(ws, payload);
                    break;
                case 'ALIVE_HEARTBEAT':
                    // Ya se reseteó arriba
                    break;
                case 'DISCONNECT_REQ':
                    handleDisconnect(ws);
                    break;
                case 'OFFLINE_PUSH':
                    handleOfflinePush(ws, payload);
                    break;
                case 'TEST_CURSOR_START':
                    handleTestCursorStart(ws, payload);
                    break;
                case 'TEST_MUTATE_REQ':
                    handleTestMutateReq(ws, payload);
                    break;
                default:
                    // Es un Delta de Geometría estándar
                    handleStandardDelta(ws, payload);
                    break;
            }
        } catch (error) {
            console.error(`[!] Payload corrupto de ${ip}:`, error.message);
        }
    });

    ws.on('close', () => {
        handleDisconnect(ws);
    });
});

function handleConnectReq(ws, payload) {
    ws.userId = payload.user;
    resetHeartbeatTimer(ws);

    const clientSeq = payload.checkpointSeq || 0;
    const diff = globalServerSeq - clientSeq;

    let syncMode = 'PATCH';
    let payloadData = [];

    // Lógica AC-301 y AC-302
    if (diff > 5000 || clientSeq === 0) {
        syncMode = 'SNAPSHOT';
        // Construir Snapshot a partir del StateMap consolidado (AC-401)
        payloadData = Array.from(stateMap.values()); 
    } else {
        // Entregar solo los deltas que le faltan (PATCH)
        payloadData = deltaHistory.filter(d => d.server_seq > clientSeq);
    }

    ws.send(JSON.stringify({
        type: 'SESSION_INIT',
        syncMode: syncMode,
        serverSeq: globalServerSeq,
        data: payloadData
    }));
}

function handleOfflinePush(ws, payload) {
    // AC-303: Chunks de Offline Push
    // Payload = { type: 'OFFLINE_PUSH', chunkId: 1, total: 3, deltas: [...] }
    payload.deltas.forEach(d => handleStandardDelta(ws, d, false)); // No brodcastear aún
    
    // Ack para que mande el siguiente
    ws.send(JSON.stringify({
        type: 'CHUNK_ACK',
        chunkId: payload.chunkId
    }));
}

function handleStandardDelta(ws, delta, broadcast = true) {
    const cacheKey = `${delta.projectId}_${delta.user}`;
    const lastSeq = seqCache.get(cacheKey) || 0;

    if (delta.client_seq && delta.client_seq <= lastSeq) return; // Idempotencia
    seqCache.set(cacheKey, delta.client_seq);

    delta.server_seq = ++globalServerSeq;
    delta.timestamp = Date.now();
    
    // RAM History (Guardar solo los últimos 50 eventos para el Dashboard)
    deltaHistory.push({
        id: delta.id,
        op: delta.op,
        user: delta.user,
        timestamp: delta.timestamp,
        type: delta.type
    });
    if (deltaHistory.length > 50) deltaHistory.shift();

    // AC-401: Fusión de Estado (Automerge simulation)
    if (delta.op === 'CREATE') {
        stateMap.set(delta.id, delta);
        console.log(`[HUB] 🟢 Entidad registrada: ${delta.id} (${delta.type}) por ${delta.user}`);
    } else if (delta.op === 'UPDATE') {
        const existing = stateMap.get(delta.id);
        if (existing) {
            if (delta.props) {
                existing.props = { ...existing.props, ...delta.props };
            }
            existing.user = delta.user;
            existing.server_seq = delta.server_seq;
            existing.timestamp = delta.timestamp;

            const fixMsg = JSON.stringify({
                type: 'RECONCILE_FIX',
                id: delta.id,
                state: existing.props
            });
            broadcastMessage(fixMsg, ws);
        } else {
            console.log(`[HUB] ⚠️ UPDATE para '${delta.id}' IGNORADO: no existe en stateMap`);
        }
    } else if (delta.op === 'DELETE') {
        stateMap.delete(delta.id);
        console.log(`[HUB] 🔴 Entidad eliminada: ${delta.id}`);
    }
    
    // Broadcast el delta original (para cursores y otros updates en vivo)
    if (broadcast) {
        broadcastMessage(JSON.stringify(delta), ws);
    }
}

function handleDisconnect(ws) {
    if (aliveTimers.has(ws)) {
        clearTimeout(aliveTimers.get(ws));
        aliveTimers.delete(ws);
    }

    if (ws.userId) {
        console.log(`[-] Usuario desconectado: ${ws.userId}. Limpiando Auras... (AC-304/305)`);
        broadcastMessage(JSON.stringify({
            type: 'CURSOR_REMOVE',
            user: ws.userId
        }));
        ws.userId = null;
    }
}

function resetHeartbeatTimer(ws) {
    if (aliveTimers.has(ws)) {
        clearTimeout(aliveTimers.get(ws));
    }
    
    // AC-305: Expulsión a los 10min sin latido (600s para desarrollo)
    const timer = setTimeout(() => {
        console.log(`[!] Timeout de sesión por inactividad. Expulsando socket...`);
        ws.terminate(); 
        handleDisconnect(ws);
    }, 600000);

    aliveTimers.set(ws, timer);
}

function broadcastMessage(rawMsg, excludeWs = null) {
    wss.clients.forEach((client) => {
        if (client !== excludeWs && client.readyState === WebSocket.OPEN) {
            client.send(rawMsg);
        }
    });
}

// Sprint 12: Bot de Mutación Automática por Selección
function handleTestMutateReq(ws, payload) {
    const ids = payload.entities || payload.ids || [];
    const requesterUser = payload.user || 'TESTER';
    const botUser = 'TEST_BOT';
    const colors = [1, 2, 3, 4, 5, 6]; // Rojo, Amarillo, Verde, Cyan, Azul, Magenta
    
    console.log(`[HUB] TEST_MUTATE_REQ de '${requesterUser}' para ${ids.length} entidades: [${ids.join(', ')}]`);

    ids.forEach((id, index) => {
        const existing = stateMap.get(id);
        if (!existing) {
            console.log(`[HUB] ⚠️ TEST_MUTATE: Entidad '${id}' no existe en stateMap. Ignorando.`);
            return;
        }

        // Generar mutación aleatoria basada en el tipo de geometría
        const offset = (Math.random() - 0.5) * 40; // Desplazamiento aleatorio ±20 unidades
        const color = colors[index % colors.length];
        let newGeom = {};

        if (existing.props && existing.props.geom) {
            const geom = existing.props.geom;
            if (geom.center) {
                // Es un círculo
                newGeom = {
                    center: [
                        geom.center[0] + offset,
                        geom.center[1] + offset,
                        0
                    ],
                    radius: (geom.radius || 5) * (0.5 + Math.random())
                };
            } else if (geom.start && geom.end) {
                // Es una línea
                newGeom = {
                    start: geom.start, // Mantener el inicio
                    end: [
                        geom.end[0] + offset,
                        geom.end[1] + offset,
                        0
                    ]
                };
            } else if (geom.nodes) {
                // Es una polilínea - rotar todos los nodos ligeramente
                newGeom = {
                    nodes: geom.nodes.map(n => [n[0] + offset, n[1] + offset]),
                    isClosed: geom.isClosed
                };
            }
        }

        // Crear el delta como si fuera un usuario externo (para que dispare el Shadowing)
        const delta = {
            op: 'UPDATE',
            id: id,
            type: existing.type, // Sprint 12: Asegurar que el tipo viaje en la mutación
            projectId: 'PROJ-TEST-AC601',
            client_seq: Date.now() + index,
            server_seq: null,
            user: botUser,
            state: 'LIVE',
            props: { geom: newGeom, color: color }
        };

        // Procesar como delta estándar (enviamos null para que el broadcast incluya al solicitante)
        handleStandardDelta(null, delta, true);
    });

    console.log(`[HUB] TEST_MUTATE completado: ${ids.length} mutaciones generadas por '${botUser}'.`);
}

function handleTestCursorStart(ws, payload) {
    const botName = " MARIA-BOT\;
 let angle = 0;
 const radius = 100;
 const center = payload.center || [0, 0, 0];
 
 console.log(\[HUB] ?? Activando Bot de Cursores:  orbitando en \);

 let steps = 0;
 const interval = setInterval(() => {
 angle += 0.1;
 steps++;
 
 const x = center[0] + radius * Math.cos(angle);
 const y = center[1] + radius * Math.sin(angle);
 
 const cursorMsg = JSON.stringify({
 type: \CURSOR\,
 user: botName,
 pos: [x, y, 0]
 });
 
 broadcastMessage(cursorMsg);

 if (steps > 300) { // Parar despu�s de 30 segundos
 clearInterval(interval);
 console.log(\[HUB] ?? Bot  ha terminado su recorrido.\);
 broadcastMessage(JSON.stringify({ type: \CURSOR_REMOVE\, user: botName }));
 }
 }, 100);
}
