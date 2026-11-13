# Windows · dónde quedó y cómo seguir

El trabajo de Windows ya está unido a `develop`. Última revisión: 2026-11-12.

## Lo que ya está comprobado en un Windows real (el de GitHub Actions, sin cámara)

- Las 83 pruebas del motor pasan, incluidas guardar y leer una contraseña en el Administrador de credenciales y la petición de «no dormir».
- El motor arranca, crea su token, responde `summary`, rechaza sin token (401), sirve la interfaz y guarda ajustes.
- `Velador.Engine.exe install` registra la tarea `Velador\Engine` (al iniciar sesión, con reinicio si falla) y la tarea lanza el motor. `uninstall` la quita.
- La app de escritorio compila en Windows y `scripts/build_windows.ps1` produce el instalador NSIS.

## Lo que falta

1. Confirmar que el último CI de la rama quedó en verde (el script de prueba terminaba con código 1 por un `reg query`; ya tiene `exit 0`, pero esa corrida no se revisó).
2. Probar en tu PC, con la cámara: instalar, dar de alta la cámara, vivo en la ventana (WebView2), la Ronda con sonido, recorte, disco externo y arranque al iniciar sesión.
3. Instalar como usuario sin permisos de administrador: si `schtasks` se niega, el motor cae a la clave `Run` del registro (sin reinicio automático). No se ha visto en la práctica.
4. ~~El instalador no detiene el motor antes de actualizar~~: resuelto con `desktop/src-tauri/windows/hooks.nsh` (detiene el motor y MediaMTX, espera a que FFmpeg suelte sus archivos, reemplaza y vuelve a arrancar la tarea; al desinstalar quita la tarea). Actualizar deja un hueco de unos segundos con causa «actualización».
5. Sin firma: SmartScreen va a advertir al abrir el instalador.

## Probado en la PC con la cámara (2026-11-13)

- Instalación como usuario sin administrador: la tarea `Velador\Engine` se registra con `schtasks` sin pedir permisos.
- Cámara dada de alta y grabando desde la app.
- Corregido: MediaMTX abría una consola visible cada vez que arrancaba o el vigía lo reiniciaba (faltaba `CreateNoWindow`), y la app abría una consola detrás de la ventana (faltaba `windows_subsystem`).
- Corregido: reinstalar encima dejaba el motor viejo porque el archivo estaba en uso.

## Lo nuevo de la fase 7 y cómo probarlo (2026-11-13)

Antes de probar el seguimiento: en la app Tapo apaga el **seguimiento de movimiento** de la cámara, para que no compita con el de Velador.

1. **Pausar:** En vivo › «Pausar grabación» › 15 minutos. Debe salir «SIN GRABAR» sobre el video, el vivo sigue, y en la bandeja «Grabación en pausa · se reanuda en…». «Reanudar grabación» lo quita. En la Ronda queda el hueco «Guardia en pausa».
2. **Bandeja:** clic en el icono › «Pausar la grabación» y luego «Reanudar la grabación».
3. **Espacio:** Ajustes › Dónde se guarda: cuánto pesa un día y cuándo empieza a borrar. Bajar «Guardar hasta» a 2 días cambia la fecha de borrado.
4. **Flechas:** con la cámara ya calibrada, «derecha» debe llevar la vista a la derecha y «arriba» hacia arriba. Antes estaban invertidas por el montaje de cabeza.
5. **Seguir personas:** En vivo › «Seguir personas». Camina frente a la cámara a paso normal, luego rápido, y sal y vuelve a entrar por un lado. Sobre el video salen los recuadros (el morado fuerte es a quien sigue) y arriba «Siguiendo» o «Buscando hacia donde iba». Si mueves la cámara a mano, el seguimiento espera 10 s.
6. **Ir y regresar:** con «Seguir personas», camina a un lado hasta salir y regresa al otro. Arriba debe decir «Buscando hacia donde se fue» y, si no te encuentra en 8 s, «Regresando a la posición de inicio». Mueve la cámara a mano a otro lugar: esa será la nueva posición de inicio.
7. **Caras:** en Personas, la portada de cada quien lleva su cara en la esquina cuando se le vio de frente.
8. **Personas:** al otro día, la vista Personas debe mostrar a cada quien agrupado con sus horas; un clic en la foto lleva a ese momento en la Ronda.
9. **Pantalla completa:** el control de movimiento, la pausa y «Seguir personas» también están ahí.

Cifras medidas en esta PC: detección en la tarjeta gráfica (DirectML), motor 11 % y FFmpeg 16 % de un núcleo con alguien a la vista; el video para la detección llega con 1.56 s de retraso.

## Cómo compilar e instalar en la PC

Requisitos: SDK de .NET 10, Node 22, Rust (`rustup`), y las herramientas de C++ de Visual Studio. El script baja MediaMTX, FFmpeg (LGPL de BtbN) y el modelo YOLOX-tiny con sus sumas fijadas; el instalador queda en unos 136 MB.

```powershell
git clone https://github.com/ReyDavidAG/velador.git
cd velador
git checkout develop
scripts\build_windows.ps1      # publica el motor, baja MediaMTX con suma verificada y arma el instalador
scripts\smoke_windows.ps1      # prueba sin cámara
```

El instalador queda en `desktop\src-tauri\target\release\bundle\nsis\`. También se puede bajar ya compilado de la corrida de CI de la rama (artefacto `velador-windows-setup`) cuando esa corrida termine bien.

Al abrir Velador por primera vez, «Iniciar la guardia» registra el motor que viene dentro de la app y lo arranca; después pide los datos de la cámara.

## Dónde vive cada cosa en Windows

| Qué | Dónde |
|---|---|
| Datos del motor (`config.json`, `token`, `velador.db`) | `%APPDATA%\Velador` |
| Grabaciones | `%USERPROFILE%\Videos\Velador`, o el disco que elijas en Ajustes |
| Contraseña de la cámara | Administrador de credenciales, entrada `Velador:<id>` |
| Arranque | Programador de tareas › `Velador\Engine` |

## Mac y PC a la vez

Cada equipo graba por su cuenta mientras esté prendido, con su propia ronda. La cámara aceptó dos conexiones por stream en las pruebas, así que caben dos equipos grabando a la vez, no más. Todavía no se juntan las grabaciones de los dos en una sola ronda.
