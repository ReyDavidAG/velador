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
- [x] macOS: el vivo por WebRTC conecta dentro de la ventana, y WebKit reproduce la grabación con su audio PCM siempre que el video se sirva con rangos (por eso el motor lo pasa por una caché en disco). Decisión 6 cerrada para macOS.
- [ ] Lo mismo en WebView2 (Windows).
- [ ] Corrida de 2 h o más y corte del WiFi de la cámara: en cuánto vuelve MediaMTX por su cuenta.
- [ ] En la app Tapo: «Auto Reboot» y modo privacidad.

### 2 · Motor

Ramas `feature/engine-record`, `feature/engine-continuity`, `feature/engine-api`, `feature/engine-retention`.

- [x] Supervisor de MediaMTX: escribe su configuración (sólo `127.0.0.1`, MoQ y salidas apagadas, `%z` en el nombre de archivo, rutas sin guion bajo), lo readopta si ya corre, lo reinicia si muere.
- [x] Continuidad: vigía, latido, clasificación de cortes y no dormir, con pruebas. Falta el aviso del sistema, la tapa y el cierre de sesión de la matriz, y detectar contraseña rechazada (hoy se ve como «cámara»).
- [x] Huecos y estado en SQLite. Los tramos salen de la lista de MediaMTX, que ya funde segmentos contiguos: no hizo falta tabla de segmentos ni avisos de segmento.
- [x] API con sesión: `summary`, `rounds/{date}`, `media`, `live`.
- [x] API: `ptz` (posición y giro relativo) y sucesos dentro de la ronda.
- [x] API: `cameras` (alta, edición, baja, prueba y descubrimiento), `clips`, `settings` y almacenamiento. Contrato en `docs/api.md`.
- [x] Descubrimiento ONVIF, alta con IP a mano y prueba de cuenta.
- [ ] Identificar la cámara por MAC para sobrevivir a un cambio de IP.
- [x] Miniaturas: se piden por rango a `stream2` y las pinta el navegador; no hizo falta FFmpeg.
- [x] Retención por días y por disco, con piso de espacio libre; nunca borra el archivo en curso ni lo que cubre un recorte.
- [ ] Modo demo (`VELADOR_DEMO=1`) con datos ficticios.
- **Listo cuando** graba 24 h con cobertura de 100 % y pasa la matriz de continuidad: reposo y despertar, tapa cerrada, cierre de sesión, `kill -STOP` a MediaMTX, reinicio de la cámara y del router, disco lleno y cambio de hora; cada caso deja su hueco con la causa correcta.

### 3 · Web

- [x] En vivo por WebRTC y Ronda (video por rango, arrastre, zoom, saltar huecos, velocidad) en `web/` contra la API real, con las tipografías empaquetadas.
- [x] PTZ en En vivo (botones y flechas del teclado) y sucesos marcados y listados en la Ronda.
- [x] Recorte con asas y vista previa al pasar el cursor (sale de `stream2` por rango, sin FFmpeg).
- [x] Sucesos, Recortes, Cámaras, Continuidad, Ajustes y Bienvenida.
- [ ] Calendario para ir más allá de la última semana; pausar la guardia; borrar un día o un tramo.
- [ ] Todos los estados de la guardia, no sólo el feliz.
- [ ] `node --test` de lo puro (tiempo, ronda) y recorrido en Chrome y WebKit a 375, 768 y 1280 px.

### 4 · Escritorio

- [x] Tauri 2 para macOS (aarch64): ventana con candado de navegación, página de respaldo, bandeja con el estado, avisos de hueco y de regreso, inicio con la sesión. Instalador `scripts/install_macos.sh`.
- [x] Lo mismo para Windows (x64): tarea de inicio de sesión, instalador y prueba en un equipo real (2026-11-13).
- [ ] El instalador copia motor, MediaMTX y FFmpeg a `<datos>/engine/<versión>/` y el LaunchAgent o la tarea apuntan ahí: actualizar es detener, cambiar el puntero y arrancar. En Windows es tarea de inicio de sesión, no servicio (necesita el Credential Manager del usuario).
- [ ] Firma: ad-hoc mientras cada quien compile; Developer ID, notarización y firma de los binarios anidados antes de publicar ejecutables.
- [x] `THIRD_PARTY_NOTICES.md`: MIT de MediaMTX, LGPL de FFmpeg (binario sin modificar, enlace a su fuente), Apache 2.0 de YOLOX y ONNX Runtime.
- [ ] CI: `dotnet test`, `cargo fmt/clippy/test`, ESLint y Prettier.

### 5 · Sucesos y avisos

- [x] El motor se suscribe a los eventos de la cámara por ONVIF y los guarda. Sin comprobar con un evento real: la cámara no emitió ninguno durante la prueba.
- [x] Vista de Sucesos y aviso del sistema por persona. Los eventos de movimiento ya llegaron de la cámara real; el de persona no se ha visto todavía.
- [ ] Opcional: aviso al celular por ntfy (sale de la red: sólo texto, sin imagen; se puede apuntar a un servidor propio).

### 6 · Más allá

- [ ] Motor en un equipo siempre prendido y esta app como visor. Acceso por Tailscale. Mosaico de cámaras. Detección propia si los eventos de la cámara no bastan.

### 7 · Guardia a mano, espacio y seguimiento propio

Aprobada por el usuario el 2026-11-13. Rama `feature/guard-plus`.

**Pausar la grabación.**

- [x] `POST /api/cameras/{id}/recording` con `{paused, minutes?}`. El motor apaga `record` de esa ruta por la API de MediaMTX (probado: el vivo no se corta, el archivo se cierra y al reanudar empieza otro). Se guarda en `config.json` para que sobreviva a un reinicio, y la configuración de MediaMTX la respeta al arrancar.
- [x] Hueco con causa `paused` abierto al pausar y cerrado al reanudar; el vigía no lo cierra ni abre otro mientras dure.
- [x] «Reanudar en 15 min, 1 h o a mano». Botón en En vivo (también en pantalla completa) y en la bandeja.

**Espacio y borrado.** Ya existe «Guardar hasta N días» y «Sin pasar de N %». Se agrega:

- [x] Cuánto se graba por día (medido de las últimas 24 h), cuántos días caben con los límites actuales, desde cuándo hay grabación y qué día se borra después.

**Detección propia de personas.**

- [x] FFmpeg (LGPL, compilación de BtbN fijada con suma) decodifica `stream2` desde MediaMTX a 10 cuadros por segundo; nunca toca el 1080p ni la grabación.
- [x] YOLOX-tiny (Apache 2.0) en ONNX Runtime (MIT), en CPU. Sólo la clase persona.
- [x] Sucesos `person` propios junto a los de la cámara; el aviso del sistema ya existente los toma.
- [x] Se puede apagar en Ajustes; sin FFmpeg o sin modelo, se dice y no se detecta.

**Seguimiento con predicción.**

- [x] Rastreador en `Velador.Core` (puro, con pruebas): filtro de Kalman de velocidad constante y asociación por IoU en dos pasadas (estilo ByteTrack). Estima posición y velocidad de cada persona.
- [x] Control de la cámara: `ContinuousMove` de ONVIF con `Timeout` corto (si el motor muere, la cámara se detiene sola) y `Stop`. Apunta adonde estará la persona tras el retraso medido del video, con zona muerta al centro y velocidad limitada. Si la cámara no acepta movimiento continuo, pasos relativos.
- [x] Interruptor «Seguir personas» en En vivo y pantalla completa; al mover a mano se suspende 10 s. Hay que apagar el seguimiento de la app Tapo para que no compitan.
- [x] Recuadros de las personas sobre el vivo.

**Fotos de personas.**

- [x] De cada persona seguida se guarda la mejor foto (cuerpo completo, la más grande y nítida) en PNG, junto a su hora.
- [x] Se agrupan por parecido de apariencia (color de ropa por zonas del cuerpo). Sirve dentro del mismo día; no es reconocimiento facial y no promete identificar a nadie.
- [x] Vista «Personas»: cada grupo con sus fotos y las horas en que sale; un clic lleva a ese momento en la Ronda. Se borran con la misma retención que el video.

**Lo que se aprendió al construirla.** El video para la detección llega con ~1.5 s de retraso; seguir la posición en la imagen hacía oscilar la cámara. Por eso el control estima la posición de la persona en el mundo y usa un predictor de Smith. El seguimiento propio de la cámara probablemente sufre lo mismo con menos retraso.

**Agregado el mismo día, a pedido del usuario:** caras con YuNet (sólo detección), búsqueda hacia el lado por donde salió la persona y regreso a la posición de inicio, zoom digital en vivo y en la Ronda, y selector de calidad del vivo con la resolución, cuadros por segundo y Mb/s reales.

**Límites honestos.** Si alguien cruza más rápido de lo que gira el motor de la cámara, ningún programa lo alcanza; la predicción reduce cuánto se le pierde. Detectar en CPU cuesta unos puntos de un núcleo; se mide y se dice.

### Linux · vivo sin WebRTC

WebKitGTK de Arch/CachyOS viene sin WebRTC (`RTCPeerConnection` no existe aunque se active). Cuando falta, el vivo va por MP4 fragmentado a MSE:

- El motor abre un FFmpeg por cada vivo abierto, que copia el video (sin recodificar) del RTSP local de MediaMTX a MP4 con un fragmento por cuadro, y lo entrega en `GET /api/cameras/{id}/live.mp4?stream=main|minor` con la misma sesión. Al cerrar el vivo el FFmpeg se termina. El front sigue sin hablar con MediaMTX.
- `live.js` mete esos fragmentos a `MediaSource` y se mantiene pegado al final: si se atrasa más de 0.8 s, salta. Con la ventana oculta más de un minuto, corta.
- Se descartó el HLS de baja latencia de MediaMTX: con esta cámara los pedazos salían de 0.3 a 1 s y el retraso pasaba de 2 s.
- Medido en esta PC en un navegador sin WebRTC contra el stream real: 1080p a 15 cps, entre 0.4 y 0.5 s detrás de lo recibido, sin errores. Falta medirlo en la ventana de Linux.
- [ ] Sonido en ese vivo: el G.711 de la cámara no cabe en MP4 y pasarlo a AAC con FFmpeg falló al arrancar en las pruebas. macOS y Windows sí llevan sonido por WebRTC.

### 8 · Zona de vigilancia, seguimiento que sí sigue y caras más nítidas

Aprobada por el usuario el 2026-11-15, con la cámara ya montada en su lugar. Rama `feature/zone-faces`.

**Por qué no seguía.** La calibración era de otro montaje y sólo se medía la primera vez; suponía que la velocidad es proporcional a la orden y un solo sentido por eje. Medido en la C500: a media orden gira un tercio de lo que gira a orden completa, y la inclinación responde al revés en órdenes continuas que en pasos.

- [x] Calibración por posición: imagen por unidad de posición (dos fotos quietas), velocidad a tres niveles de orden y sentido de cada eje para pasos y para órdenes continuas. Las calibraciones viejas se descartan y se repite al encender el seguimiento.
- [x] El control pide una velocidad y el motor la convierte en orden con la curva medida.

**Zona de vigilancia.**

- [x] Límites de giro e inclinación y posición de reposo por cámara en `config.json`. El seguimiento y la búsqueda no salen; al perder a la persona regresa al reposo. El control a mano no se bloquea: la interfaz dice si quedó fuera.
- [x] «Medir cobertura»: el motor recorre el alcance, guarda una foto por dirección y regresa. Las fotos son privadas.
- [x] Editor en Cámaras: vivo, control de movimiento y mapa hecho con esas fotos. Los límites se arrastran o se fijan con «aquí»; tocar el mapa lleva la cámara. En vivo muestra el mismo mapa y «Ir al reposo».
- [ ] Proponer los límites solos a partir de las fotos (detectar muro y cielo).

**Caras.**

- [x] El mejor cuadro se elige por nitidez y por qué tan de frente está (los cinco puntos de YuNet), además del tamaño.
- [x] La cara se recorta de la grabación principal (1080p): al cerrar un avistamiento con cara, FFmpeg decodifica 1.7 s alrededor de ese instante, sólo el recuadro de la cabeza. Es la excepción puntual a «lo que decodifica usa `stream2`».
- [x] Fusión de varios cuadros de la misma cara, alineados por sus puntos y con ajuste fino de un octavo de pixel. Quita ruido y bloques de compresión con información real; no inventa. Se guarda aparte y se rotula «combinada».
- [x] Sin modelos que «restauran» caras: generan rasgos que no estaban en la imagen.
- [ ] A decidir por el usuario: agrupar personas por la cara (SFace, local) en vez de sólo por ropa. Eso ya es reconocimiento facial.

**Caras, segunda vuelta.** Aprobada por el usuario el 2026-11-15 tras su caminata de las 12:25. Lo medido en esa grabación: el 1080p iba clavado en su tope de 1228 kbps y la compresión borraba la cara aunque midiera 70 px; la app guardó un cuadro de perfil y mirando abajo cuando un segundo después había cuadros de frente; de día no hay barrido por obturador.

- [x] Subir el 1080p a 2048 kbps (el máximo que admite la cámara). Son unos 9 GB más por día.
- [x] Elegir la cara entre los tres mejores momentos del avistamiento, no uno solo, con una sola fórmula que castiga el perfil y la cabeza agachada y deja de premiar tanto el tamaño.
- [x] Repetir la caminata y comparar contra la de las 12:25: caras de 84 y 63 px con menos bloques; mejora modesta, siguen los ojos en sombra y la vista desde arriba.
- [x] Seguimiento: el giro se atascaba al alternar ejes cada 0.3 s; ahora los turnos duran de 0.8 a 1.6 s y el giro va primero. Falta probarlo caminando.
- [ ] Ver por qué la caminata de las 12:58 no produjo «combinada».
- [ ] Según lo que salga: probar H.265 a 2048 kbps (hay que confirmar el vivo y la Ronda en WebView2 y WebKit).
- [ ] Súper-resolución de video con varios cuadros en la GPU, con modelo de licencia compatible con MIT. Usa cuadros reales; se espera 1.5–2× de detalle útil.
- [x] Decidido por el usuario el 2026-11-15: sin restaurador de caras. Vio una demostración con GFPGAN sobre su propia cara y no lo convenció. Las caras se quedan como están.
- [ ] La elección de cara no detecta una mano tapando los ojos (visto en la caminata de las 13:07).

Sin servicios en la nube para mejorar caras (se descartó Gemini): mandaría caras de la calle fuera del equipo.

**Imagen de la cámara.** Probado de día por el protocolo de Tapo: sólo la nitidez cambia la imagen; el rango dinámico amplio, el contraste, el brillo y la corrección de lente se guardan pero no la alteran en este modelo. La nitidez queda en Cámaras. El video principal sí se deja cambiar, con un `set` directo en vez de los métodos con nombre.

**Interfaz.**

- [x] Sin desbordes de 375 a 1280 px, medido en todas las vistas.

**Límites honestos.** Con el lente de la C500 una cara mide unos 40 px de ancho a 5 m, 20 px a 10 m y 13 px a 15 m en el 1080p; por debajo de ~32 px identificar con certeza no es realista. La cámara no tiene zoom óptico. El seguimiento sigue sin probarse con una persona caminando.

### 9 · Rediseño: turno de noche

Aprobado por el usuario el 2026-11-15 sobre la propuesta de `propuesta_nocturna/`.

- [x] Tema de caseta de velador con monitores, en noche y día, sin usar nada de un juego existente.
- [x] Sereno, el perro velador, como logo: en la app, el favicon y los íconos de escritorio y bandeja.
- [x] Sereno avisa con los ojos el estado real de la guardia y cuando la detección ve a alguien.
- [x] Marco de monitor en el vivo, medidores de aguja con cobertura y disco, palanca para mover la cámara.
- [x] Transiciones entre vistas, píldora que se desliza en el menú y apagón al cambiar de día a noche.
- [x] Tipografías nuevas servidas localmente.
- [x] Ícono propio de Windows, sin el margen de macOS, para que llene la barra de tareas y la bandeja; puesto en la app instalada.
- [ ] Revisar a ojo en WebKit (la ventana de macOS) y a ancho de teléfono.
- No se trajo de la propuesta: el plano de la casa y la pared de varios monitores (hoy hay una sola cámara), el sonido del monitor y la pantalla de las 6 AM.

### 10 · Detección fresca en Linux

Propuesto el 2026-11-16, **pendiente de aprobación**. Objetivo: que la laptop no se caliente. Medido hoy con nadie a la vista: la detección corre en CPU (núcleos eficientes 12-15) y cuesta ~190 ms por cuadro; el motor gasta ~47 % de un núcleo a 2.5 cps, contra ~5 % en Windows, donde la inferencia va en la GPU. Con alguien a la vista pide 10 cps y en CPU no llega.

En orden: cada paso se mide antes de pasar al siguiente, y si con uno ya se cumple el objetivo, los demás no se hacen.

- [ ] **1. No inferir cuando nada cambia.** Sin nadie a la vista, YOLOX sólo corre si hay cambio en la imagen gris de 320×180 que ya se calcula (diferencia contra el cuadro anterior, fuera de la franja de la hora y del logo), y además una vez cada 3 s por si alguien entró muy despacio. Con alguien a la vista o siguiendo, sin cambios. No agrega dependencias y sirve igual en Windows y macOS. Meta: motor ≤ 10 % de un núcleo en reposo. Riesgo: de noche el ruido del infrarrojo y los insectos frente a la luz cuentan como cambio; el umbral se calibra con la grabación de esta noche.
- [ ] **2. Inferencia en la Intel integrada (UHD de la i7-12650H) con OpenVINO**, sólo si con el paso 1 no se llega a 10 cps con alguien a la vista o el motor pasa de ~25 % mientras sigue. El paquete de ONNX Runtime con OpenVINO sólo trae binarios de Windows; en Linux se usaría el runtime de OpenVINO que publica Intel, bajado con suma verificada por `scripts/fetch_binaries.sh` igual que MediaMTX, y llamado desde una implementación de `PersonDetector` sobre la API C de OpenVINO. Requiere el paquete `intel-compute-runtime` del sistema; si falta o la GPU no arranca, sigue en CPU como hoy. Se mide el consumo de la Intel con `intel_gpu_top`, porque también decodifica el vivo.
- [ ] **3. Modelo cuantizado (int8) en CPU**, como alternativa al paso 2 si OpenVINO resulta frágil: se mide primero que no pierda personas de noche.
- **NVIDIA (RTX 4060): no se usa para la detección.** Despertarla para inferir 3–10 cuadros por segundo la deja encendida todo el tiempo (varios watts constantes contra ~2 W dormida), es lo que más calienta y CUDA pesa más de 1 GB. Se queda dormida a propósito, igual que para el vivo.
- Medición de cada paso: CPU de motor, FFmpeg y MediaMTX, temperatura del paquete, cuadros por segundo con alguien a la vista, y que la NVIDIA siga en reposo.

## Disco externo

Hecho en 0.1.0. Se elige en Ajustes entre la Mac, un disco conectado o una ruta.

- Si el disco no está (o se desconecta de golpe), el motor pasa a grabar en `~/Movies/Velador` en unos 3 segundos y lo dice en la interfaz.
- Cuando vuelve, regresa al disco y se lleva lo que quedó en la Mac. Los recortes se quedan donde se guardaron.
- «Preparar para expulsar» suelta el disco 10 minutos para poder desconectarlo sin forzar.
- La retención cuenta el disco donde se está grabando.
- Mientras el disco está fuera, lo grabado en él no aparece en la ronda.

## Fuera de la primera versión

Relevo en paralelo, velocidad 60×, respaldo HLS.

## Riesgos

- **La Tapo pierde WiFi o se reinicia:** es la causa más común de huecos y el motor no la controla; se mide y se muestra.
- **Laptop con la tapa cerrada o en batería:** se duerme. Ver «Límites honestos».
- **Permiso de Red local en macOS:** sin él el motor no ve la cámara; va en la bienvenida.
- **WKWebView ≠ WebView2:** todo se prueba en los dos motores.
- **Firmware de TP-Link:** una actualización puede romper ONVIF; el PTZ y los eventos son opcionales, la grabación sólo depende de RTSP.
- **Llavero y firma ad-hoc:** macOS puede volver a pedir permiso tras cada actualización hasta que haya Developer ID.
