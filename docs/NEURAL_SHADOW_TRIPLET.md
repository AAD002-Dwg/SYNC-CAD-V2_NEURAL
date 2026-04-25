# Shadow Triplet — Arquitectura de Shadowing Colaborativo

> **Sprint 9 — Validado End-to-End: 2026-04-24**

## 1. Problema

En un sistema colaborativo real, cualquier usuario puede editar cualquier entidad en cualquier momento. Cuando un usuario remoto modifica una entidad que existe nativamente en tu archivo DWG local, **no podemos simplemente modificar la BD local** porque:

1. La operación entraría en el stack de **UNDO** de AutoCAD — el usuario podría deshacer una acción que no realizó.
2. Cualquier cambio de propiedad genera un evento `ObjectModified` que el `EventMonitor` capturaría y emitiría como un delta falso a la red, contaminando el historial canónico.

## 2. Solución: Shadow Triplet

Tres `Overrules` coordinados que operan **puramente en la capa de renderizado**. Para la base de datos, la entidad nunca cambió.

### 2.1 ShadowDrawOverrule

```csharp
public class ShadowDrawOverrule : DrawableOverrule
{
    public override bool WorldDraw(Drawable drawable, WorldDraw wd)
    {
        if (ShadowRegistry.IsShaded(((Entity)drawable).ObjectId))
            return true; // Sin geometría → purga del índice espacial
        return base.WorldDraw(drawable, wd);
    }
}
```

**Efecto:** Al devolver `true` sin emitir geometría, AutoCAD purga la entidad de su índice espacial. Esto la hace:
- **Invisible** — no se dibuja en pantalla
- **Inseleccionable por clic/ventana** — el motor de selección geométrica no la encuentra

### 2.2 ShadowOsnapOverrule

```csharp
public class ShadowOsnapOverrule : OsnapOverrule
{
    public override void GetObjectSnapPoints(...)
    {
        if (ShadowRegistry.IsShaded(entity.ObjectId))
            return; // Sin puntos snap
        base.GetObjectSnapPoints(...);
    }
}
```

**Efecto:** El cursor no se ancla a la geometría sombreada.

### 2.3 ShadowGripOverrule

```csharp
public class ShadowGripOverrule : GripOverrule
{
    public override void GetGripPoints(Entity entity, GripDataCollection grips, ...)
    {
        if (ShadowRegistry.IsShaded(entity.ObjectId))
            return; // Sin grips
        base.GetGripPoints(...);
    }
}
```

**Efecto:** Bloquea la aparición de los "cuadraditos azules" (grips) si el usuario hace una selección profunda o `Ctrl+A` (Select All) evadiendo el índice espacial.

### 2.4 ShadowRegistry

```csharp
public static class ShadowRegistry
{
    private static readonly HashSet<ObjectId> _shaded = new();
    
    public static void Shadow(ObjectId id)
    {
        _shaded.Add(id);
        // Actualiza IdFilter en los Overrules
    }
    
    public static void Unshadow(ObjectId id) { ... } // Permite remover el Shadow (Sprint 11)

    public static bool IsShaded(ObjectId id) => _shaded.Contains(id);
}
```

**Efecto:** Control O(1) de qué entidades están sombreadas. El `SetIdFilter` asegura que los Overrules solo se apliquen a entidades registradas.

## 3. Flujo End-to-End

```
Tester (Beto envía UPDATE) 
  → Hub (LWW resolve, emite RECONCILE_FIX)
    → WebSocket Client (ReceiveLoop recibe JSON)
      → AppIdleManager (encola acción para hilo UI)
        → GhostManager.SetGlowRed(entityId)
          → OwnershipRegistry.IsOwnedLocally(id) ✓
            → ShadowRegistry.Shadow(nativeObjectId)     // Oculta nativo
            → new Circle() { Center, Radius, Color=1 }  // Ghost fresco
            → TransientManager.AddTransient(ghost)       // Inyecta en RAM
        → [2 segundos después]
        → GhostManager.ApplyMergedState(id, serverState)
          → Reposiciona ghost a coordenadas canónicas    // LWW ganador
```

## 4. Bugs Resueltos en Sprint 9

| Bug | Síntoma | Causa | Fix |
|-----|---------|-------|-----|
| ReceiveLoop zombi | Mensajes perdidos al reconectar | Viejo loop seguía leyendo del nuevo `_ws` | `CancellationToken` |
| `KeyNotFoundException` | Loop crash al recibir deltas crudos | `GetProperty("type")` en mensajes sin campo `type` | `TryGetProperty` |
| `JsonElement` use-after-dispose | Crash silencioso 2s después del RECONCILE_FIX | `winnerState` puntero a `JsonDocument` destruido | Serializar a `string` |
| `eLockViolation` | Ghost no se crea | Transacción desde `AppIdle` sin lock | `doc.LockDocument()` |
| Ghost invisible | Transient no renderiza | `Entity.Clone()` retiene estado de BD | Ghost fresco (`new Circle()`) |
| `seqCache` envenenado | Hub descarta deltas del tester | `client_seq` menor que valor cacheado | `Date.now()` como base |

## 5. Limitaciones Conocidas (Histórico Sprints 9-11)

1. ~~**Ctrl+A (Select All):** Bypassa el motor gráfico y selecciona la entidad sombreada desde la BD.~~ -> **Resuelto (Sprint 10)**: `ShadowGripOverrule`.
2. ~~**Color post-merge:** El ghost pierde el color rojo tras `ApplyMergedState`.~~ -> **Resuelto (Sprint 10)**.
3. ~~**Heartbeat:** El cliente C# no envía latidos. El hub desconecta tras 10min.~~ -> **Resuelto (Sprint 10)**: Timer periódico de 30s implementado.
4. ~~**CREATE nativo:** El plugin aún no emite deltas `CREATE` al servidor.~~ -> **Resuelto (Sprint 10)**: `EventMonitor.ObjectAppended` emite automáticamente.
5. ~~**Persistencia / Bake:** Los hologramas viven solo en RAM. Si se cierra el archivo, se pierden los cambios remotos.~~ -> **Resuelto (Sprint 11)**: Comando `HSYNC_BAKE` actualiza geometría de BD desde el holograma.
6. ~~**Detección de ERASE:** El motor no detecta borrados nativos.~~ -> **Resuelto (Sprint 11)**: Diffing de `IsErased` al final del comando emite delta `DELETE` determinista.
