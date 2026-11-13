# CLAUDE.md

Guía para Claude Code en este repositorio. `plan.md` dice **qué sigue** y `state.md` **dónde vamos**.

## Qué es Velador

La app de escritorio (macOS y Windows) que graba y cuida una cámara IP de casa sin depender de la nube ni de una microSD: ve en vivo, graba 24/7 al disco, deja recorrer el día en una línea de tiempo y avisa cuando algo pasa. Empieza con una TP-Link Tapo C500 por RTSP/ONVIF y está hecha para sumar cámaras.

Repo **público** (MIT). Todo corre local: sin nube, sin cuentas, sin telemetría. Nunca van al repo la IP, el usuario ni la contraseña de la cámara, ni imágenes reales: el modo demo y `propuesta/js/data.sample.js` son ficticios.

### Objetivos

1. Abrir la app y ver la cámara en vivo en menos de 2 segundos.
2. Encontrar qué pasó a cualquier hora del día arrastrando una sola línea de tiempo.
3. Que la grabación tenga los menos huecos posibles; y que cada uno se vea, con su causa.
4. Uso mínimo de CPU: el video nunca se recodifica.
5. Guardar un recorte y sacarlo de la app en dos clics.

### Vocabulario (igual en código, pantalla y documentos)

| Pantalla | Código | Qué es |
|---|---|---|
| Motor | `engine` | El proceso que graba y sirve la API. Vive aunque la ventana esté cerrada. |
| De guardia | `on_guard` | El motor está grabando sin cortes. |
| Ronda | `round` | La línea de tiempo de un día de una cámara. |
| Tramo | `span` | Periodo grabado continuo dentro de la ronda. Se calcula fundiendo segmentos (`segment`), que son los archivos. |
| Hueco | `gap` | Periodo sin grabación, siempre con causa (`sleep`, `camera`, `network`, `engine`, `power`, `update`, `disk`, `paused`, `privacy`, `credentials`, `unknown`). |
| Suceso | `event` | Algo detectado: `motion` (Movimiento) o `person` (Persona). |
| Vigía | `watchdog` | Lo que detecta que dejó de llegar imagen y reconecta. |
| Recorte | `clip` | Pedazo de grabación guardado aparte; la retención no lo borra. |
| Cobertura | `coverage` | Porcentaje del día que quedó grabado. |

## Fases

| Fase | Qué | Dónde corre |
|---|---|---|
| 0 · Propuesta | Prototipo navegable en HTML/CSS/JS sin compilación, con datos de ejemplo. | `propuesta/` |
| 1 · Verificación | Pruebas con la cámara real antes de escribir el motor (`docs/verification.md`). | Terminal |
| 2 · Motor | .NET Worker + API mínima en `127.0.0.1:8352`; supervisa MediaMTX y FFmpeg. | Servicio de la sesión |
| 3 · Web | `web/`: módulos ES sin framework ni compilación, servidos por el motor. | Navegador y ventana |
| 4 · Escritorio | Tauri 2 (`desktop/src-tauri`): ventana, bandeja, avisos, inicio con la sesión. | `Velador.app` · `Velador.exe` |
| 5+ | Sucesos, avisos, motor en un equipo siempre prendido, acceso remoto, más cámaras. | — |

### Correr el motor

.NET 10 está instalado aparte en `~/.dotnet10` (el `dotnet` del sistema es el 8 y no se toca):

```bash
export DOTNET_ROOT=$HOME/.dotnet10 PATH=$HOME/.dotnet10:$PATH
scripts/fetch_binaries.sh                  # MediaMTX con suma verificada, en tools/
dotnet test Velador.slnx
VELADOR_MEDIAMTX=tools/mediamtx/mediamtx dotnet run --project Velador.Engine    # http://127.0.0.1:8352
```

Variables: `VELADOR_HOME` (datos; por omisión la carpeta de datos de la app del sistema), `VELADOR_PORT`, `VELADOR_WEB` (carpeta del front) y `VELADOR_CAMERA_PASSWORD_<ID>` (sólo para desarrollo; en uso real la contraseña sale del llavero). En `VELADOR_HOME` viven `config.json`, `token`, `velador.db` y `mediamtx.yml`. MediaMTX usa los cuatro puertos siguientes al del motor, todos en `127.0.0.1`.

### Ver el prototipo

```bash
cd propuesta && python3 -m http.server 5188
# http://localhost:5188
```

Usa módulos ES: necesita servidor. Después de editar, recarga con Cmd+Shift+R.

## Convenciones

- Respuestas en español; código en inglés (archivos, variables, tipos, rutas). Español sólo en lo que lee una persona.
- Sin comentarios en el código, salvo una línea para lo que el código no puede enseñar.
- Commits de una sola línea, sin descripción ni trailer.
- **Sin React ni framework en el front.** HTML, CSS y módulos ES; archivos en `snake_case`; un módulo por vista y por componente. Nada de `<select>` nativo.
- **Seguridad, no se negocia:** el motor escucha sólo en `127.0.0.1` con sesión por cookie y es el único que habla con MediaMTX (el front nunca); la contraseña de la cámara vive en el llavero del sistema (Keychain / Credential Manager), nunca en archivos ni logs; la ventana de Tauri no opera archivos ni procesos (todo va al motor por HTTP); la página remota no tiene IPC. Nada de abrir puertos en el router.
- **El video no se recodifica.** Sólo el audio pasa a AAC al guardar. Cualquier cosa que decodifique (miniaturas, detección) usa `stream2`.
- **Tiempos en UTC** (epoch ms) en disco, índice y API; el día local lo calcula la API.
- **Estructura .NET:** `Velador.Core` (puro), `Velador.Engine` (host y adaptadores), `Velador.Tests`. Interfaces sólo con dos implementaciones reales o un falso útil.
- Nada de métricas inventadas presentadas como reales: los datos de ejemplo se marcan como tales.

### Diseño

- Escritorio primero (1280 px), sin romper en 375 px.
- Oscuro por omisión, con tema claro. El color es sólo para datos: grabado verde azulado, movimiento ámbar, persona violeta, hueco rayado, en vivo rojo.
- Tipografía: Schibsted Grotesk (texto) y JetBrains Mono (horas, cifras, folios).
- Pieza de identidad: **la ronda**, la línea de tiempo de 24 horas con tramos, huecos y sucesos; y la hora en mono sobre la imagen.
- Animación sólo con `transform`/`opacity`, apagada con `prefers-reduced-motion`.
- Contraste AA y foco visible siempre.

## Cómo se trabaja

1. Antes de construir algo nuevo se acuerda en `plan.md` y el usuario lo aprueba.
2. Al terminar una tarea se actualiza `state.md`.
3. Lo visual se prueba en el navegador (y en WebKit, que es el motor de la ventana en macOS) antes de pedir aprobación.

## Git (gitflow)

`main`, `develop`, `feature/*`, `release/*` con tag, `hotfix/*`. Lo privado va en `CLAUDE.local.md`, `docs/local/` y `.git/info/exclude`.
