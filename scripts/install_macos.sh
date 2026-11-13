#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VERSION="$(sed -n 's/.*"version": "\(.*\)".*/\1/p' desktop/src-tauri/tauri.conf.json | head -1)"
HOME_DIR="$HOME/Library/Application Support/Velador"
DEST="$HOME_DIR/engine/$VERSION"
LABEL="com.reydavidag.velador.engine"

if [ -x "$HOME/.dotnet10/dotnet" ]; then
	export DOTNET_ROOT="$HOME/.dotnet10" PATH="$HOME/.dotnet10:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

{ [ -x tools/mediamtx/mediamtx ] && [ -f tools/models/yolox_tiny.onnx ]; } || scripts/fetch_binaries.sh

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
mkdir -p "$DEST"
dotnet publish Velador.Engine -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o "$DEST" >/dev/null
cp tools/mediamtx/mediamtx "$DEST/mediamtx"
rm -rf "$DEST/web" && cp -R web "$DEST/web"
rm -rf "$DEST/models" && cp -R tools/models "$DEST/models"
"$DEST/Velador.Engine" install

[ -d node_modules ] || npm ci --no-audit --no-fund
npm run --silent desktop:build >/dev/null
APP="desktop/src-tauri/target/release/bundle/macos/Velador.app"
osascript -e 'quit app "Velador"' 2>/dev/null || true
rm -rf /Applications/Velador.app && cp -R "$APP" /Applications/Velador.app
echo "Velador $VERSION instalado en /Applications. Ábrelo con: open -a Velador"
