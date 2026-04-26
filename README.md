# 🔄 SYNC-CAD Neural v2.0

> Motor de Co-Edición Atómica en Tiempo Real para AutoCAD — Estilo Figma, dentro de tu CAD.

[![Build Plugin](https://github.com/AAD002-Dwg/SYNC-CAD-V2_NEURAL/actions/workflows/build-plugin.yml/badge.svg)](https://github.com/AAD002-Dwg/SYNC-CAD-V2_NEURAL/actions/workflows/build-plugin.yml)

## 🎯 ¿Qué es?

SYNC-CAD Neural transforma AutoCAD en una plataforma colaborativa en tiempo real. Múltiples arquitectos pueden editar el mismo proyecto simultáneamente, viendo los cambios de sus compañeros aparecer instantáneamente como "hologramas" en su pantalla, sin bloquear archivos ni subir DWGs.

## 🏗️ Arquitectura

```
┌─────────────────┐          WebSocket           ┌──────────────────┐
│  AutoCAD + Plugin│ ◄──────────────────────────► │   Hub (Node.js)  │
│  (C# / .NET)    │    Deltas JSON atómicos      │   LWW + CRDT     │
│                  │                              │                  │
│  • GhostManager  │                              │  • stateMap      │
│  • ShadowTriplet │                              │  • deltaHistory  │
│  • EventMonitor  │                              │  • Heartbeat     │
│  • Bake Engine   │                              │  • Reconcile     │
└─────────────────┘                              └──────────────────┘
```

## 📦 Estructura del Proyecto

```
neural_v2/
├── plugin/                    # Complemento de AutoCAD (C#)
│   ├── Core/
│   │   ├── Sync/             # Sprint 14: Motor Modular de Entidades
│   │   │   ├── IEntitySynchronizer.cs  # Interfaz + SyncRegistry
│   │   │   ├── LineSynchronizer.cs
│   │   │   ├── CircleSynchronizer.cs
│   │   │   ├── PolylineSynchronizer.cs
│   │   │   ├── ArcSynchronizer.cs
│   │   │   ├── TextSynchronizer.cs
│   │   │   └── MTextSynchronizer.cs
│   │   ├── Network/          # WebSocket, Diffing, Payloads
│   │   ├── HologramOsnapOverrule.cs
│   │   └── UndoInterceptor.cs
│   ├── Render/
│   │   ├── GhostManager.cs   # Motor de Hologramas (RAM)
│   │   ├── CursorManager.cs  # Cursores remotos en tiempo real
│   │   └── ShadowOverrule.cs # Shadow Triplet
│   ├── HSyncPlugin.cs        # Punto de entrada
│   └── HSync.csproj
├── server/                    # Hub WebSocket (Node.js)
│   ├── hub.js                # Servidor principal (HTTP + WS)
│   ├── tester.js             # Bot BDD (Carla/Beto)
│   └── package.json
├── dashboard/                 # Panel Web (React + Vite)
│   ├── src/                  # Monitoreo en tiempo real
│   └── index.html
├── docs/                      # Documentación técnica
│   ├── CHANGELOG.md
│   ├── NEURAL_SYNC_REGISTRY.md  # Sprint 14: Arquitectura modular
│   ├── NEURAL_DATA_SCHEMA.md
│   └── NEURAL_SHADOW_TRIPLET.md
└── .github/workflows/        # CI/CD
    └── build-plugin.yml
```

## 🚀 Inicio Rápido

### Servidor (Hub)

```bash
cd server
npm install
npm start
# Hub corriendo en ws://localhost:3000
```

### Plugin de AutoCAD

1. Descargar la DLL desde [GitHub Actions](../../actions) → Artifacts:
   - **AutoCAD 2025-2027:** `HSync-NET8-AutoCAD2025-2027`
   - **AutoCAD 2019-2024:** `HSync-NET48-AutoCAD2019-2024`
2. En AutoCAD: `NETLOAD` → Seleccionar `HSync.dll`
3. Comando: `HSYNC_CONNECT`

### Comandos Disponibles

| Comando | Descripción |
|---------|-------------|
| `HSYNC_SERVER` | Configura la URL del Hub (ej: `wss://tu-app.onrender.com`). |
| `HSYNC_CONNECT` | Conectar al Hub + Auto-Discovery de entidades |
| `HSYNC_BAKE` | Persistir hologramas en el DWG local |
| `HSYNC_CURSORS` | Activar/Desactivar cursores remotos |
| `HSYNC_FOLLOW` | Seguir la cámara de otro usuario |
| `HSYNC_TEST_DRAW` | Test completo: Line + Circle + Polyline animados |
| `HSYNC_TEST_ME` | Seleccionar entidades y disparar mutaciones remotas |
| `HSYNC_TEST_CURSORS` | Test de cursores remotos animados |
| `HSYNC_CLEAR` | Purgar todos los hologramas |

### 📊 Dashboard Web (Monitoreo)

Ubicado en la carpeta `/dashboard`. Permite ver la salud del Hub y la actividad de los usuarios.

1. **Instalación:** `cd dashboard && npm install`
2. **Ejecución:** `npm run dev`
3. **Acceso:** `http://localhost:5173`

## 🌐 Deploy a Producción (Render)

Ver instrucciones detalladas en [`server/DEPLOY_RENDER.md`](server/DEPLOY_RENDER.md).

## 🔧 Compilación Local

```bash
cd plugin
dotnet build                      # Ambas versiones
dotnet build -f net48             # Solo .NET Framework 4.8
dotnet build -f net8.0-windows    # Solo .NET 8
```

## 📋 Sprints Completados

- **Sprint 1-8:** Motor Holográfico, OSNAP, Undo Protection, Ownership
- **Sprint 9:** Shadow Triplet (Ocultamiento visual sin BD)
- **Sprint 10:** Heartbeat, GripOverrule, EventMonitor (CREATE/UPDATE)
- **Sprint 11:** Bake Engine, Detección de ERASE, Soporte de Polilíneas
- **Sprint 12:** Auto-Discovery, HSYNC_TEST_ME, Soporte de Líneas en ApplyMergedState
- **Sprint 13:** Estabilización de hologramas, Anti-eDegenerateGeometry, Actualización atómica de vértices
- **Sprint 14:** Modularización (`SyncRegistry` + `IEntitySynchronizer`), soporte para Arc, Text y MText

## 📄 Licencia

Uso interno — AAD002-Dwg
