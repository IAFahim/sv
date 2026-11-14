#!/usr/bin/env sh
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
mode=${1:-}
dest=${2:-}
if [ "$mode" != "sync" ] && [ "$mode" != "check" ] || [ -z "$dest" ]; then
  echo "usage: eng/unity-sync.sh sync|check <unity-project>/Packages/com.sv.core" >&2
  exit 2
fi
case $dest in /*) ;; *) dest=$PWD/$dest ;; esac
src="$root/src/Sv"
if [ "$mode" = "sync" ]; then
  mkdir -p "$dest/Runtime"
  find "$dest/Runtime" -maxdepth 1 -name '*.cs' -exec rm -f {} +
  cp "$src"/*.cs "$dest/Runtime/"
  cat > "$dest/Runtime/Sv.Core.asmdef" <<'JSON'
{
    "name": "Sv.Core",
    "rootNamespace": "Sv",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
JSON
  printf '%s\n' '-nullable:enable' > "$dest/Runtime/csc.rsp"
  cat > "$dest/package.json" <<'JSON'
{
    "name": "com.sv.core",
    "version": "2.1.0",
    "displayName": "sv core",
    "description": "sv tiers 0 and 1, synced verbatim from src/Sv by eng/unity-sync.sh",
    "unity": "7000.0"
}
JSON
  (cd "$src" && sha256sum *.cs) > "$dest/SHA256SUMS"
  (cd "$root" && git rev-parse HEAD) > "$dest/SOURCE_COMMIT"
  echo "synced $(wc -l < "$dest/SHA256SUMS") files from $(cat "$dest/SOURCE_COMMIT")"
  exit 0
fi
(cd "$dest/Runtime" && sha256sum -c --quiet "$dest/SHA256SUMS")
(cd "$src" && sha256sum *.cs) | cmp -s - "$dest/SHA256SUMS" || { echo "src/Sv changed since sync; rerun sync" >&2; exit 1; }
extra=$(cd "$dest/Runtime" && ls *.cs | sort | comm -23 - "$(cd "$src" && ls *.cs | sort > "$dest/.list" && echo "$dest/.list")")
rm -f "$dest/.list"
[ -z "$extra" ] || { echo "files not in src/Sv: $extra" >&2; exit 1; }
echo "com.sv.core matches src/Sv"
