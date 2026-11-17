# Estado · Velador

Última actualización: 2026-11-16

Última actualización: 2026-11-15

## Dónde vamos

- **Linux (rama `feature/linux`, laptop CachyOS + KDE Wayland, i7-12650H + RTX 4060):** instalado y grabando desde el 2026-11-16. El 2026-11-17 se rehízo el vivo (MP4 por cuadro en lugar de HLS) y se corrigió el reintento con cuenta rechazada; falta verlo en el equipo. Detalle en «Linux» abajo.
- **0.1.0 para macOS: terminada e instalada en esta Mac.** Motor, interfaz completa y app de escritorio.
- **Windows: instalado y grabando en la PC con la cámara (2026-11-13).** Compila ahí (96 pruebas del motor, 15 de la app). Corregidas las consolas que se abrían solas y la reinstalación encima. Falta recorrer Ronda con sonido, recortes, disco externo y controles Tapo en WebView2. Detalle en `docs/windows.md`.
- **Mover la cámara manteniendo presionado** (botón o flecha): avanza paso a paso sin soltar. En pantalla completa hay control de movimiento sobre el video, doble clic entra y sale, y los controles y el cursor se esconden tras 3 s quietos. Probado en Edge (mismo motor que WebView2) con la cámara real.
- **Fase 7 (rama `feature/guard-plus`), hecha el 2026-11-13 en la PC:**
  - **Pausar la grabación** desde En vivo (también en pantalla completa) y la bandeja: 15 min, 1 h, 4 h o hasta reanudar. Se apaga `record` por la API de MediaMTX sin cortar el vivo; queda un hueco `paused` y sobrevive a reinicios. Probado con la cámara: 60 s exactos y se reanudó sola.
  - **Espacio:** Ajustes dice cuánto pesa un día (medido: unos 15–16 GB con esta cámara), cuántos días caben, desde cuándo hay grabación y cuándo empieza a borrar. Con el disco de la PC caben unos 10–11 días de los 14 pedidos.
  - **Detección propia de personas:** FFmpeg (LGPL) lee `stream2` de MediaMTX por RTSP en `127.0.0.1` y YOLOX-tiny corre en ONNX Runtime; en Windows en la GPU con DirectML (1.6 ms de CPU por cuadro contra 198 ms en CPU). 3 cuadros por segundo sin nadie, 10 con alguien a la vista. Medido: motor 11 % y FFmpeg 16 % de un núcleo detectando a 10 cps. Genera sucesos `person` propios.
  - **Seguimiento con predicción:** rastreador Kalman + asociación tipo ByteTrack; el control estima dónde camina la persona en el mundo (imagen + giro conocido de la cámara) y apunta adonde estará tras el retraso (predictor de Smith); si se sale de la imagen sigue su trayectoria 2.5 s y la retoma si reaparece cerca. Calibración automática medida con la cámara: gira 0.86 anchos de imagen por segundo, el video llega con 1.56 s de retraso, el pan estaba invertido para las flechas (montaje de cabeza) y ahora se corrige solo. En simulación con esos datos: a paso lento 100 % a la vista (sin predicción 44 %), a paso normal 85 % (51 %), rápido 53 % (34 %). **Sin probar con una persona caminando y el seguimiento encendido.**
  - **Personas:** foto de la mejor toma de cada persona y agrupación por el promedio de apariencia (color de ropa). Con las fotos reales de hoy agrupó bien a las dos personas. No es reconocimiento facial.
  - **Caras (2026-11-13):** YuNet (MIT) busca la cara en la parte de arriba de cada persona cada tres cuadros y guarda la mejor junto a la foto del cuerpo; en Personas la portada muestra la cara. Con las fotos de hoy encontró cara en 9 de 12. Sólo detecta: la agrupación sigue siendo por ropa. Los cuadros volvieron a 1280×720 (nativo de `stream2`); medido: motor 12 % y FFmpeg 17 % de un núcleo detectando.
  - **Seguimiento al regresar (2026-11-13):** el usuario vio que, al caminar a un lado y regresar, la cámara se quedaba del primer lado. Medido: el motor de la cámara empieza a girar en 130–460 ms, pero el video llega 1.5–1.9 s tarde (la propia cámara; no es FFmpeg). Con ese retraso nada puede seguir una vuelta rápida en el momento; ahora, al perder a la persona, gira 4 s hacia el lado por donde salió y, si no la encuentra en 8 s, regresa a la posición donde se activó el seguimiento (mover a mano cambia esa posición). En simulación, regresar a paso normal pasó de 59 % a 81 % a la vista. **Sin probar caminando.**
  - **Zoom (2026-11-13):** zoom digital de hasta 6× en vivo y en la Ronda: rueda del ratón (acerca hacia el cursor), pellizco, arrastrar para moverse, botones − 1× + y teclas `+` `-` `0`; también en pantalla completa, y los recuadros de personas siguen al zoom. La C500 no tiene zoom óptico; en vivo el zoom es sobre el 1080p. Probado en Edge con la cámara: 2.4× y arrastre en vivo, 2× en la Ronda.
  - **Calidad del vivo:** indicador con lo que llega de verdad (resolución, cuadros por segundo y Mb/s, de las estadísticas de WebRTC) y menú «Alta · 1080p» o «Ligera · 720p», recordado por equipo. Medido con esta cámara: 1920×1080 a 15 cps ≈ 0.9 Mb/s; 1280×720 a 15 cps ≈ 0.2 Mb/s. La Ronda dice la resolución de lo grabado. Cambiar la resolución o los cuadros por segundo que la cámara codifica no se tocó: se hace en la app Tapo.
  - **Optimización:** FFmpeg entrega YUV (la mitad de datos) y el motor convierte a color sólo los cuadros que analiza. Medido en reposo: FFmpeg de 20 % a 6 % y motor 5 % de un núcleo; con alguien a la vista, motor 12–18 %. Se probó la decodificación por hardware (`-hwaccel auto`) y se descartó: subía FFmpeg a 17 %. La ventana de la app, con el vivo 1080p abierto, es lo que más gasta (WebView2 ~40 %); se suelta sola al minuto de estar oculta, y «Ligera» la baja.
  - **Placas de carros:** pendiente para una cámara de exterior.
  - **Corregido:** el motor servía la interfaz sin `Cache-Control` y WebView2 mostraba versiones viejas; el instalador ahora detiene el motor (y espera a FFmpeg) antes de reemplazarlo.
- **Hasta 3 redes por cámara: hecho, sin probar el cambio físico de red.** Cada red donde Velador ve la cámara queda guardada (se reconoce por el router). Al cambiar de red usa la dirección recordada, y si no hay, la busca por número de serie. Se ven y se olvidan en Cámaras. En macOS el nombre sale como «Red 192.168.1.1» porque el sistema no entrega el nombre del WiFi a un servicio.
- **Luz blanca, modo de noche, voltear la imagen y WiFi de la cámara: hecho, por el protocolo privado de Tapo.** El motor lo habla por sí mismo (SPAKE2+ sobre P-256 y AES-CCM, portado de `pytapo` y verificado con sus vectores; el protocolo anterior queda como respaldo). Probado con la cámara real: leer estado, encender y apagar la luz, y listar redes. **Sin probar:** cambiar el modo de noche, voltear la imagen y, sobre todo, cambiar la cámara de red (no se ejecutó para no dejarla desconectada). Usa la contraseña de la cuenta Tapo; si no se da aparte, prueba con la de la cuenta de cámara.
- **Cámara con IP cambiante: hecho.** El motor la reencuentra por su número de serie (probado apuntándolo a una dirección equivocada).
- **Fase 8 (rama `feature/zone-faces`), hecha el 2026-11-15 en la PC e instalada ahí; sin commit:**
  - **Por qué no seguía:** la calibración era del montaje anterior. Medido hoy: la velocidad de giro no es proporcional a la orden (0.06, 0.17 y 0.50 unidades por segundo a 0.25, 0.5 y 1) y la inclinación va al revés en órdenes continuas. La calibración nueva lo mide con la posición real; las flechas quedaron derechas (comprobado: «arriba» sube, «derecha» va a la derecha). **El seguimiento sigue sin probarse con una persona caminando.**
  - **Zona de vigilancia:** centro en x 0.00; giro útil de −0.35 a +0.35 (con eso la imagen ya abarca de muro a muro) e inclinación de −0.70 a +0.20. Guardada con reposo en +0.35, −0.50 (viendo la calle hacia la derecha). Editor en Cámaras con mapa de 24 fotos; probado arrastrando un límite con el ratón en Edge.
  - **Caras:** con gente real de hoy, a un motociclista de frente le sacó la cara a 55 px del 1080p. Corregido en la misma prueba: a un ciclista de espaldas le «encontraba» una cara en el matorral; ahora sólo se mejora una cara ya vista y de tamaño coherente. La fusión pasa sus pruebas con imágenes sintéticas; **falta verla con una cara real**.
  - **Imagen de la cámara:** sólo la nitidez tiene efecto; se dejó en su valor original (50).
  - **Responsivo:** corregidos los desbordes de Cámaras (820–1100 px) y Personas (375 px).
  - **Segunda vuelta el mismo día, tras la prueba del usuario caminando:** la grabación mostró que la cámara lo perdía al pasar por debajo, se quedaba viendo el suelo y se pasaba del límite inferior. El control se rehízo (`PathFollower`): usa la posición real de la cámara, predice a la persona a lo largo de la calle (en la tangente del ángulo de giro, donde caminar a paso constante es una recta), saca la inclinación de la ruta de la calle, que aprende de donde ve gente, y lleva siete hipótesis del retraso del video para quedarse con la que mejor explica lo que ve. En el simulador con los datos medidos: 94–100 % a la vista a paso normal aunque el retraso real difiera 0.4 s; trote 100 %; bicicleta 66–100 %. **Sin probar con una persona real.** Cada seguimiento queda en `follow.csv` (carpeta de datos) para diagnosticar.
  - **Calibración:** la medida de la imagen fallaba con grava y ladrillo y con la hora impresa; ahora compara las dos fotos en dos dimensiones sin la franja de la hora ni el logotipo, precarga el juego del engrane y exige que cuatro repeticiones coincidan. Medido: 2.24 anchos y 1.01 altos por unidad (la ficha da 2.31 y 1.09); la cámara arranca en 0.14 s y el video llega 1.33 s después.
  - **Ronda:** el botón de pausa no respondía porque el icono se redibujaba en cada cuadro y el clic caía en un elemento ya reemplazado; corregido y centrado.
  - **Personas:** galería foto por foto con visor a pantalla completa (zoom con rueda, pellizco y arrastre; cuerpo, cara y combinada; anterior y siguiente; borrar una foto).
  - **Caras:** se descartan las detecciones que no tienen ojos, nariz y boca en su lugar; la combinada pide tres cuadros que coincidan y descarta los valores extremos. **Error propio:** una revisión de las caras guardadas las borró todas, también las buenas; se regeneraron desde las fotos de cuerpo (83 de 469), pero las ocho de hoy recortadas del 1080p se perdieron.
  - **En vivo:** el video llena su espacio en ventanas medianas y los avisos ya no se enciman con la hora. Barrido de 340 a 1440 px cada 40 px en todas las vistas, con el editor de zona y el visor abiertos: sin desbordes.
  - 135 pruebas del motor en verde. La app de escritorio no cambió. Capturas del barrido a mano en `docs/local/coverage/` (privado).
  - **Tercera vuelta, tras la caminata del usuario de las 12:25:** el seguimiento lo siguió en los 20 s revisados (quedó al borde dos veces y lo retomó). La fusión produjo sus dos primeras «combinadas» reales: quitan bloques pero salen suaves. Medido en la grabación: el 1080p iba en su tope (1.3 Mb/s con audio) y la compresión era el límite de la cara; la app eligió un cuadro de perfil y agachado habiendo cuadros de frente a 70–75 px un segundo después.
  - **Bitrate:** el 1080p quedó en 2048 kbps (medido después: 1.85 Mb/s, sin corte en la grabación). El motor fallaba al cambiarlo porque probaba métodos con nombre; la cámara lo acepta con un `set` directo. Son unos 9 GB más por día.
  - **Elección de la cara:** se guardan hasta tres momentos por avistamiento, separados 1.2 s, y se busca en el 1080p alrededor de cada uno; la calidad usa una sola fórmula que castiga perfil y cabeza agachada. 138 pruebas en verde; motor instalado a las 12:41 sin hueco. **Sin probar con una persona:** falta repetir la caminata.
  - **Caminata de las 12:58:** dos caras, de 84 y 63 px, con menos bloques que la de las 12:25; la de 84 px sigue mirando abajo y con los ojos en sombra por el sol de mediodía, y esta vez no salió ninguna «combinada» (sin investigar). Mejora modesta, no una cara nítida.
  - **Por qué el seguimiento se retrasaba:** `follow2.csv` muestra que, cuando hacía falta girar e inclinar a la vez, el giro se quedaba parado hasta 2.5 s (12:58:11–13 y 12:58:29–31) mientras la inclinación sí avanzaba, y la persona se salía por el borde. La cámara mueve un eje a la vez y el motor los alternaba cada 0.3 s, menos de lo que tarda el giro en arrancar. Ahora cada eje se sostiene al menos 0.8 s (1.6 s como máximo si el otro espera) y el giro tiene prioridad. El simulador ya modela ese arranque lento: con turnos cortos el giro se atasca y con largos no. 139 pruebas en verde; instalado a las 13:04 sin hueco.
  - **Caminata de las 13:07 con el giro corregido:** 32 s seguidos con la misma persona sin perderla (en las de 12:25 y 12:58 se perdió dos veces cada una); las órdenes de giro todavía cambian de signo seguido (cabecea). La cara elegida fue una con la mano tapando los ojos: la elección no detecta eso. El usuario no ve mejora en las caras: 70–85 px comprimidos no dan una cara nítida con información real. Vio una demostración de un restaurador (GFPGAN) y lo descartó: las caras se quedan así.
  - **Cabeceo:** la cámara reporta su posición tarde y a saltos; mientras giraba rápido el motor calculaba mal dónde estaba la persona y la orden cambiaba de sentido. Ahora un salto mayor que la velocidad máxima se reparte hacia atrás en el tiempo, la estimación confía menos en lo que ve mientras la cámara gira, y el refuerzo de velocidad por eje bajó de 1.8 a 1.5. Repitiendo fuera de línea la caminata de las 13:07, los cambios de sentido que el motor le atribuía a la persona bajan de 9 a 3. 141 pruebas en verde; instalado a las 13:34 sin hueco. **Sin probar con una persona.**

## Linux (2026-11-16)

Objetivo: que funcione igual que en Windows pero más fresco (allá la laptop se calentaba).

**Hecho (sin commit todavía, en `feature/linux`):**
- Contraseña en el llavero de KDE con `secret-tool` (`LinuxSecretStore`); probado guardar y leer.
- «No dormir» con `systemd-inhibit --what=sleep` atado al PID del motor (`LinuxPowerGuard`, comparte base con el de macOS).
- `Velador.Engine install|uninstall` en Linux: servicio de usuario `~/.config/systemd/user/velador-engine.service` con `Restart=always`, `Nice=5`, `CPUWeight=50` y, si la CPU es híbrida, `AllowedCPUs=` con los núcleos eficientes (`/sys/devices/cpu_atom/cpus`; en esta laptop 12-15). MediaMTX y FFmpeg heredan esos núcleos.
- Discos externos en `/run/media/<usuario>/<disco>` y `/media/<usuario>/<disco>`; red con `ip neigh` y nombre del WiFi con `nmcli`; «mostrar recorte» abre la carpeta con `xdg-open`.
- Ventana (Rust): datos en `~/.config/Velador` (igual que el motor), arranque con `systemctl --user start`, y `GST_PLUGIN_FEATURE_RANK` para decodificar el vivo con la Intel (`vah264dec`) y no despertar la NVIDIA.
- `scripts/fetch_binaries.sh` usa `sha256sum` si no hay `shasum`. Nuevo `scripts/install_linux.sh`: publica el motor en `~/.local/share/velador/engine/<versión>`, lo registra, compila la ventana con `tauri build --no-bundle` y deja `~/.local/bin/velador` + entrada en el menú.
- .NET 10 SDK en `~/.dotnet10` (10.0.404). MediaMTX y modelos ya bajados en `tools/`. `webkit2gtk-4.1` instalado.
- 126 pruebas del motor en verde en Linux. Motor arrancado a mano en una carpeta temporal (puerto 18352): token, `summary` y la interfaz responden.
- Detección de personas en Linux va en CPU (ONNX Runtime, 2 hilos); no se usa CUDA a propósito para no prender la NVIDIA.

- Tras reiniciar: ventana compilada en Linux (`fmt`, `clippy -D warnings` limpios, 16 pruebas). `scripts/install_linux.sh` corrió completo: servicio activo, fijado a núcleos 12-15, ~200 MB de RSS; la ventana abre.

- Problemas al dar de alta (2026-11-16): (a) la ventana se cayó: WebKitGTK aborta en `createAudioSink` si falta `gst-plugins-good` (no hay `autoaudiosink`); (b) la búsqueda no encontró la cámara: la cámara sí responde desde el puerto 3702 pero `ufw` (entrada DROP) tira la respuesta; (c) `401` en RTSP: la cámara rechaza usuario y contraseña también probados directo con `ffprobe`, así que no es el llavero. Probablemente se usó la cuenta Tapo en vez de la cuenta de cámara. `install_linux.sh` ahora revisa los elementos GStreamer y avisa de ufw.

- Cámara dada de alta y grabando los dos streams en Linux (2026-11-16, 19:52). Causas del atasco, en orden: (1) la primera cuenta era la de Tapo y los reintentos con `401` bloquearon el RTSP hasta reiniciar la cámara; (2) `stream1` cerraba la conexión tras `PLAY` porque la Mac seguía grabando 24/7 con su motor (la C500 admite 2 sesiones de alta calidad). Apagada la Mac, entró.
- Corregido en el vigía: con la fuente desconectada y MediaMTX vivo ya no reinicia MediaMTX cada 30 s (cortaba el stream ligero y dejaba sesiones colgadas en la cámara; MediaMTX ya reintenta solo). Prueba nueva; 127 en verde.
- Pendiente: el motor no alimenta `CredentialsRejected`, así que un `401` sigue reintentando cada 5 s y puede bloquear la cámara. Y decidir qué equipo graba 24/7: tres motores a la vez no caben en esta cámara.

- Reinstalar sobre la misma versión dejó el motor sin `libe_sqlite3.so` ni ONNX Runtime (la publicación incremental no los volvió a copiar) y se caía al arrancar. `install_linux.sh` ahora borra la carpeta de la versión antes de publicar.
- KINGSTON es `sda1` (NTFS, 675 GB, 280 GB de datos personales) en el mismo SSD externo que este Linux. No montaba: `ntfs3` lo rechaza por «volume is dirty» (Windows no lo cerró limpio: inicio rápido o Velador grabando al apagar). No formatear. Arreglo: `ntfsfix -d` o `chkdsk` en Windows, apagar el inicio rápido de Windows y montarlo por `fstab` con `nofail` en `/run/media/davidag/KINGSTON`.

- KINGSTON arreglado con `ntfsfix -d` y montado por `fstab` (`nofail`) en `/run/media/davidag/KINGSTON`. Falta apagar el inicio rápido en Windows (`powercfg /h off`).
- **Retención borraba grabaciones en Linux (2026-11-16):** `RecordingStore.Disk()` medía `Path.GetPathRoot`, que en Unix siempre es `/`; con KINGSTON (39 %) medía el disco interno (94 %, arriba del 80 %) y borró 261 segmentos (18.8 GB) al arrancar. Ahora mide el montaje más profundo que contiene la carpeta (`MountOf`). En macOS con disco externo tenía el mismo fallo. También corregido: el chequeo de la bandeja en `install_linux.sh` fallaba con `pipefail` + `grep -q` aunque la biblioteca existiera.
- Arranque dual en esta laptop: el Velador de Windows también graba en KINGSTON y con el reloj 6 h adelantado (Linux guarda el RTC en UTC); dejó segmentos con fecha de mañana. Falta `RealTimeIsUniversal=1` en Windows.
- SIGSEGV del motor al apagarse (2026-11-16, 20:22): `StopAsync` liberaba las sesiones ONNX mientras un `RunAsync` (lanzado sin esperar) seguía en `Run`. Ahora `PersonDetector`/`FaceDetector.Dispose` toman el mismo candado que la inferencia y las llamadas posteriores no hacen nada. Prueba nueva: sin el arreglo, el proceso de pruebas se cae.
- KINGSTON volvió a quedar «dirty» (2026-11-16, 21:24) y **no fue Windows**: al apagar Linux, `umount` falló con «target is busy» porque `velador-engine` (servicio de usuario, grabando en KINGSTON) seguía vivo; systemd no ordena `user@1000.service` contra el montaje. Arreglo: `x-systemd.before=user@1000.service` en la línea de `fstab`, para que el motor (y MediaMTX/FFmpeg) se detenga antes de desmontar. Mientras KINGSTON no monta, el punto de montaje es de root y el motor no escribe en el disco interno por accidente.
- El vivo se quedaba en «Conectando»: el WebKitGTK de Arch/CachyOS está compilado sin WebRTC (`RTCPeerConnection` no existe aunque se active) y tampoco acepta HLS en `<video>`, pero sí MSE con H.264. Ahora, sin `RTCPeerConnection`, `live.js` lee el HLS de baja latencia de MediaMTX (pedazos de 200 ms) que el motor reenvía en `/api/cameras/{id}/hls/{stream}/{archivo}` y los mete a MSE. Probado en WebKitGTK contra la cámara: 1080p avanzando sin errores. Sin audio (HLS no admite G.711). Visto por el usuario en la ventana: **funciona pero con mucho retraso** frente a WebRTC; falta medirlo y reducirlo (pedir la lista con espera `_HLS_msn`/`_HLS_part` en vez de cada 300 ms, empezar en el pedazo más nuevo, o bajar el intervalo de cuadros clave de la cámara).

**Revisión del 2026-11-17 (hecha desde Windows, sin el equipo Linux a la mano):**
- El vivo ya no va por HLS. Causa del retraso: el reproductor pedía la lista cada 300 ms, sólo se ponía al corriente al ir 2 s atrás, y MediaMTX cortaba pedazos de 0.3 a 1 s con esta cámara. Ahora el motor entrega MP4 con un fragmento por cuadro (`/api/cameras/{id}/live.mp4`, FFmpeg copiando del RTSP local) y `live.js` lo mete a MSE pegado al final. Medido aquí en Edge sin WebRTC contra el stream real: 1080p a 15 cps, 0.4–0.5 s detrás de lo recibido; la barra ya muestra resolución, cuadros y Mb/s. **No se ha visto en WebKitGTK.**
- Cuenta rechazada: al primer `401` el motor deja la fuente en pausa y reintenta cada 10 minutos en vez de cada 6 s; el hueco sale como «Cuenta rechazada». Probado con una cámara falsa: 4 intentos en los primeros 4 s y ninguno después. Guardar la cámara de nuevo reintenta de inmediato.
- La ventana ya es de instancia única: abrirla otra vez trae la que está en la bandeja en lugar de duplicarla. Se quitó el interruptor de WebRTC de WebKitGTK (no hacía nada y recargaba la página al abrir).
- `install_linux.sh` revisa lo que de verdad se usa (demuxer MP4, `h264parse`, un decodificador H.264, AAC para la Ronda y la biblioteca de la bandeja) y deja el ícono del menú en 32, 128, 256 y 512 px con `StartupWMClass=velador`.
- CI: el motor y la ventana se prueban también en Ubuntu, y en cada rama `feature/*`.
- La prueba del servicio de systemd fallaba al compilar en Windows por los fines de línea; el archivo de unidad ahora sale siempre con `\n`.

**Siguiente, en el equipo Linux:**
1. `git pull` en `feature/linux` y `scripts/install_linux.sh`.
2. Ver el vivo en la ventana: que arranque, cuánto retraso tiene frente al reloj de la cámara y que «Ligera» también se vea.
3. Elegir el disco KINGSTON en Ajustes: el disco interno tiene ~18 GB libres y un día pesa 15–16 GB.
4. Medir CPU/temperatura con vivo abierto, oculto y detectando (motor, MediaMTX, FFmpeg, WebKitWebProcess) y comparar con lo de Windows; confirmar con `intel_gpu_top`/`nvidia-smi` que el vivo usa la Intel y la NVIDIA queda dormida.
5. Revisar bandeja, ícono en la barra de tareas y el menú, avisos, abrir dos veces y arranque con la sesión tras reiniciar.
6. Revisar el tema nuevo (turno de noche) en WebKitGTK.
7. Decidir qué equipo graba 24/7: la cámara admite dos sesiones de alta calidad y hay tres motores (Mac, Windows, Linux).

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

## Rediseño: turno de noche (2026-11-15)

- La app real (`web/`) lleva el tema nuevo: colores y letras en `tokens.css`, piezas en `skin.css`, Sereno en `components/mascot.js`. La lógica de las vistas no cambió, salvo En vivo: marco de monitor, dos medidores de aguja (cobertura de hoy y disco libre, con datos reales) y una palanca que mueve la cámara por el mismo camino que las flechas.
- Sereno refleja el estado real: en calma de guardia, ojos rojos sin grabar o sin motor, ojos cerrados en pausa y violetas cuando la detección ve a alguien en el vivo.
- Copiado a mano al motor instalado en esta PC (`%LOCALAPPDATA%\Velador\engine\web`); el motor no se reinició y la grabación no se tocó.
- Revisado en capturas de Edge sin ventana contra el motor real: En vivo y Ronda de noche; En vivo, Personas, Continuidad, Cámaras y Ajustes de día. En esas capturas el video sale negro porque el navegador sin ventana no abre el vivo.
- Sin revisar: Sucesos, Recortes, Bienvenida y el editor de zona con el tema nuevo; las animaciones y la palanca con una persona usándolas; WebKit; ancho de teléfono.
- Íconos de escritorio y bandeja regenerados en `desktop/src-tauri/icons` desde `desktop/art`. Windows lleva los suyos (`app_windows.svg` › `icon.ico` y `tray_windows.png`): sin el margen de macOS, para que Sereno llene la barra de tareas y la bandeja.
- 2026-11-17: en esta PC se cambió sólo `velador-desktop.exe` de la app instalada por el compilado con el ícono nuevo; el motor no se tocó. Falta verlo a ojo en la barra de tareas.
- Las tipografías Schibsted Grotesk se quitaron; entran Bungee, Rubik, VT323 y Special Elite, bajadas de Google Fonts y servidas localmente.

## Repositorio

`github.com/ReyDavidAG/velador`, público, con `main`, `develop` y ramas `feature/*`. Commits de una línea con el correo de Bitácora.

