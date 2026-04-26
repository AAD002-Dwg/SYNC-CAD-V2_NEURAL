# SYNC-CAD NEURAL v2: Informe de Revisión y Mejora

Este documento detalla los hallazgos técnicos tras la auditoría del código de la versión `neural_v2` y propone una hoja de ruta para la Fase 3 del proyecto.

## 1. Auditoría del Motor de Eventos (EventMonitor.cs)

### Hallazgo: Posible Duplicidad de Deltas
En `EventMonitor.cs`, el evento `OnObjectAppended` envía un `CREATE` de forma inmediata. Sin embargo, `OnCommandEnded` también tiene lógica para detectar objetos nuevos en `_newlyCreatedObjects`.

> [!WARNING]
> Actualmente `_newlyCreatedObjects.Add` no se está llamando en el código revisado. Si se activa, podrías estar enviando dos deltas `CREATE` para el mismo objeto, lo que causaría que los colaboradores vean dos entidades iguales.

**Recomendación:**
Mantener el envío inmediato en `OnObjectAppended` para una experiencia "Figma-like" (ver el objeto mientras se dibuja), pero asegurar que `OnCommandEnded` solo procese `UPDATE` y `DELETE` para evitar colisiones.

## 2. Optimización del Hub (hub.js)

### Hallazgo: Merge de Propiedades Atomizado
El hub actual hace un spread de las propiedades: `existing.props = { ...existing.props, ...delta.props }`. Esto es excelente, pero la reconciliación hacia los clientes (`RECONCILE_FIX`) envía el estado completo.

**Mejora Sugerida:**
Enviar solo la propiedad que cambió en el `RECONCILE_FIX` para ahorrar ancho de banda, especialmente en Polilíneas con miles de nodos.

```javascript
// En hub.js
if (delta.op === 'UPDATE') {
    // ... logic ...
    const fixMsg = JSON.stringify({
        type: 'RECONCILE_FIX',
        id: delta.id,
        changed: delta.props // Solo lo que cambió
    });
}
```

## 3. Robustez en C# (Async Patterns)

### Hallazgo: Peligro de `async void`
Los comandos como `ConnectToHub` usan `async void`. Si la conexión falla por un timeout de red fuera del `try-catch`, el proceso de AutoCAD podría cerrarse inesperadamente.

**Recomendación: Safe Command Wrapper**
```csharp
public static async Task SafeExecute(Func<Task> action, Editor ed)
{
    try { await action(); }
    catch (Exception ex) {
        ed.WriteMessage($"\n[H-SYNC ERROR] {ex.Message}");
    }
}

[CommandMethod("HSYNC_CONNECT")]
public void ConnectToHub() {
    _ = SafeExecute(async () => {
        // Lógica de conexión aquí
    }, Application.DocumentManager.MdiActiveDocument.Editor);
}
```

## 4. Hoja de Ruta de Entidades (Sprint 15+)

Para que el proyecto sea usable en producción, el `SyncRegistry` necesita expandirse. He priorizado las entidades por complejidad:

| Entidad | Dificultad | Reto Técnico |
| :--- | :--- | :--- |
| **Dimension** | Alta | Requiere sincronizar el `DimensionStyle` y puntos de definición. |
| **Hatch** | Muy Alta | El bucle (boundary) debe estar sincronizado antes que el patrón. |
| **BlockReference** | Media | Sincronizar el nombre del bloque y los atributos (AttributeCollection). |
| **LayerTable** | Media | No es una entidad, pero los cambios de color/visibilidad de capa deben ser globales. |

## 5. Implementación de un "Heartbeat" Visual
Para evitar que un usuario crea que está sincronizando cuando en realidad perdió la conexión:
- **Idea**: Agregar un pequeño icono o mensaje en la `StatusBar` de AutoCAD que cambie de color (Verde/Rojo) según el estado de `SocketClient.IsConnected`.

---
**Conclusión:** El proyecto tiene una base técnica excepcionalmente sólida. La transición a un modelo de **Estrategias** fue el movimiento correcto para permitir que el sistema crezca sin volverse inmanejable.
