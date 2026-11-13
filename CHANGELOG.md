# Cambios

## 0.1.0 · 2026-11-12

Primera versión, para macOS (Apple Silicon).

- Motor en .NET que graba con MediaMTX sin recodificar el video, vigila la señal y anota cada hueco con su causa.
- En vivo por WebRTC con control para mover la cámara.
- Ronda de 24 horas con arrastre, acercamiento, vista previa, salto de huecos y recorte.
- Sucesos de movimiento y persona tomados de la propia cámara por ONVIF.
- Recortes que no se borran con lo viejo.
- Grabación en disco externo con respaldo en la Mac cuando el disco no está.
- Retención por días y por porcentaje de disco.
- App de escritorio con Tauri: ventana, estado en la barra de menú y avisos de hueco y de persona.
- Instalador `scripts/install_macos.sh` y motor como servicio de la sesión.
