# Estado · Velador

Última actualización: 2026-11-13

## Dónde vamos

- **0.1.0 para macOS: terminada e instalada en esta Mac.** Motor, interfaz completa y app de escritorio.
- **Windows: instalado y grabando en la PC con la cámara (2026-11-13).** Compila ahí (96 pruebas del motor, 15 de la app). Corregidas las consolas que se abrían solas y la reinstalación encima. Falta recorrer Ronda con sonido, recortes, disco externo y controles Tapo en WebView2. Detalle en `docs/windows.md`.
- **Mover la cámara manteniendo presionado** (botón o flecha): avanza paso a paso sin soltar. En pantalla completa hay control de movimiento sobre el video, doble clic entra y sale, y los controles y el cursor se esconden tras 3 s quietos. Probado en Edge (mismo motor que WebView2) con la cámara real.
- **Fase 7 (rama `feature/guard-plus`), hecha el 2026-11-13 en la PC:**
  - **Pausar la grabación** desde En vivo (también en pantalla completa) y la bandeja: 15 min, 1 h, 4 h o hasta reanudar. Se apaga `record` por la API de MediaMTX sin cortar el vivo; queda un hueco `paused` y sobrevive a reinicios. Probado con la cámara: 60 s exactos y se reanudó sola.
  - **Espacio:** Ajustes dice cuánto pesa un día (medido: unos 15–16 GB con esta cámara), cuántos días caben, desde cuándo hay grabación y cuándo empieza a borrar. Con el disco de la PC caben unos 10–11 días de los 14 pedidos.
  - **Detección propia de personas:** FFmpeg (LGPL) lee `stream2` de MediaMTX por RTSP en `127.0.0.1` y YOLOX-tiny corre en ONNX Runtime; en Windows en la GPU con DirectML (1.6 ms de CPU por cuadro contra 198 ms en CPU). 3 cuadros por segundo sin nadie, 10 con alguien a la vista. Medido: motor 11 % y FFmpeg 16 % de un núcleo detectando a 10 cps. Genera sucesos `person` propios.
  - **Seguimiento con predicción:** rastreador Kalman + asociación tipo ByteTrack; el control estima dónde camina la persona en el mundo (imagen + giro conocido de la cámara) y apunta adonde estará tras el retraso (predictor de Smith); si se sale de la imagen sigue su trayectoria 2.5 s y la retoma si reaparece cerca. Calibración automática medida con la cámara: gira 0.86 anchos de imagen por segundo, el video llega con 1.56 s de retraso, el pan estaba invertido para las flechas (montaje de cabeza) y ahora se corrige solo. En simulación con esos datos: a paso lento 100 % a la vista (sin predicción 44 %), a paso normal 85 % (51 %), rápido 53 % (34 %). **Sin probar con una persona caminando y el seguimiento encendido.**
  - **Personas:** foto de la mejor toma de cada persona y agrupación por el promedio de apariencia (color de ropa). Con las fotos reales de hoy agrupó bien a las dos personas. No es reconocimiento facial.
  - **Corregido:** el motor servía la interfaz sin `Cache-Control` y WebView2 mostraba versiones viejas; el instalador ahora detiene el motor (y espera a FFmpeg) antes de reemplazarlo.
- **Hasta 3 redes por cámara: hecho, sin probar el cambio físico de red.** Cada red donde Velador ve la cámara queda guardada (se reconoce por el router). Al cambiar de red usa la dirección recordada, y si no hay, la busca por número de serie. Se ven y se olvidan en Cámaras. En macOS el nombre sale como «Red 192.168.1.1» porque el sistema no entrega el nombre del WiFi a un servicio.
- **Luz blanca, modo de noche, voltear la imagen y WiFi de la cámara: hecho, por el protocolo privado de Tapo.** El motor lo habla por sí mismo (SPAKE2+ sobre P-256 y AES-CCM, portado de `pytapo` y verificado con sus vectores; el protocolo anterior queda como respaldo). Probado con la cámara real: leer estado, encender y apagar la luz, y listar redes. **Sin probar:** cambiar el modo de noche, voltear la imagen y, sobre todo, cambiar la cámara de red (no se ejecutó para no dejarla desconectada). Usa la contraseña de la cuenta Tapo; si no se da aparte, prueba con la de la cuenta de cámara.
- **Cámara con IP cambiante: hecho.** El motor la reencuentra por su número de serie (probado apuntándolo a una dirección equivocada).

## Qué falta

1. Pruebas en la PC con la cámara (lista en `docs/windows.md`): seguimiento con alguien caminando, pausa desde la bandeja, Personas tras un día de uso, Ronda con sonido, recortes, disco externo y controles Tapo en WebView2.
2. macOS: compilar y probar la fase 7 (FFmpeg del sistema con `brew install ffmpeg`, detección en CPU).
3. Probar con uso real: reposo del equipo, corte del WiFi de la cámara, arranque tras reiniciar la Mac, avisos del sistema y el evento de persona.
4. Identificar la cámara por MAC, causa propia para contraseña rechazada, y firma con Developer ID antes de repartir ejecutables.
5. Acceso desde el celular por Tailscale y motor en un equipo siempre prendido (fase 6).

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

## Escritorio e instalación en macOS (2026-11-12)

- `desktop/` con Tauri 2: 13 pruebas en Rust, `fmt` y `clippy -D warnings` limpios. `Velador.app` pesa 3.6 MB, firma ad-hoc.
- Instalado en esta Mac: motor autocontenido en `~/Library/Application Support/Velador/engine/0.1.0`, LaunchAgent `com.reydavidag.velador.engine`, cámara «Entrada» dada de alta con la contraseña en el llavero, grabando en `~/Movies/Velador`.
- Comprobado: el servicio lee la contraseña del llavero y graba; la ventana abre con sesión y su vivo conecta (sesión WebRTC establecida vista en MediaMTX); En vivo y Ronda reproducen en WebKit (prueba automatizada con el WebKit de Playwright).
- Hallazgo: WebKit exige rangos para `<video>`; el motor ahora baja cada tramo a una caché en disco (16 archivos como máximo) y lo sirve con rangos. De paso, moverse dentro de un tramo ya no vuelve a pedirlo.
- Sin comprobar a ojo: la ventana real (no hay permiso de captura de pantalla en esta sesión), los avisos del sistema y el arranque tras reiniciar la Mac.
- Con el disco actual (75 % ocupado y límite en 80 %) caben alrededor de día y medio de grabación antes de que la retención empiece a borrar.

## PTZ y sucesos (2026-11-12)

- Cliente ONVIF propio en el motor (WS-Security probado con el vector de ejemplo del estándar). Mover la cámara por la API funciona con la cámara real: giró y regresó a la posición exacta.
- Sucesos: suscripción PullPoint con renovación, guardados en SQLite y entregados en la ronda. El lector de mensajes sigue el formato del estándar (`IsMotion`, `IsPeople`, `IsTamper`), **no** un mensaje capturado de esta cámara: en dos intentos no emitió ninguno. Hay que confirmarlo con movimiento real frente a la cámara.
- 67 pruebas en verde.

## Cierre de la 0.1.0 (2026-11-12)

- Disco externo con respaldo en la Mac, probado con un volumen real de prueba: elegirlo, grabar ahí, soltarlo y expulsarlo sin forzar, reconectarlo (regresa y se lleva lo de la Mac) y desconectarlo de golpe (pasa a la Mac en 3 s). No se probó con el disco físico del usuario, que no estaba conectado.
- Alta, edición, prueba y baja de cámaras desde la app, con búsqueda en la red y giro de imagen de 180°.
- Recortes (desde la Ronda con asas y «últimos 30 s» en vivo), Sucesos, Continuidad, Ajustes y Bienvenida.
- Vista previa al pasar el cursor en la ronda y miniaturas de sucesos, pedidas por rango al stream ligero.
- La cámara emitió eventos reales de movimiento: el lector quedó confirmado. Se unen los que llegan a menos de 10 s para no llenar la lista.
- Aviso del sistema por persona en la app de escritorio.
- El vivo se cura solo: si deja de llegar video 6 s (medido por las estadísticas de la conexión), reconecta. Probado en la ventana real cortando la sesión desde el servidor: volvió en 9 s. Con la ventana oculta más de un minuto, suelta la conexión y la retoma al volver.
- Durante las pruebas hubo dos ocasiones en que el vivo de la ventana se cayó sin recuperarse; no se pudo reproducir después (estuvo estable varios minutos al frente, detrás y oculta). El vigilante anterior cubre ese caso, pero la causa no quedó identificada.
- 78 pruebas del motor y 14 de la app. Todas las pantallas recorridas en WebKit sin errores y sin desbordes.
- La imagen de la cámara sale de cabeza porque así está montada; conviene invertirla en la app de la cámara (así el texto de la hora queda derecho) o con el giro de Velador.

## Repositorio

`github.com/ReyDavidAG/velador`, público, con `main`, `develop` y ramas `feature/*`. Commits de una línea con el correo de Bitácora.

