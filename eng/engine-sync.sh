#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C  # byte order, so SHA256SUMS is identical on every machine
root=$(cd "$(dirname "$0")/.." && pwd)
mode=${1:-}
dest=${2:-$root/packages/com.sv.engine}
if [ "$mode" != "sync" ] && [ "$mode" != "check" ]; then
  echo "usage: eng/engine-sync.sh sync|check [packages/com.sv.engine]" >&2
  exit 2
fi

sources() {
  (cd "$root/src/Sv.Core" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' | sed 's#^\./##' | sort | sed 's#^#Runtime/Core/#')
  (cd "$root/src/Sv.Host" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' -not -name 'HostPack.Plan.cs' -not -name 'ColumnRunner.cs' | sed 's#^\./##' | sort | sed 's#^#Runtime/Host/#')
  (cd "$root/src/Sv.Rules" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' | sed 's#^\./##' | sort | sed 's#^#Editor/Rules/#')
}

origin() {
  case "$1" in
    Runtime/Core/*) echo "$root/src/Sv.Core/${1#Runtime/Core/}" ;;
    Runtime/Host/*) echo "$root/src/Sv.Host/${1#Runtime/Host/}" ;;
    Editor/Rules/*) echo "$root/src/Sv.Rules/${1#Editor/Rules/}" ;;
  esac
}

sums() {
  while read -r file; do
    printf '%s  %s\n' "$(tr -d '\r' < "$(origin "$file")" | sha256sum | cut -d' ' -f1)" "$file"
  done
}

asmdef() {
  local name=$1 references=$2 platforms=$3 engine=$4
  cat <<JSON
{
    "name": "$name",
    "rootNamespace": "Sv",
    "references": [$references],
    "includePlatforms": [$platforms],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": $engine
}
JSON
}

if [ "$mode" = "sync" ]; then
  mkdir -p "$dest"
  wanted=$(sources)
  for area in Runtime/Core Runtime/Host Editor/Rules; do
    if [ -d "$dest/$area" ]; then
      (cd "$dest" && find "$area" -name '*.cs') | sort | while read -r present; do
        if ! printf '%s\n' "$wanted" | grep -qxF "$present"; then
          rm -f "$dest/$present" "$dest/$present.meta"
        fi
      done
    fi
  done
  printf '%s\n' "$wanted" | while read -r file; do
    mkdir -p "$(dirname "$dest/$file")"
    tr -d '\r' < "$(origin "$file")" > "$dest/$file"
  done
  asmdef Sv.Engine.Core "" "" true > "$dest/Runtime/Core/Sv.Engine.Core.asmdef"
  asmdef Sv.Engine.Host '"Sv.Engine.Core"' "" true > "$dest/Runtime/Host/Sv.Engine.Host.asmdef"
  asmdef Sv.Engine.Rules '"Sv.Engine.Core"' '"Editor"' false > "$dest/Editor/Rules/Sv.Engine.Rules.asmdef"
  for area in Runtime/Core Runtime/Host Editor/Rules; do
    printf '%s\n' '-nullable:enable' > "$dest/$area/csc.rsp"
  done
  cat > "$dest/package.json" <<'JSON'
{
    "name": "com.sv.engine",
    "version": "3.0.0",
    "displayName": "sv engine",
    "description": "sv v3 engine for Unity 7: Sv.Core and Sv.Host (runtime) and Sv.Rules (editor), synced verbatim by eng/engine-sync.sh",
    "unity": "7000.0"
}
JSON
  printf '%s\n' "$wanted" | sums > "$dest/SHA256SUMS"
  (cd "$root" && git rev-parse HEAD) > "$dest/SOURCE_COMMIT"
  echo "synced $(wc -l < "$dest/SHA256SUMS") files into $dest"
  exit 0
fi

wanted=$(sources)
if ! printf '%s\n' "$wanted" | sums | cmp -s - "$dest/SHA256SUMS"; then
  echo "engine sources changed since the last sync; run eng/engine-sync.sh sync" >&2
  exit 1
fi
while read -r sum file; do
  if [ ! -f "$dest/$file" ] || [ "$(tr -d '\r' < "$dest/$file" | sha256sum | cut -d' ' -f1)" != "$sum" ]; then
    echo "package file differs from its source: $file" >&2
    exit 1
  fi
done < "$dest/SHA256SUMS"
extra=$( (cd "$dest" && find Runtime Editor -name '*.cs' | sort) | comm -23 - <(printf '%s\n' "$wanted" | sort))
if [ -n "$extra" ]; then
  echo "package files with no source: $extra" >&2
  exit 1
fi
echo "com.sv.engine matches src ($(wc -l < "$dest/SHA256SUMS") files)"
