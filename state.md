# Estado · Velador

Última actualización: 2026-11-12

## Dónde vamos

- **Fase 0 · Propuesta: aprobada** (2026-11-12).
- **Fase 1 · Verificación: hecha**, salvo lo que necesita la ventana de Tauri y una corrida larga.
- **Fase 2 · Motor: sin empezar.** Requiere el SDK de .NET 10 (en esta Mac hay 8).

## Hecho

- Análisis de Bodega (motor + ventana Tauri), Labia (núcleo puro con pruebas, una app por plataforma) y Chispa (propuesta → plan → estado) para definir estructura y reglas.
- Investigación de lo que existe (Frigate, MediaMTX, go2rtc, Moonfire, Viseron, Blue Iris) y de la Tapo C500; resumen y fuentes en `docs/research.md`.
- Decisiones del usuario (2026-11-12): se llama Velador, graba en la misma máquina que se usa, Tauri 2 + motor .NET, front sin React, repo público MIT.
- `CLAUDE.md`, `plan.md` y prototipo navegable en `propuesta/`, probado en Chrome en oscuro y claro.
- Verificación con la cámara real (`docs/verification.md`): streams, códecs, peso, CPU, sesiones simultáneas y ONVIF. Hallazgos: `stream2` es 720p, el MP4 no admite G.711, la cámara aceptó sólo 2 de 4 sesiones a `stream1` y sí acepta la suscripción a eventos.
- MediaMTX v1.21.3 probado contra la cámara: 0.6 % de CPU y 56 MB con los dos streams, video por rango en 40 ms, 0.77 s perdidos al matarlo, vivo por WebRTC en Chrome. PTZ y evento de movimiento por ONVIF funcionando.
- Tres revisiones independientes (diseño, arquitectura, continuidad) aplicadas al plan y al prototipo: motor como único punto de entrada, sesión por cookie, vigía sobre MediaMTX, retención sólo en el motor, tres proyectos .NET en vez de cinco, relevo descartado a favor de motor en equipo siempre prendido.

## Siguiente

1. Instalar el SDK de .NET 10 y abrir `feature/engine-record`: solución, `Velador.Core`, `Velador.Engine` y supervisor de MediaMTX.
2. Pendientes de la Fase 1 (lista en `plan.md`).

## Repositorio

`github.com/ReyDavidAG/velador`, público, con `main`, `develop` y ramas `feature/*`. Commits de una línea con el correo de Bitácora.

