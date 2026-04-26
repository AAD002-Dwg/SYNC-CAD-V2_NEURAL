# SyncRegistry: Motor Modular de Sincronización de Entidades

> Sprint 14 — Documento de Arquitectura Interna

## 1. Propósito

El `SyncRegistry` es el despachador central que elimina la necesidad de cadenas `if-else` por tipo de entidad en todo el plugin. Reemplaza ~220 líneas de código duplicado en 3 archivos (`GhostManager.cs`, `HSyncPlugin.cs`, `PayloadBuilder.cs`) por un sistema modular donde cada tipo de entidad AutoCAD tiene su propio archivo "Synchronizer" independiente.

## 2. Interfaz `IEntitySynchronizer`

Cada tipo de entidad implementa esta interfaz, que cubre las **5 operaciones críticas** del ciclo de vida colaborativo:

```
┌──────────────────────────────────────────────────────────────────┐
│                   IEntitySynchronizer                            │
├──────────────────────────────────────────────────────────────────┤
│  NativeType      → typeof(Line), typeof(Circle), etc.           │
│  TypeTag          → "LINE", "CIRCLE" (clave JSON para la red)   │
├──────────────────────────────────────────────────────────────────┤
│  SerializeGeometry()   → Entidad nativa → JSON (salida a red)   │
│  CreateGhost()         → Entidad nativa → Holograma visual      │
│  CreateGhostFromDelta()→ JSON de red → Holograma nuevo          │
│  ApplyDelta()          → JSON de red → Actualizar holograma     │
│  InstantiatePure()     → Holograma → Entidad limpia para DB     │
│  TransferGeometry()    → Copiar geometría Ghost → Entidad en DB │
└──────────────────────────────────────────────────────────────────┘
```

## 3. Flujo de Datos

### 3.1 Envío (Local → Red)
```
Usuario edita entidad nativa
        │
        ▼
EventMonitor detecta cambio
        │
        ▼
PayloadBuilder.BuildCreate()
        │
        ├─── SyncRegistry.Get(entity.GetType())
        │         │
        │         ▼
        │    sync.SerializeGeometry(entity, tr)  ← Delegación limpia
        │
        ▼
JSON enviado al Hub via WebSocket
```

### 3.2 Recepción (Red → Local)
```
Delta JSON llega por WebSocket
        │
        ▼
GhostManager.ApplyIncomingDelta()
        │
        ├─ ¿Ghost existe? ─── NO ──→ SyncRegistry.Get(typeTag)
        │                                    │
        │                                    ▼
        │                            sync.CreateGhostFromDelta(geom)
        │                                    │
        │                                    ▼
        │                            AddOrUpdateGhost()
        │
        └─ ¿Ghost existe? ─── SÍ ──→ SyncRegistry.Get(ghost.GetType())
                                             │
                                             ▼
                                     sync.ApplyDelta(ghost, geom)
                                             │
                                             ▼
                                     TransientManager.UpdateTransient()
```

### 3.3 Horneado (Bake: Holograma → Entidad Persistente)
```
Usuario ejecuta HSYNC_BAKE
        │
        ├─── Sombras Locales ──→ sync.TransferGeometry(ghost, nativeEnt)
        │                               │
        │                               ▼
        │                        nativeEnt actualizada en DB
        │
        └─── Fantasmas Remotos ──→ sync.InstantiatePure(ghost)
                                         │
                                         ▼
                                  Entidad nueva, limpia, sin corrupción
                                         │
                                         ▼
                                  btr.AppendEntity(newEnt)
```

## 4. Registro de Synchronizers

El `SyncRegistry` mantiene dos diccionarios internos para lookup O(1):

```csharp
Dictionary<Type, IEntitySynchronizer> _byType;   // Get(typeof(Line))
Dictionary<string, IEntitySynchronizer> _byTag;   // Get("LINE")
```

### Tipos Registrados (V27)

| TypeTag | Clase .NET | Synchronizer | Estado |
|---|---|---|---|
| `LINE` | `Line` | `LineSynchronizer` | ✅ Producción |
| `CIRCLE` | `Circle` | `CircleSynchronizer` | ✅ Producción |
| `POLYLINE` | `Polyline` | `PolylineSynchronizer` | ✅ Producción |
| `ARC` | `Arc` | `ArcSynchronizer` | ✅ Nuevo |
| `TEXT` | `DBText` | `TextSynchronizer` | ✅ Nuevo |
| `MTEXT` | `MText` | `MTextSynchronizer` | ✅ Nuevo |

### Tipos Pendientes (Sprints Futuros)

| TypeTag | Clase .NET | Complejidad | Notas |
|---|---|---|---|
| `ELLIPSE` | `Ellipse` | Media | Ejes como vectores 3D |
| `SPLINE` | `Spline` | Alta | Control points + knots |
| `BLOCKREF` | `BlockReference` | Media | Solo referencia, no geometría |
| `DYNBLOCK` | `BlockReference` (dinámico) | Alta | Propiedades dinámicas localizadas |
| `HATCH` | `Hatch` | Muy Alta | Boundary loops como referencias a otras entidades |
| `DIMENSION` | `Dimension` (abstracta) | Muy Alta | 6+ subtipos, dependencia de DimStyles |

## 5. Propiedades Comunes

Las propiedades heredadas de `Entity` (`ColorIndex`, `Layer`, `Linetype`) se manejan **una sola vez** en `SyncRegistry`, no en cada Synchronizer:

```csharp
SyncRegistry.ApplyCommonProps(target, source);      // Entity → Entity
SyncRegistry.ApplyCommonPropsFromJson(target, json); // JSON → Entity
```

Esto evita duplicar `target.ColorIndex = source.ColorIndex` en cada uno de los 6+ archivos.

## 6. Cómo Agregar un Nuevo Tipo

Para agregar soporte para un nuevo tipo de entidad (ejemplo: `Ellipse`):

1. Crear `Core/Sync/EllipseSynchronizer.cs` (~60-80 líneas).
2. Implementar `IEntitySynchronizer` con los 7 métodos.
3. Registrar en el constructor estático de `SyncRegistry`:
   ```csharp
   Register(new EllipseSynchronizer());
   ```
4. (Opcional) Agregar test `HSYNC_TEST_ELLIPSE` en `hub.js`.

**No se requiere ningún cambio en `GhostManager`, `HSyncPlugin` ni `PayloadBuilder`.**

## 7. Lecciones Aprendidas (Sprint 13 → 14)

| Problema | Causa Raíz | Solución |
|---|---|---|
| `eDegenerateGeometry` al hornear | `Entity.Clone()` corrompe Transients | `InstantiatePure()`: crear desde cero |
| Polilínea invisible durante animación | `RemoveVertexAt(0)` vacía la entidad | Actualización atómica: `SetPointAt()` + recorte desde el final |
| Cursores no se apagan | `EraseTransient` fallaba silenciosamente | Try-catch + `UpdateScreen()` forzado |
| `HSYNC_BAKE` decía "no hay hologramas" | Solo verificaba sombras locales, ignoraba fantasmas remotos | Verificar `activeGhostsCount > 0` también |

## 8. Referentes Externos

- **Speckle** (`speckle-sharp-connectors`): Patrón Converter con `ISpeckleConverter`. Diferencia clave: Speckle es batch, H-Sync es streaming (requiere `ApplyDelta` y `InstantiatePure` adicionales).
- **Autodesk Docs**: `DynamicBlockReferenceProperty` requiere verificar `ReadOnly` y `GetAllowedValues()` antes de escribir.

## 9. Estructura de Archivos

```
plugin/
├── Core/
│   ├── Sync/                              ← Sprint 14
│   │   ├── IEntitySynchronizer.cs         ← Interfaz + SyncRegistry
│   │   ├── LineSynchronizer.cs
│   │   ├── CircleSynchronizer.cs
│   │   ├── PolylineSynchronizer.cs
│   │   ├── ArcSynchronizer.cs
│   │   ├── TextSynchronizer.cs
│   │   └── MTextSynchronizer.cs
│   └── Network/
│       ├── Diffing/                       ← Sprint 11 (detección de cambios)
│       ├── PayloadBuilder.cs              ← Refactorizado: usa SyncRegistry
│       ├── WebSocketClient.cs
│       └── AppIdleManager.cs
├── Render/
│   ├── GhostManager.cs                    ← Refactorizado: usa SyncRegistry
│   ├── CursorManager.cs
│   └── ShadowOverrule.cs
└── HSyncPlugin.cs                         ← Refactorizado: usa SyncRegistry
```
