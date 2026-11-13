# Investigación · 2026-11-12

Qué existe y qué se toma de cada cosa. «Sin confirmar» = sólo foros o sin fuente primaria; se prueba en la Fase 1.

## Tapo C500

- ONVIF Profile S en el puerto 2020 y RTSP en 554; sin microSD no hay conflicto con Tapo Care. https://www.tp-link.com/us/support/faq/2680/
- El PTZ por ONVIF se rompió en el firmware 1.3.1 y se corrigió en 1.3.2. https://community.tp-link.com/en/smart-home/forum/topic/744616
- Eventos PullPoint: un C500 con 1.3.5 responde que no los soporta (foro). https://community.tp-link.com/en/home/forum/topic/839478
- Audio de dos vías: no por ONVIF; sí por la fuente `tapo://` de go2rtc. https://github.com/AlexxIT/go2rtc/blob/master/internal/tapo/README.md
- Conexiones RTSP simultáneas (2 a 4 según el hilo) y efecto del modo privacidad: sin confirmar.

## Servidor de medios

- MediaMTX v1.21.3 y go2rtc v1.9.15, los dos MIT y con binarios para macOS y Windows.
- MediaMTX graba fMP4 por partes (`recordPartDuration`), rota segmentos, borra por antigüedad y sirve reproducción por rango (`/list`, `/get`). https://mediamtx.org/docs/usage/record · https://mediamtx.org/docs/usage/playback
- WebRTC acepta H.264 + G.711 directo en los dos. https://mediamtx.org/docs/read/webrtc
- go2rtc no documenta grabador; su ventaja es `tapo://` (audio de dos vías).
- FFmpeg: LGPL si el build no lleva `--enable-gpl`; copiar streams no lo necesita. https://ffmpeg.org/legal.html
- El fMP4 sigue siendo legible si la escritura se interrumpe; el MP4 normal no. https://ffmpeg.org/ffmpeg-formats.html

## Cómo lo resuelven otros

- Frigate: segmentos MP4 de 10 s sin recodificar, índice en base de datos, borra lo más viejo si falta espacio; vivo por MSE con WebRTC de respaldo; revisión con previews al pasar el cursor; exportar arrastrando asas. https://docs.frigate.video/configuration/record/ · https://docs.frigate.video/configuration/review/
- Moonfire NVR: índice por muestra en SQLite, MP4 generado al vuelo. https://github.com/scottlamb/moonfire-nvr/blob/master/design/schema.md
- Viseron: segmentos fMP4 servidos por HLS. https://viseron.netlify.app/docs/documentation/configuration/recordings
- Blue Iris: lo que más se critica es el arrastre impreciso y los saltos por hora (foro). https://ipcamtalk.com/threads/timeline-in-ui3.54277/latest

Qué se toma: una sola conexión a la cámara, video sin recodificar, índice aparte de los archivos, línea de tiempo con zoom centrado en el cursor, recorte con asas, y huecos visibles en vez de escondidos.

## Detección

- Frigate decodifica sólo el stream de detección (5 fps, 640×360) y busca objetos sólo donde hubo movimiento; no recomienda detector en CPU para uso general. https://docs.frigate.video/frigate/camera_setup/ · https://docs.frigate.video/configuration/object_detectors/

## Remoto y avisos

- Tailscale atraviesa NAT sin abrir puertos. https://tailscale.com/compare/wireguard
- ntfy publica con un POST sin cuenta; el tema hace de contraseña. https://docs.ntfy.sh/publish/
