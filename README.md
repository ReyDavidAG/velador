# Velador

La app de escritorio (macOS y Windows) que graba y cuida una cámara IP de casa sin nube y sin microSD: en vivo, grabación 24/7 al disco, una línea de tiempo por día y avisos cuando algo pasa. Empieza con una TP-Link Tapo C500 por RTSP y ONVIF.

**Estado:** propuesta aprobada; el motor todavía no existe. El avance está en [`state.md`](state.md) y lo que sigue en [`plan.md`](plan.md).

## Ver la propuesta

```bash
cd propuesta && python3 -m http.server 5188
# http://localhost:5188
```

Todo lo que muestra es de ejemplo: la imagen es una escena dibujada.

## Cómo está pensado

- **Motor** en .NET que graba aunque la ventana esté cerrada, con MediaMTX y FFmpeg; el video nunca se recodifica.
- **Ventana** en Tauri 2 que sólo mira al motor en `127.0.0.1`.
- **Interfaz** en HTML, CSS y módulos ES, sin framework ni compilación.
- **Todo local:** sin cuentas, sin telemetría, sin abrir puertos.

Lo que se investigó y lo que se midió con la cámara real está en [`docs/`](docs).

## Licencia

MIT.
