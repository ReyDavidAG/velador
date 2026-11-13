# Verificación con la cámara real · 2026-11-12

Hecha desde la Mac de desarrollo, en la misma red, con `ffmpeg`/`ffprobe` 8 y ONVIF por HTTP. Sólo lectura: la cámara no se movió ni se le cambió nada. La dirección y la cuenta están en `CLAUDE.local.md`.

## Resultados

| Qué | Resultado |
|---|---|
| Modelo | Tapo C500, hardware 2.0, firmware 1.3.5 Build 260810 |
| `stream1` | H.264 High, 1920×1080, 15 cuadros/s, límite 1228 kbps |
| `stream2` | H.264, **1280×720** (no 360p), 15 cuadros/s, límite 512 kbps |
| Otro perfil | JPEG 640×360, 256 kbps |
| Audio | G.711 A-law, 8 kHz mono, 64 kbps, en los dos streams |
| Cuadros clave | Uno cada ~2.1 s |
| Peso de `stream1` | 1.17 Mbps de noche (infrarrojo, sin movimiento): unos 12.7 GB por día. De día puede subir hasta el límite. |
| CPU al grabar sin recodificar | 0.48 s de CPU en 90 s (0.5 % de un núcleo) convirtiendo sólo el audio a AAC; 0.2 % copiando todo a MKV. 30 MB de RAM. |
| G.711 dentro de MP4 | **No se puede** con FFmpeg: «codec not currently supported in container». Con audio en AAC el fMP4 sale bien. |
| Sesiones RTSP a la vez | De 4 a `stream1`, la cámara aceptó 2. Tres mezcladas (2+1 y 1+2) sí entraron. |
| Marcas de tiempo | Al conectar llegan desordenadas un instante; hay que tolerarlo al arrancar una grabación. |
| ONVIF | Responde en el puerto 2020 con Analytics, Device, Events, Imaging, Media y PTZ. |
| PTZ | Tiene configuración PTZ y responde la posición. Sin posiciones guardadas. No se probó moverla. |
| Eventos | Anuncia `CellMotionDetector`, `PeopleDetector` y `TamperDetector`, y **sí aceptó** crear la suscripción PullPoint (en foros decían que no). En 10 s sin movimiento no llegó ningún mensaje: falta probar con movimiento. |

## MediaMTX v1.21.3 con la cámara

Binario oficial para macOS arm64, con la suma SHA-256 comprobada. Grabó `stream1` y `stream2` a la vez durante 3 minutos, en segmentos de 1 minuto para forzar el cambio de archivo.

| Qué | Resultado |
|---|---|
| CPU y memoria | 0.6 a 0.9 % de un núcleo y 56 MB grabando los dos streams |
| Peso en disco | `stream1` 1.24 Mbps y `stream2` 0.37 Mbps con audio: unos 17 GB por día los dos |
| Audio en el archivo | MediaMTX **no** guarda G.711: lo guarda como PCM sin comprimir (`pcm_s16be`). Chrome lo decodifica dentro de `<video>`. |
| Video por rango | 30 s cruzando un cambio de archivo: respuesta en 40 ms, 30.05 s exactos |
| Tramos | `/list` ya devuelve los segmentos contiguos fundidos en un tramo |
| Saltar dentro de la respuesta | No funcionó en Chrome: cada salto debe ser una petición nueva con otro `start` |
| Vivo por WebRTC | En Chrome: 1920×1080 con pista de video y de audio, avanzando en tiempo real |
| Matar el proceso (`kill -9`) | El archivo en curso quedó legible y completo; se perdieron 0.77 s |
| Contraseña | No aparece en los logs, ni en la lista de rutas, ni en los argumentos del proceso. **Sí** aparece en la API de configuración (`/v3/config/paths/list`) y en el entorno del proceso. |
| Puertos | API, WebRTC y reproducción quedaron en `127.0.0.1`. **MoQ escucha en todas las interfaces (8892 y 8893) por omisión**: hay que apagarlo. |
| Variables de entorno | `MTX_PATHS_<RUTA>_SOURCE` no funciona si el nombre de la ruta lleva guion bajo; y la configuración necesita una fuente válida de relleno. |
| Nombre de archivo | Pide `%z` (zona) en `recordPath` para no pisar archivos en el cambio de horario. |

## PTZ y eventos por ONVIF

| Qué | Resultado |
|---|---|
| Mover | `RelativeMove` de +0.08 en horizontal: la posición pasó de 0.038 a 0.119. Con −0.08 regresó a 0.038 exacto. |
| Eventos | Al moverse la imagen llegó `tns1:RuleEngine/CellMotionDetector/Motion` por PullPoint, una vez por movimiento. Sin movimiento no llega nada. |

## Qué cambió en el plan

- No hace falta FFmpeg para grabar: MediaMTX guarda el audio en PCM y Chrome lo reproduce. FFmpeg queda para recortes (audio a AAC, que abre cualquier reproductor) y miniaturas.
- `stream2` es 720p: sirve para miniaturas y vista remota, y se graba.
- El reproductor pide el video por rango en cada salto, no un archivo largo.
- El motor escribe la configuración de MediaMTX con MoQ apagado, y nunca reenvía la API de configuración.
- Una conexión por stream y nada más; el relevo en paralelo queda descartado.
- Los sucesos salen de la cámara sin gastar CPU, y el PTZ funciona por ONVIF.

## Pendiente

- Lo mismo dentro de la ventana de Tauri: WebRTC y el audio PCM en WKWebView (macOS) y WebView2 (Windows). Si WKWebView no reproduce el PCM, el audio se convierte a AAC al servirlo.
- Una corrida de 2 h o más, y cortar el WiFi de la cámara a propósito para medir en cuánto vuelve.
- En la app Tapo: revisar «Auto Reboot» y qué le pasa al stream con el modo privacidad.
