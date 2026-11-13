# Avisos de terceros

Velador es MIT. Junto al motor se distribuyen estas piezas, sin modificar, con su propia licencia.

| Pieza | Para qué | Licencia | Fuente |
|---|---|---|---|
| MediaMTX v1.21.3 | Recibe, graba y reenvía el video de la cámara | MIT (`MEDIAMTX_LICENSE.txt`) | https://github.com/bluenviron/mediamtx |
| FFmpeg 9.0.2, compilación LGPL compartida de BtbN (`autobuild-2026-10-31-12-59`) | Decodifica el stream ligero para la detección de personas (sólo Windows; en macOS se usa el del sistema) | LGPL 2.1 o posterior (`ffmpeg/FFMPEG_LICENSE.txt`) | https://ffmpeg.org · compilación: https://github.com/BtbN/FFmpeg-Builds |
| YOLOX-tiny (`yolox_tiny.onnx`, versión 0.1.1rc0) | Modelo que encuentra personas en la imagen | Apache 2.0 | https://github.com/Megvii-BaseDetection/YOLOX |
| ONNX Runtime | Corre el modelo | MIT | https://github.com/microsoft/onnxruntime |
| DirectML (sólo Windows) | Corre el modelo en la tarjeta gráfica | Licencia del paquete Microsoft.AI.DirectML | https://www.nuget.org/packages/Microsoft.AI.DirectML |
| SQLite (Microsoft.Data.Sqlite) | Índice de huecos, sucesos, recortes y personas | Dominio público / MIT | https://www.sqlite.org · https://github.com/dotnet/efcore |

FFmpeg se usa como programa aparte (`ffmpeg.exe` y sus bibliotecas), sin enlazarlo al motor, y se puede reemplazar por cualquier otra compilación de FFmpeg con la variable `VELADOR_FFMPEG`. Las sumas SHA-256 de FFmpeg y del modelo están fijadas en `scripts/build_windows.ps1` y `scripts/fetch_binaries.sh`.
