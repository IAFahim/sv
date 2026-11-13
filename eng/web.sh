#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
out=${2:-$root/build/web}
case "${1:-}" in
  publish) dotnet publish "$root/src/Sv.Wasm" -c Release -o "$out" ;;
  serve) cd "$out/wwwroot" && python3 -m http.server "${PORT:-8123}" --bind 127.0.0.1 ;;
  *) echo "usage: eng/web.sh publish|serve [output folder]" >&2; exit 2 ;;
esac
