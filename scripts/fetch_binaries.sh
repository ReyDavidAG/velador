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
