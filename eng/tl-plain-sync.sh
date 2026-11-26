#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C
root="$(cd "$(dirname "$0")/.." && pwd)"
source_root="${TL_UNITY_ROOT:-$root/../tl.unity}"
source_dll="$source_root/com.tl.unity/Tests/Plain/Generator/Tl.Gen.Plain.dll"
source_stamp="$source_root/com.tl.unity/Tools~/generators.stamp"
dll="$root/packages/com.sv.unity.v4/Tests/Tl/Generator/Tl.Gen.Plain.dll"
stamp="$root/eng/tl-plain.stamp"

stamp_of() {
  printf 'from=com.tl.unity/Tests/Plain/Generator/Tl.Gen.Plain.dll\n'
  grep '^tl-kinds=' "$1"
  grep '^Tl.Gen.Plain.dll=' "$1"
  printf 'sha256=%s\n' "$(sha256sum "$2" | cut -d' ' -f1)"
  printf 'tl.unity=%s\n' "$3"
}

case "${1:-check}" in
  sync)
    [ -f "$source_dll" ] || { echo "no $source_dll: set TL_UNITY_ROOT to a tl.unity checkout" >&2; exit 1; }
    commit="$(git -C "$source_root" rev-parse HEAD 2>/dev/null)" || { echo "$source_root is not a git checkout of tl.unity" >&2; exit 1; }
    [ -z "$(git -C "$source_root" status --porcelain -- com.tl.unity)" ] || { echo "$source_root has uncommitted changes under com.tl.unity: commit them so the pin can be fetched" >&2; exit 1; }
    mkdir -p "$(dirname "$dll")"
    cp "$source_dll" "$dll"
    stamp_of "$source_stamp" "$dll" "$commit" >"$stamp"
    echo "Tl.Gen.Plain.dll synced: $(grep '^tl-kinds=' "$stamp"), tl.unity $commit"
    ;;
  check)
    [ -f "$stamp" ] && [ -f "$dll" ] || { echo "Tl.Gen.Plain.dll or eng/tl-plain.stamp missing: run eng/tl-plain-sync.sh sync" >&2; exit 1; }
    [ "$(grep '^sha256=' "$stamp" | cut -d= -f2)" = "$(sha256sum "$dll" | cut -d' ' -f1)" ] || { echo "Tl.Gen.Plain.dll differs from eng/tl-plain.stamp" >&2; exit 1; }
    if [ -f "$source_dll" ] && ! cmp -s "$source_dll" "$dll"; then
      echo "Tl.Gen.Plain.dll differs from $source_dll: run eng/tl-plain-sync.sh sync" >&2
      exit 1
    fi
    echo "Tl.Gen.Plain.dll matches $(grep '^tl-kinds=' "$stamp")"
    ;;
  *)
    echo "usage: eng/tl-plain-sync.sh sync|check" >&2
    exit 2
    ;;
esac
