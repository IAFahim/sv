#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C
root=$(cd "$(dirname "$0")/.." && pwd)
mode=${1:-}
dest=${2:-$root/packages/com.sv.columns}
if [ "$mode" != "sync" ] && [ "$mode" != "check" ]; then
  echo "usage: eng/columns-sync.sh sync|check [packages/com.sv.columns]" >&2
  exit 2
fi

sources() {
  (cd "$root/src/Sv.Columns" && find . -name '*.cs' -not -path './bin/*' -not -path './obj/*' | sed 's#^\./##' | sort | sed 's#^#Runtime/Columns/#')
  echo "Tests/Editor/Canon/Canon.cs"
  echo "Tests/Packs~/arena.json"
}

origin() {
  case "$1" in
    Runtime/Columns/*) echo "$root/src/Sv.Columns/${1#Runtime/Columns/}" ;;
    Tests/Editor/Canon/*) echo "$root/tests/Sv.Columns.Tests/Canon/${1#Tests/Editor/Canon/}" ;;
    Tests/Packs~/*) echo "$root/packs/columns/${1#Tests/Packs~/}" ;;
  esac
}

sums() {
  while read -r file; do
    printf '%s  %s\n' "$(tr -d '\r' < "$(origin "$file")" | sha256sum | cut -d' ' -f1)" "$file"
  done
}

mirrored() {
  (cd "$dest" && { find Runtime/Columns Tests/Editor/Canon -name '*.cs' 2>/dev/null || true; find 'Tests/Packs~' -type f 2>/dev/null || true; } | sort)
}

if [ "$mode" = "sync" ]; then
  wanted=$(sources)
  mkdir -p "$dest"
  mirrored | while read -r present; do
    if ! printf '%s\n' "$wanted" | grep -qxF "$present"; then
      rm -f "$dest/$present" "$dest/$present.meta"
    fi
  done
  printf '%s\n' "$wanted" | while read -r file; do
    mkdir -p "$(dirname "$dest/$file")"
    tr -d '\r' < "$(origin "$file")" > "$dest/$file"
  done
  cat > "$dest/Runtime/Columns/Sv.Columns.asmdef" <<'JSON'
{
    "name": "Sv.Columns",
    "rootNamespace": "Sv.Columns",
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
  printf '%s\n' '-nullable:enable' > "$dest/Runtime/Columns/csc.rsp"
  printf '%s\n' "$wanted" | sums > "$dest/SHA256SUMS"
  echo "synced $(wc -l < "$dest/SHA256SUMS") files into $dest"
  exit 0
fi

wanted=$(sources)
if ! printf '%s\n' "$wanted" | sums | cmp -s - "$dest/SHA256SUMS"; then
  echo "Sv.Columns sources changed since the last sync; run eng/columns-sync.sh sync" >&2
  exit 1
fi
while read -r sum file; do
  if [ ! -f "$dest/$file" ] || [ "$(tr -d '\r' < "$dest/$file" | sha256sum | cut -d' ' -f1)" != "$sum" ]; then
    echo "package file differs from its source: $file" >&2
    exit 1
  fi
done < "$dest/SHA256SUMS"
extra=$(mirrored | comm -23 - <(printf '%s\n' "$wanted" | sort))
if [ -n "$extra" ]; then
  echo "package files with no source: $extra" >&2
  exit 1
fi
echo "com.sv.columns matches src ($(wc -l < "$dest/SHA256SUMS") files)"
