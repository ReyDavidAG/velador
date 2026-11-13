#!/usr/bin/env bash
set -euo pipefail

VERSION="v1.21.3"
case "$(uname -s)-$(uname -m)" in
	Darwin-arm64) ASSET="mediamtx_${VERSION}_darwin_arm64.tar.gz" ;;
	Darwin-x86_64) ASSET="mediamtx_${VERSION}_darwin_amd64.tar.gz" ;;
	Linux-x86_64) ASSET="mediamtx_${VERSION}_linux_amd64.tar.gz" ;;
	*) echo "Plataforma sin binario: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac

DEST="${1:-$(cd "$(dirname "$0")/.." && pwd)/tools/mediamtx}"
BASE="https://github.com/bluenviron/mediamtx/releases/download/${VERSION}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

curl -fsSL -o "$WORK/$ASSET" "$BASE/$ASSET"
curl -fsSL -o "$WORK/checksums.sha256" "$BASE/checksums.sha256"
(cd "$WORK" && grep "$ASSET" checksums.sha256 | shasum -a 256 -c -)
mkdir -p "$DEST"
tar -xzf "$WORK/$ASSET" -C "$DEST" mediamtx LICENSE
echo "MediaMTX $VERSION en $DEST/mediamtx"

MODELS="$(cd "$(dirname "$0")/.." && pwd)/tools/models"
MODEL_SHA="427cc366d34e27ff7a03e2899b5e3671425c262ea2291f88bb942bc1cc70b0f7"
if [ ! -f "$MODELS/yolox_tiny.onnx" ]; then
	mkdir -p "$MODELS"
	curl -fsSL -o "$WORK/yolox_tiny.onnx" "https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_tiny.onnx"
	echo "$MODEL_SHA  $WORK/yolox_tiny.onnx" | shasum -a 256 -c -
	mv "$WORK/yolox_tiny.onnx" "$MODELS/yolox_tiny.onnx"
fi
if [ ! -f "$MODELS/face_yunet.onnx" ]; then
	curl -fsSL -o "$WORK/face_yunet.onnx" "https://media.githubusercontent.com/media/opencv/opencv_zoo/f12e12798e8314f7c074a6656816c048dcc95b7a/models/face_detection_yunet/face_detection_yunet_2023mar.onnx"
	echo "8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4  $WORK/face_yunet.onnx" | shasum -a 256 -c -
	mv "$WORK/face_yunet.onnx" "$MODELS/face_yunet.onnx"
fi
echo "Modelos de personas y caras en $MODELS"
command -v ffmpeg >/dev/null || echo "Para detectar personas en macOS instala FFmpeg: brew install ffmpeg"
