# Estado · Velador

Última actualización: 2026-11-12

## Dónde vamos

- **Fase 0 · Propuesta: aprobada** (2026-11-12).
- **Fase 1 · Verificación: hecha**, salvo lo que necesita la ventana de Tauri y una corrida larga.
- **Fase 2 · Motor: en curso.** Graba, vigila y sirve la ronda y el video; faltan retención, alta de cámaras, PTZ, sucesos y recortes.

## Hecho

- Análisis de Bodega (motor + ventana Tauri), Labia (núcleo puro con pruebas, una app por plataforma) y Chispa (propuesta → plan → estado) para definir estructura y reglas.
- Investigación de lo que existe (Frigate, MediaMTX, go2rtc, Moonfire, Viseron, Blue Iris) y de la Tapo C500; resumen y fuentes en `docs/research.md`.
- Decisiones del usuario (2026-11-12): se llama Velador, graba en la misma máquina que se usa, Tauri 2 + motor .NET, front sin React, repo público MIT.
- `CLAUDE.md`, `plan.md` y prototipo navegable en `propuesta/`, probado en Chrome en oscuro y claro.
- Verificación con la cámara real (`docs/verification.md`): streams, códecs, peso, CPU, sesiones simultáneas y ONVIF. Hallazgos: `stream2` es 720p, el MP4 no admite G.711, la cámara aceptó sólo 2 de 4 sesiones a `stream1` y sí acepta la suscripción a eventos.
- MediaMTX v1.21.3 probado contra la cámara: 0.6 % de CPU y 56 MB con los dos streams, video por rango en 40 ms, 0.77 s perdidos al matarlo, vivo por WebRTC en Chrome. PTZ y evento de movimiento por ONVIF funcionando.
- Tres revisiones independientes (diseño, arquitectura, continuidad) aplicadas al plan y al prototipo: motor como único punto de entrada, sesión por cookie, vigía sobre MediaMTX, retención sólo en el motor, tres proyectos .NET en vez de cinco, relevo descartado a favor de motor en equipo siempre prendido.

## Siguiente

1. `feature/engine-cameras`: alta y edición con llavero, descubrimiento ONVIF, PTZ y eventos de la cámara.
2. Recortes y miniaturas con FFmpeg.
4. Pendientes de la Fase 1 y la matriz de continuidad (lista en `plan.md`).

## Motor (2026-11-12)

- `Velador.Core` (puro), `Velador.Engine` y `Velador.Tests` en .NET 10; 64 pruebas en verde. CI en macOS y Windows; el primer CI cazó dos fallos sólo de Windows (zonas horarias IANA y saltos de línea), ya corregidos.
- Retención por días y por porcentaje de disco con piso de 2 GB libres; sin probar todavía con un disco lleno de verdad.
- Probado contra la cámara real: arranca MediaMTX con su configuración, graba los dos streams, `summary` y la ronda del día responden, 10 s de video por la API en 16 ms, el vivo se reenvía por WHEP.
- Cortes provocados: parada limpia y rearranque, `kill -9` a MediaMTX (se levanta solo en menos de 1 s), `kill -9` al motor con MediaMTX vivo (no deja hueco: lo readopta) y MediaMTX congelado (hueco `engine`, reinicio solo a los 15 s).
- Error encontrado y corregido en esa prueba: sin tiempo límite, un MediaMTX congelado dejaba colgado al motor.
- Otro, visto al usar la web: el motor tardaba más de un minuto en apagarse si había un video a medio enviar, y podía relanzar MediaMTX mientras se apagaba. Ahora se detiene en 5 s como máximo.
- Sin probar: todo lo de Windows (Credential Manager y petición de energía están escritos, no ejecutados), el llavero de macOS con una contraseña real, reposo real del equipo y corte del WiFi de la cámara.

## Web (2026-11-12)

- `web/` servida por el motor: En vivo (WebRTC reenviado por el motor) y Ronda (reproductor sobre las grabaciones, por rango). Probado en Chrome contra la cámara real: imagen en vivo 1080p, reproducción, salto atrás, 2× y cobertura del día.
- Faltan en `web/`: Sucesos, Recortes, Cámaras, Continuidad, Ajustes y Bienvenida (dependen de API que aún no existe), y probar en WebKit.

## Repositorio

`github.com/ReyDavidAG/velador`, público, con `main`, `develop` y ramas `feature/*`. Commits de una línea con el correo de Bitácora.

