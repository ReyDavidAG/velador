#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VERSION="$(sed -n 's/.*"version": "\(.*\)".*/\1/p' desktop/src-tauri/tauri.conf.json | head -1)"
SHARE="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$SHARE/velador/engine/$VERSION"

if [ -x "$HOME/.dotnet10/dotnet" ]; then
	export DOTNET_ROOT="$HOME/.dotnet10" PATH="$HOME/.dotnet10:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

for tool in ffmpeg secret-tool systemd-inhibit; do
	command -v "$tool" >/dev/null || { echo "Falta $tool" >&2; exit 1; }
done
has() { gst-inspect-1.0 --exists "$1"; }
for element in autoaudiosink qtdemux h264parse; do
	has "$element" || { echo "Falta el elemento GStreamer $element (en Arch: gst-plugins-base gst-plugins-good gst-plugins-bad)" >&2; exit 1; }
done
has vah264dec || has avdec_h264 || has openh264dec || { echo "Falta un decodificador H.264 para el vivo y la Ronda (en Arch: gst-plugin-va o gst-libav)" >&2; exit 1; }
has avdec_aac || has fdkaacdec || has faad || echo "Aviso: sin decodificador AAC la Ronda se oye en silencio (en Arch: gst-libav)" >&2
ldconfig -p | grep -q -E 'libayatana-appindicator3|libappindicator3' || { echo "Falta la biblioteca del icono de la bandeja (en Arch: libayatana-appindicator)" >&2; exit 1; }
if command -v ufw >/dev/null && grep -q '^ENABLED=yes' /etc/ufw/ufw.conf 2>/dev/null && ! grep -q 'sport 3702' /etc/ufw/user.rules 2>/dev/null; then
	echo "Aviso: ufw bloquea la búsqueda de cámaras. Permítela con: sudo ufw allow proto udp from 192.168.0.0/16 port 3702" >&2
fi
[ -x tools/mediamtx/mediamtx ] || scripts/fetch_binaries.sh

systemctl --user stop velador-engine.service 2>/dev/null || true
rm -rf "$DEST" && mkdir -p "$DEST"
dotnet publish Velador.Engine -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o "$DEST" >/dev/null
cp tools/mediamtx/mediamtx "$DEST/mediamtx"
cp -R web "$DEST/web" && cp -R tools/models "$DEST/models"
"$DEST/Velador.Engine" install

[ -d node_modules ] || npm ci --no-audit --no-fund
npx tauri build --no-bundle --config desktop/src-tauri/tauri.conf.json >/dev/null
install -Dm755 desktop/src-tauri/target/release/velador-desktop "$HOME/.local/bin/velador"
install -Dm644 desktop/src-tauri/icons/32x32.png "$SHARE/icons/hicolor/32x32/apps/velador.png"
install -Dm644 desktop/src-tauri/icons/128x128.png "$SHARE/icons/hicolor/128x128/apps/velador.png"
install -Dm644 "desktop/src-tauri/icons/128x128@2x.png" "$SHARE/icons/hicolor/256x256/apps/velador.png"
install -Dm644 desktop/src-tauri/icons/icon.png "$SHARE/icons/hicolor/512x512/apps/velador.png"
install -Dm644 /dev/stdin "$SHARE/applications/velador.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Velador
Comment=Graba y cuida tu cámara IP sin nube
Exec=$HOME/.local/bin/velador
Icon=velador
Categories=Video;Utility;
StartupWMClass=velador
DESKTOP
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t -f "$SHARE/icons/hicolor" 2>/dev/null || true
command -v update-desktop-database >/dev/null && update-desktop-database -q "$SHARE/applications" 2>/dev/null || true
echo "Velador $VERSION instalado. Ábrelo desde el menú o con: ~/.local/bin/velador"
