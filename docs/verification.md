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

## Qué cambió en el plan

- El audio se guarda en AAC; el vivo conserva G.711 (decisión 6).
- `stream2` es 720p: sirve para miniaturas y vista remota, y vale la pena grabarlo.
- Una conexión por stream desde MediaMTX y nada más; el relevo en paralelo se descarta.
- Los sucesos pueden salir de la cámara sin gastar CPU (decisión 10), sujeto a la prueba con movimiento.

## Pendiente

Lo que necesita MediaMTX y la ventana de Tauri: ver la Fase 1 de `plan.md`.
