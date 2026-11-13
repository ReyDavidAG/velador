# Velador

La app de escritorio (macOS y Windows) que graba y cuida una cámara IP de casa sin nube y sin microSD: en vivo, grabación 24/7 al disco, una línea de tiempo por día y avisos cuando algo pasa. Empieza con una TP-Link Tapo C500 por RTSP y ONVIF.

**Estado:** versión 0.1.0 para macOS (Apple Silicon). La de Windows comparte el motor y la interfaz, pero su instalador y su prueba en un equipo real están pendientes. El avance está en [`state.md`](state.md).

## Qué hace

- **En vivo** con menos de un segundo de retraso, y control para mover la cámara.
- **Ronda:** la línea de tiempo del día. Se arrastra, se acerca, muestra una vista previa al pasar el cursor y salta los huecos.
- **Huecos a la vista:** cada momento sin grabar queda con su causa (reposo, cámara, red, luz, disco).
- **Sucesos** de la propia cámara (movimiento y personas), sin gastar procesador.
- **Recortes** que no se borran con lo viejo.
- **Disco externo:** graba en el disco que elijas; si lo desconectas sigue en la Mac y al volver lo pasa al disco.
- **Avisos del sistema** cuando hay un hueco o una persona.

## Instalar en macOS

Hace falta el SDK de .NET 10, Rust y Node.

```bash
scripts/install_macos.sh
open -a Velador
```

La primera vez la app pide los datos de la cámara. Las grabaciones van a `~/Movies/Velador` y se cambian en Ajustes.

## Ver la propuesta original

```bash
cd propuesta && python3 -m http.server 5188
```

## Cómo está pensado

- **Motor** en .NET que graba aunque la ventana esté cerrada, con MediaMTX y FFmpeg; el video nunca se recodifica.
- **Ventana** en Tauri 2 que sólo mira al motor en `127.0.0.1`.
- **Interfaz** en HTML, CSS y módulos ES, sin framework ni compilación.
- **Todo local:** sin cuentas, sin telemetría, sin abrir puertos.

Lo que se investigó y lo que se midió con la cámara real está en [`docs/`](docs).

## Licencia

MIT.
