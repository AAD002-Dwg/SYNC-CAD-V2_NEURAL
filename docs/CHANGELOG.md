# Changelog (H-SYNC)

Registro formal de todos los cambios, mejoras continuas, y adiciones arquitectónicas realizadas en el repositorio **neural_v2**. 

El formato se basa en "Keep a Changelog" y respeta SemVer (versionado semántico). Emplearemos un enfoque sistemático y estricto donde **cada cambio estructural o lógico que toque la fase de diseño es documentado aquí preventivamente.**

### Added
- **Modularización del Motor de Entidades (Sprint 14):**
  - Implementación del patrón Strategy con `IEntitySynchronizer` y `SyncRegistry` (despachador O(1) por tipo de entidad).
  - Extracción de lógica duplicada de `GhostManager.cs`, `HSyncPlugin.cs` y `PayloadBuilder.cs` a 6 Synchronizers independientes.
  - **3 nuevos tipos de entidad soportados:** `Arc` (arcos), `DBText` (texto simple), `MText` (texto multilínea).
  - Centralización de propiedades comunes (`Color`, `Layer`, `Linetype`) en `SyncRegistry.ApplyCommonProps()`.
  - `AutoDiscovery` extendido para detectar automáticamente los nuevos tipos de entidad al conectarse.
  - Documentación técnica: `docs/NEURAL_SYNC_REGISTRY.md`.
- **Estabilización de Hologramas Colaborativos (Sprint 13):**
  - **Modo DirectTopmost restaurado:** Revertido de `TransientDrawingMode.Main` a `DirectTopmost` para eliminar errores `eDegenerateGeometry` causados por validaciones de DB asíncronas.
  - **Motor de Horneado Universal (`HSYNC_BAKE` V18→V26):** Reescrito para materializar tanto sombras locales como fantasmas remotos (bots/compañeros). Usa instanciación pura (no `Clone()`) para evitar corrupción de estado interno de AutoCAD.
  - **Actualización Atómica de Vértices:** Polilíneas ahora usan `SetPointAt()` + recorte desde el final en lugar de vaciar y recrear, eliminando la degradación silenciosa que las hacía invisibles.
  - **Cadencia Humana:** Intervalo del bot reducido de 150ms a 300ms para no saturar el hilo de UI de AutoCAD.
  - **Fix de cursores:** `ClearAllCursors()` ahora usa try-catch robusto y fuerza `UpdateScreen()` al desactivar.
  - **Blindaje Geométrico Total:** Validación de Radius > 0.001, vértices >= 2, y Length > 0.001 antes de inyectar geometrías en TransientManager.
- **Persistencia y Ciclo de Vida (Sprint 11):**
  - Implementación del "Bake Engine" (`HSYNC_BAKE`): Transforma los hologramas canónicos en geometría nativa definitiva escribiéndolos en el DWG y removiendo el Shadowing.
  - Prevención de Eco (Echo-Loop): Añadido `EventMonitor.IsBaking` para evitar que el Bake dispare deltas `UPDATE` redundantes.
  - Detección determinista de borrado (`DELETE`): El motor de diffing ahora detecta cuando una entidad nativa finaliza un comando con `.IsErased == true` y emite el delta correspondiente (superando la inestabilidad del evento `ObjectErased`).
  - Soporte para **Polilíneas (`LWPolyline`)**: Añadido soporte de extracción (`PayloadBuilder`), renderizado efímero (`GhostManager`), y Diffing atómico (`PolylineDiffer`). Las polilíneas se sincronizan íntegramente como un *Atomic Group* por LWW estricto para evitar corrupciones.
- **Conectividad y Flujo Nativo (Bonus Sprint 10):**
  - Heartbeat asíncrono (30s) en C# con `ALIVE_HEARTBEAT`.
  - Hub optimizado: cualquier mensaje recibido resetea el timer de inactividad.
  - `ShadowGripOverrule`: Completa el "Shadow Triplet" ocultando grips en `Ctrl+A`.
  - Emisión automática de deltas: `EventMonitor` ahora captura `CREATE` y `UPDATE` nativos sin intervención del tester.
  - `PayloadBuilder` compliant: Serialización compatible con `NEURAL_DATA_SCHEMA.md` (`props.geom`).
- **Shadow Triplet Completo (Sprint 9):** Implementación end-to-end del motor de Shadowing Colaborativo. Primera prueba exitosa de co-edición en tiempo real sobre AutoCAD nativo sin polucionar el UNDO stack.
  - `ShadowDrawOverrule`: Intercepta `WorldDraw`, devuelve `true` sin geometría. Purga la entidad del índice espacial haciéndola invisible e inseleccionable por clic/ventana.
  - `ShadowOsnapOverrule`: Intercepta `GetObjectSnapPoints`, evita que el cursor se ancle a entidades sombreadas.
  - `ShadowRegistry`: Diccionario estático con `SetIdFilter` para control O(1) de entidades sombreadas. Permanencia de sesión hasta Bake.
  - `OwnershipRegistry`: Puente Canónico-Proyectado. Registra entidades nativas creadas en sesión local para identificar colisiones remotas.
  - Ghost fresco (no Clone): Creación de entidades Transient desde cero (`new Circle()`) en lugar de `Entity.Clone()` que retiene estado de BD incompatible con `TransientManager`.
  - `ApplyMergedState`: Parseo de geometría canónica del servidor y reposicionamiento del Holograma a coordenadas ganadoras del LWW.
- **Correcciones críticas de red (Sprint 9):**
  - Fix `CancellationToken` en `ReceiveLoopAsync`: Elimina ReceiveLoops zombi al reconectar (`HSYNC_CONNECT` múltiple).
  - Fix `TryGetProperty("type")`: Los deltas crudos del hub no tienen campo `type`, `GetProperty` explotaba con `KeyNotFoundException`.
  - Fix `JsonElement` use-after-dispose: El `winnerState` era un puntero a memoria del `JsonDocument` destruido por el `using` block. Se serializa a `string` antes de salir.
  - Fix `DocumentLock`: Requerido para transacciones desde contextos no-comando (`AppIdle`). Sin él, AutoCAD lanza `eLockViolation`.
  - Fix `seqCache` envenenado en hub: `client_seq` del tester usaba `Math.random()` que generaba valores menores a los cacheados por idempotencia. Cambiado a `Date.now()`.
  - Timeout del hub subido a 600s (10 min) para desarrollo.
- **Test de Fuego (Sprint 8):** Ejecución de `tester.js` con colisiones LWW de 5ms. El test reveló una falla arquitectónica crucial: las mutaciones remotas sobre entidades nativas locales fallaban porque no existía un puente entre el estado "Canónico" (Servidor) y la entidad nativa "Proyectada".
- Juez Transaccional y Automerge (Sprint 6): Motor de diffing `_preCommandSnapshot` sin reflexión, mapeo de Mutaciones Parciales, mitigación de colisiones cruzadas usando `AppIdleManager`, y detección automática del comando COPY vía `Database.ObjectAppended`.
- Ruta HTTP GET `/api/snapshot` para tests BDD (AC-403).
- Ciclo de Vida y Handshake (Sprint 5): Enrutador PATCH/SNAPSHOT en Node, `HandshakeManager` asíncrono en C# (Task.Run), y limpieza de Auras por `DocumentDestroyed`.
- Pipeline de Streaming (Sprint 3-4): Implementación de `PayloadBuilder` con `System.Text.Json` puro, `DraftState` para privacidad local, y `WebSocketClient` con optimización WAN (`NoDelay = true`).
- Servidor Hub (Node.js) inicializado con lógica de Idempotencia y LWW básico para AC-203.
- Comando `HSYNC_HEAVY_TEST` implementado (Spike 1.5), inyectando exitosamente 10,000 entidades pesadas (MText/Circles) en ~1s sin impacto residual en FPS.
- Estructuras en C# consolidadas para Sprint 1-2: `GhostManager`, `UndoInterceptor` y `HologramOsnapOverrule`.
- Documentos de Definición de Producto (Esquema, Protocolo y DoD) para la Fase 1.
- Inicialización de estructura de directorios pura para `plugin` (C#) y `server` (Node).
- Aislamiento de código basado en pruebas empíricas exitosas del Spike de `TransientManager`.

### Changed
- Migración dictaminada de API C#: Se abandona .NET Framework 4.8. El proyecto a partir de este punto se configura pura y exclusivamente bajo .NET 8 (AutoCAD 2025).

### Deprecated
- SYNC-CAD v1.0 (Flujo de base de datos DWG completa y bloqueos) queda oficialmente deprecado a favor de H-SYNC Neural.
