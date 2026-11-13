# Plan · Velador

Qué sigue y en qué orden. El avance real vive en `state.md`. Nada de la Fase 2 en adelante se construye hasta aprobar la propuesta (`propuesta/`) y este plan.

## Decisiones

Cerradas el 2026-11-12 con el usuario.

| # | Tema | Elegido |
|---|---|---|
| 1 | Nombre | Velador. Identificador `com.reydavidag.velador`. |
| 2 | Quién graba | La misma máquina que se usa (Mac o PC con Windows). El motor queda separado de la ventana para poder moverlo a un equipo siempre prendido sin reescribir. |
| 3 | Stack | Tauri 2 (Rust) + motor .NET. Front sin React: HTML, CSS y módulos ES. |
| 4 | Repo | Público, MIT, sin datos de la cámara. |

Aprobadas por el usuario el 2026-11-12 junto con la propuesta. Pasaron por tres revisiones (diseño, arquitectura, continuidad) y por pruebas con la cámara real (`docs/verification.md`).

| # | Tema | Recomendación | Por qué |
|---|---|---|---|
| 5 | Pieza de video | **MediaMTX** (MIT) como único servidor de medios, supervisado por el motor y escondido detrás de él. FFmpeg (build LGPL) para recortes y miniaturas. | Medido con la cámara: 0.6 % de un núcleo y 56 MB grabando los dos streams; 30 s de video por rango en 40 ms; al matarlo se perdieron 0.77 s. Ni FFmpeg solo ni go2rtc lo igualan. |
| 6 | Audio | La cámara manda G.711. En vivo va directo por WebRTC. En disco MediaMTX lo guarda como PCM sin comprimir, que Chrome reproduce. Los recortes salen con audio **AAC**. | Medido. Falta confirmar el PCM en WKWebView; si no suena, el motor lo convierte a AAC al servirlo (0.5 % de un núcleo). |
| 7 | Vivo | WebRTC (WHEP), con la señalización reenviada por el motor. | Menos de 1 s de retraso sin recodificar. |
| 8 | .NET | .NET 10 (LTS hasta nov-2028), autocontenido, un solo archivo. En esta Mac hay SDK 8, que terminó soporte el 2026-11-10. | Un ejecutable por plataforma, sin instalar runtime. |
| 9 | Índice | SQLite en WAL (`velador.db`), tiempos en epoch ms UTC. | Abrir un día es una consulta, no listar archivos. |
| 10 | Sucesos | Los eventos de la propia cámara por ONVIF (movimiento, persona, manipulación): cero CPU. Detección propia sólo si no bastan. | Verificado: el evento de movimiento llegó al mover la imagen. |
| 11 | Móvil | Sin app aparte: el mismo front por navegador a través de Tailscale. | El motor ya lo sirve por un solo puerto. |
| 12 | Cero huecos | Mover el motor a un equipo siempre prendido (la PC vieja) y dejar esta máquina como visor. Es el mismo código. El relevo en paralelo queda descartado por ahora. | Es lo único que quita los huecos por reposo, y no necesita fusionar grabaciones. |

## Arquitectura

```
Velador.app / Velador.exe (Tauri 2 · Rust)        Motor (Velador.Engine · .NET)  127.0.0.1:8352
├─ Ventana ── http://127.0.0.1:8352 ─────────────► API + front + /live (WHEP) + /media (video por rango)
├─ Página de respaldo ── «Iniciar la guardia» ───► LaunchAgent / tarea de inicio de sesión
├─ Bandeja ── GET /api/summary cada 30 s          ├─ Supervisor ──► MediaMTX (sólo 127.0.0.1, sin RTSP/HLS/RTMP de salida)
└─ Avisos del sistema ◄── sucesos y huecos        │     ├─ RTSP TCP ◄── cámara: stream1 (1080p) y stream2 (720p)
                                                  │     └─ fMP4 en partes de 1 s; avisa cada segmento al motor
                                                  ├─ Continuidad: vigía, latido, no dormir
                                                  ├─ Índice SQLite: segmentos, huecos, sucesos, recortes
                                                  ├─ Retención: sólo el motor borra (días y % de disco)
                                                  ├─ ONVIF: PTZ y eventos de la cámara
                                                  └─ FFmpeg: audio a AAC, recortes, miniaturas
```

- **Un solo puerto.** El front nunca habla con MediaMTX: el motor reenvía la señalización del vivo y el video grabado bajo `/api/cameras/{id}/live` y `/media`. Así cambiar MediaMTX toca una carpeta, y Tailscale expone una sola cosa.
- La ventana es una vista más hacia el motor, como en Bodega: carga la URL local, sin IPC en la página remota, con candado de navegación. Tauri no lanza el motor como proceso hijo.
- Puerto `8352` (V-E-L-A en el teclado del teléfono), configurable.

### Seguridad

- MediaMTX escucha sólo en `127.0.0.1`. Su configuración la escribe el motor y se prueba: por omisión MoQ queda abierto en todas las interfaces (visto en la verificación) y acepta cualquier origen.
- La API de configuración de MediaMTX devuelve la dirección de la cámara con contraseña: el motor nunca la reenvía.
- Sesión por cookie: el motor guarda un token en un archivo `0600`; la ventana abre `/?t=…`, el motor lo cambia por una cookie `HttpOnly; SameSite=Strict` y redirige. Un `<video>` no puede mandar cabeceras, la cookie sí viaja.
- El motor valida `Host` (contra DNS rebinding) y `Origin` en toda escritura.
- El celular se empareja con un código de un solo uso que muestra la ventana.
- La contraseña de la cámara vive en el llavero del sistema y llega a MediaMTX por variable de entorno; nunca en archivos, URL registradas ni logs.

## Continuidad: los menos huecos posibles, y todos a la vista

Grabar en la máquina de uso diario es la decisión que más huecos causa. Con franqueza: **ningún programa garantiza cero huecos aquí**. Lo que sí se puede es prevenir los evitables, detectar los demás en segundos y no esconder ninguno.

1. **El motor no depende de la ventana.** LaunchAgent (macOS) o tarea al iniciar sesión (Windows), con reinicio automático. Cerrar la ventana no corta nada. Al actualizar, el motor nuevo readopta el MediaMTX que ya está grabando; sólo actualizar MediaMTX corta, unos segundos, con causa `update`.
2. **El equipo no se duerme solo.** Aserción `PreventUserIdleSystemSleep` en macOS y `PowerCreateRequest` en Windows (visible en `powercfg /requests`). Sólo evita el reposo por inactividad.
3. **Vigía.** El motor observa a MediaMTX: avisos de fuente en línea y fuera de línea, bytes recibidos, que el archivo crezca y que el proceso responda. Si pasan 5 s sin imagen nueva, reinicia la ruta. Separa cámara de red (ping a la cámara y a la puerta de enlace) y de contraseña rechazada (sin reintentos en ráfaga). Al despertar el equipo reconecta de inmediato.
4. **Se guarda cada segundo.** fMP4 en partes de 1 s: un corte de luz pierde a lo mucho un segundo y el archivo sigue sirviendo.
5. **Cambio de archivo sin corte.** MediaMTX rota en un cuadro clave sin cerrar la conexión (la cámara manda uno cada ~2 s, medido).
6. **Latido.** El motor escribe cada 5 s que está vivo. Al arrancar compara: así sabe si el hueco fue por reposo, por caída del motor o por apagón.
7. **Todo hueco queda a la vista.** En el índice con causa y duración, rayado en la ronda, contado en la cobertura, y con aviso del sistema si pasa de 30 s.

Causas: `sleep`, `camera`, `network`, `engine`, `power`, `update`, `disk`, `paused`, `privacy`, `credentials`, `unknown`.

**Límites honestos.** No se puede impedir: cerrar la tapa, el botón de reposo, la batería baja, cerrar la sesión, el reinicio por actualización del sistema. Y los huecos de la cámara (WiFi, reinicio, luz) no los evita ningún programa: sólo buena señal y un no-break en cámara y router.

**El camino a cero huecos por equipo** es la decisión 12: el mismo motor en un equipo siempre prendido, instalado sin depender de la sesión (LaunchDaemon o servicio de Windows, con la contraseña en el llavero de sistema o DPAPI de máquina). El relevo en paralelo se descarta por ahora: deja huecos en cada traspaso, obliga a fusionar grabaciones y gasta sesiones RTSP (la cámara rechazó dos de cuatro conexiones a `stream1`).

## Escalabilidad y rendimiento

- **Más cámaras:** todo lleva `cameraId` desde el primer día. La interfaz muestra una y pasa a mosaico sin cambiar rutas.
- **Más visores:** MediaMTX reparte el mismo stream sin abrir más conexiones a la cámara.
- **Meses de grabación:** segmentos de 1 h (24 filas por día y cámara). El índice se alimenta de los avisos de segmento de MediaMTX, nunca escaneando; el escaneo sólo al arrancar. Los tramos no se guardan: se funden segmentos contiguos al leer.
- **Zona horaria:** disco e índice en UTC; el día local (de 23, 24 o 25 h) es asunto de la API.
- **Ronda:** `GET /api/cameras/{id}/rounds/{date}?tz=` → `{date, tz, startMs, endMs, coverage, live, spans[], gaps[], events[]}`, con `ETag` fuerte para días pasados. El video se pide por rango y cada salto es una petición nueva (40 ms medidos).
- **Miniaturas y arrastre rápido:** se graba también `stream2` (720p, ~0.5 Mbps); de ahí salen miniaturas, vista previa al pasar el cursor y vista remota, sin decodificar el 1080p.
- **Retención:** MediaMTX no borra nada; sólo el motor, del más viejo al más nuevo, por días y por % de disco, saltando lo que cubre un recorte. Piso de espacio libre y retención de emergencia antes de dejar de grabar.
- **Disco:** medido de noche con MediaMTX, audio incluido: `stream1` 1.24 Mbps y `stream2` 0.37 Mbps, unos 17 GB por día los dos.

Presupuestos a medir en la Fase 2 (estimaciones, no mediciones): motor en reposo ≤ 0.5 % de un núcleo y ≤ 100 MB; MediaMTX ≤ 2 % y ≤ 80 MB por cámara; vivo ≤ 500 ms de retraso y primer cuadro ≤ 2 s; ronda ≤ 50 ms en la API; instalador ≤ 200 MB. El costo real está en la detección, las miniaturas y los vatios de no dormir, no en el motor ni en el front.

### Índice

```
cameras(id, slug UNIQUE, name)
segments(id, camera_id, stream, start_ms, end_ms, path, bytes, source)   UNIQUE(camera_id, stream, start_ms)
gaps(id, camera_id, start_ms, end_ms NULL, cause)                        INDEX(camera_id, start_ms)
events(id, camera_id, start_ms, end_ms, kind, score)                     INDEX(camera_id, start_ms), (camera_id, kind, start_ms)
clips(id, camera_id, start_ms, end_ms, path, bytes, title)
engine_state(last_alive_ms)
```

Del disco se reconstruyen segmentos y tramos. Los huecos reconstruidos quedan con causa `unknown`; sucesos y recortes no se reconstruyen, por eso hay respaldo diario de `velador.db`.

## Estructura

```
Velador/
├─ CLAUDE.md  plan.md  state.md  README.md  CHANGELOG.md  CONTRIBUTING.md  LICENSE  THIRD_PARTY_NOTICES.md
├─ propuesta/                    Fase 0: prototipo (se sirve, no se compila)
├─ docs/                         research.md, verification.md, api.md
├─ Velador.slnx
├─ Velador.Core/                 dominio y lógica pura: Camera, Segment, Gap, Event, Clip; cálculo de huecos, ronda, retención
├─ Velador.Engine/               host: Worker + API mínima + archivos de web/
│  ├─ Api/                       endpoints por recurso
│  ├─ MediaMtx/  Ffmpeg/  Onvif/  Sqlite/
│  └─ Secrets/  Power/           una implementación por plataforma
├─ Velador.Tests/                xUnit: huecos, retención, vigía, ronda con cambio de horario, reconstrucción
├─ web/
│  ├─ index.html
│  ├─ styles/                    tokens.css, app.css, motion.css
│  └─ js/
│     ├─ app.js  router.js  store.js
│     ├─ services/               api.js, live.js (WebRTC), playback.js
│     ├─ components/             round_timeline.js, player.js, ptz_pad.js, combobox.js, …
│     ├─ views/                  live_view.js, round_view.js, events_view.js, clips_view.js, cameras_view.js, continuity_view.js, settings_view.js, welcome_view.js
│     └─ utils/                  time.js, format.js, validate.js
├─ desktop/
│  ├─ art/  fallback/
│  └─ src-tauri/src/             main.rs, engine.rs, tray.rs, alerts.rs, nav.rs, text.rs
└─ scripts/                      fetch_binaries (MediaMTX y FFmpeg con suma verificada), sample_data
```

Tres proyectos, no cinco: para este tamaño, `Application` e `Infrastructure` aparte son ceremonia. Interfaces sólo donde hay dos implementaciones reales o un falso útil: `ISecretStore` e `IPowerGuard` (macOS y Windows), `ICameraControl` (falso para el modo demo), `IMediaServer` (delgada) e `IRecordingStore` (espacio libre y borrado, para probar la retención). El índice es una clase concreta probada con SQLite en memoria; el tiempo entra por `TimeProvider`.

## Fases

### 0 · Propuesta (ahora)

- [x] Estructura, vocabulario y reglas (`CLAUDE.md`).
- [x] Prototipo navegable: En vivo, Ronda, Sucesos, Recortes, Cámaras, Continuidad, Ajustes, Bienvenida y Escritorio (bandeja, avisos y estados de la guardia).
- [x] Revisión de diseño, arquitectura y continuidad; hallazgos aplicados.
- [x] Aprobada por el usuario el 2026-11-12.

### 1 · Verificación con la cámara real

Hecha el 2026-11-12 (`docs/verification.md`): streams, códecs, peso, CPU, sesiones simultáneas, MediaMTX grabando los dos streams, video por rango, vivo por WebRTC en Chrome, corte a la fuerza, fugas de contraseña, puertos, PTZ y eventos por ONVIF.

- [x] Decisiones 5, 7 y 10 cerradas con datos.
- [ ] WebRTC y audio PCM dentro de la ventana de Tauri, en WKWebView y WebView2 (cierra la decisión 6). Se hace con la ventana mínima de la Fase 4 o antes con Safari.
- [ ] Corrida de 2 h o más y corte del WiFi de la cámara: en cuánto vuelve MediaMTX por su cuenta.
- [ ] En la app Tapo: «Auto Reboot» y modo privacidad.

### 2 · Motor

Ramas `feature/engine-record`, `feature/engine-continuity`, `feature/engine-api`, `feature/engine-retention`.

- [x] Supervisor de MediaMTX: escribe su configuración (sólo `127.0.0.1`, MoQ y salidas apagadas, `%z` en el nombre de archivo, rutas sin guion bajo), lo readopta si ya corre, lo reinicia si muere.
- [x] Continuidad: vigía, latido, clasificación de cortes y no dormir, con pruebas. Falta el aviso del sistema, la tapa y el cierre de sesión de la matriz, y detectar contraseña rechazada (hoy se ve como «cámara»).
- [x] Huecos y estado en SQLite. Los tramos salen de la lista de MediaMTX, que ya funde segmentos contiguos: no hizo falta tabla de segmentos ni avisos de segmento.
- [x] API con sesión: `summary`, `rounds/{date}`, `media`, `live`.
- [ ] API: `cameras` (alta y edición), `events`, `clips`, `settings`, `ptz`. Contrato en `docs/api.md`.
- [ ] Descubrimiento ONVIF, alta con IP a mano, errores de cuenta de cámara; la cámara se identifica por MAC para sobrevivir a un cambio de IP.
- [ ] Miniaturas desde `stream2`.
- [x] Retención por días y por disco, con piso de espacio libre; nunca borra el archivo en curso ni lo que cubre un recorte.
- [ ] Modo demo (`VELADOR_DEMO=1`) con datos ficticios.
- **Listo cuando** graba 24 h con cobertura de 100 % y pasa la matriz de continuidad: reposo y despertar, tapa cerrada, cierre de sesión, `kill -STOP` a MediaMTX, reinicio de la cámara y del router, disco lleno y cambio de hora; cada caso deja su hueco con la causa correcta.

### 3 · Web

- [x] En vivo por WebRTC y Ronda (video por rango, arrastre, zoom, saltar huecos, velocidad) en `web/` contra la API real, con las tipografías empaquetadas.
- [ ] PTZ en vivo, recorte, vista previa al pasar el cursor (necesita miniaturas) y sucesos en la ronda.
- [ ] Recortes, Cámaras, Continuidad, Ajustes y Bienvenida (con el permiso de Red local de macOS).
- [ ] Calendario para ir más allá de la última semana; pausar la guardia; borrar un día o un tramo.
- [ ] Todos los estados de la guardia, no sólo el feliz.
- [ ] `node --test` de lo puro (tiempo, ronda) y recorrido en Chrome y WebKit a 375, 768 y 1280 px.

### 4 · Escritorio

- [ ] Tauri 2 para macOS (aarch64) y Windows (x64): ventana con candado de navegación, página de respaldo, bandeja, avisos, inicio con la sesión.
- [ ] El instalador copia motor, MediaMTX y FFmpeg a `<datos>/engine/<versión>/` y el LaunchAgent o la tarea apuntan ahí: actualizar es detener, cambiar el puntero y arrancar. En Windows es tarea de inicio de sesión, no servicio (necesita el Credential Manager del usuario).
- [ ] Firma: ad-hoc mientras cada quien compile; Developer ID, notarización y firma de los binarios anidados antes de publicar ejecutables.
- [ ] `THIRD_PARTY_NOTICES.md`: MIT de MediaMTX y LGPL de FFmpeg (binario sin modificar, enlace a su fuente).
- [ ] CI: `dotnet test`, `cargo fmt/clippy/test`, ESLint y Prettier.

### 5 · Sucesos y avisos

- [ ] Vista de Sucesos con los eventos de la cámara; avisos del sistema.
- [ ] Opcional: aviso al celular por ntfy (sale de la red: sólo texto, sin imagen; se puede apuntar a un servidor propio).

### 6 · Más allá

- [ ] Motor en un equipo siempre prendido y esta app como visor. Acceso por Tailscale. Mosaico de cámaras. Detección propia si los eventos de la cámara no bastan.

## Fuera de la primera versión

Relevo en paralelo, detección de personas con CPU, selector de calidad en vivo, velocidad 60×, respaldo HLS.

## Riesgos

- **La Tapo pierde WiFi o se reinicia:** es la causa más común de huecos y el motor no la controla; se mide y se muestra.
- **Laptop con la tapa cerrada o en batería:** se duerme. Ver «Límites honestos».
- **Permiso de Red local en macOS:** sin él el motor no ve la cámara; va en la bienvenida.
- **WKWebView ≠ WebView2:** todo se prueba en los dos motores.
- **Firmware de TP-Link:** una actualización puede romper ONVIF; el PTZ y los eventos son opcionales, la grabación sólo depende de RTSP.
- **Llavero y firma ad-hoc:** macOS puede volver a pedir permiso tras cada actualización hasta que haya Developer ID.
