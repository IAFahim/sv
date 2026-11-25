#!/usr/bin/env bash
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
newest_editor() {
  if command -v unity >/dev/null 2>&1 || [ -x "$HOME/.local/bin/unity" ]; then
    local cli; cli="$(command -v unity || echo "$HOME/.local/bin/unity")"
    "$cli" --no-banner editors --installed --json 2>/dev/null | python3 -c '
import json, re, sys
try:
    rows = json.load(sys.stdin)["data"]
except Exception:
    sys.exit(0)
rows = rows.get("items", []) if isinstance(rows, dict) else rows
key = lambda v: [(0, int(p)) if p.isdigit() else (1, p) for p in re.findall(r"\d+|[A-Za-z]+", v)]
rows = [r for r in rows if isinstance(r, dict) and str(r.get("version", "")).startswith("7")]
if rows:
    print(sorted(rows, key=lambda r: key(r["version"]))[-1]["location"])
'
  fi
}
unity="${UNITY:-$(newest_editor)}"
if [ -z "$unity" ] || [ ! -x "$unity" ]; then
  unity="$(ls -d "$HOME"/Unity/Hub/Editor/7*/Editor/Unity 2>/dev/null | sort -V | tail -1)"
fi
[ -n "$unity" ] && [ -x "$unity" ] || { echo "no Unity 7 editor found; set UNITY" >&2; exit 1; }
version="$(basename "$(dirname "$(dirname "$unity")")")"
probe="${SV_UNITY_PROBE:-${XDG_CACHE_HOME:-$HOME/.cache}/sv-unity-probe-$version}"
out="${SV_UNITY_OUT:-$root/artifacts/sv-unity}"
steps="${SV_UNITY_STEPS:-compile EditMode PlayMode bench}"
echo "unity $unity ($version), probe $probe, steps: $steps"
bash "$root/eng/engine-sync.sh" check || exit 1
if pgrep -af 'Unity' | grep -F -- "-projectPath $probe" >/dev/null; then echo "an Editor already has $probe open" >&2; exit 1; fi
mkdir -p "$out"
if [ ! -f "$probe/ProjectSettings/ProjectVersion.txt" ]; then
  rm -rf "$probe"
  "$unity" -batchmode -nographics -quit -createProject "$probe" -logFile "$out/create.log" || { echo "project creation failed: $out/create.log" >&2; exit 1; }
fi
tl_package=""
if [ -n "${TL_UNITY_ROOT:-}" ] && [ -d "$TL_UNITY_ROOT/com.tl.unity" ]; then
  tl_package=$'\n'"    \"com.tl.unity\": \"file:$TL_UNITY_ROOT/com.tl.unity\","
fi
cat > "$probe/Packages/manifest.json" <<JSON
{
  "dependencies": {
    "com.sv.engine": "file:$root/packages/com.sv.engine",
    "com.sv.unity.v4": "file:$root/packages/com.sv.unity.v4",${tl_package}
    "com.unity.entities": "7.0.0",
    "com.unity.burst": "2.0.0",
    "com.unity.collections": "7.0.0",
    "com.unity.test-framework": "1.6.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0"
  },
  "testables": ["com.sv.unity.v4"]
}
JSON
rm -rf "$probe/Assets/SvBench"
mkdir -p "$probe/Assets/SvBench/Editor"
cp "$root/eng/unity-bench/SvUnityBench.cs" "$probe/Assets/SvBench/Editor/"
cat > "$probe/Assets/SvBench/Editor/SvBench.Editor.asmdef" <<'JSON'
{
  "name": "SvBench.Editor",
  "references": ["Sv.Engine.Core", "Sv.Engine.Host", "Sv.Unity.V4", "Sv.Unity.V4.TestPacks", "Unity.Burst", "Unity.Collections"],
  "includePlatforms": ["Editor"],
  "allowUnsafeCode": true,
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"]
}
JSON
cp "$root/packages/com.sv.unity.v4/Runtime/csc.rsp" "$probe/Assets/SvBench/Editor/csc.rsp"
status=0
counts() { echo "errorCS=$(grep -c 'error CS' "$1" 2>/dev/null) warningCS=$(grep -c 'warning CS' "$1" 2>/dev/null) errorBC=$(grep -c 'error BC' "$1" 2>/dev/null)"; }
for step in $steps; do
  case "$step" in
    compile)
      log="$out/compile.log"
      "$unity" -batchmode -nographics -quit -projectPath "$probe" -logFile "$log"
      code=$?
      echo "compile exit=$code $(counts "$log")"
      if [ $code -ne 0 ] || [ "$(grep -c 'error CS' "$log")" -ne 0 ] || [ "$(grep -c 'error BC' "$log")" -ne 0 ]; then
        status=1; grep -E 'error CS|error BC' "$log" | sort -u | head -20 >&2
      fi
      ;;
    EditMode|PlayMode)
      xml="$out/$step.xml"; log="$out/$step.log"; rm -f "$xml"
      "$unity" -batchmode -nographics -projectPath "$probe" -runTests -testPlatform "$step" -testResults "$xml" -logFile "$log"
      code=$?
      run="$(grep -m1 -o '<test-run [^>]*>' "$xml" 2>/dev/null || true)"
      attr() { local value; value="$(printf '%s' "$run" | sed -n "s/.* $1=\"\([0-9]*\)\".*/\1/p")"; echo "${value:-0}"; }
      total="$(attr total)"; passed="$(attr passed)"; failed="$(attr failed)"
      echo "$step exit=$code total=$total passed=$passed failed=$failed $(counts "$log")"
      if [ $code -ne 0 ] || [ "$total" -eq 0 ] || [ "$failed" -ne 0 ]; then
        status=1; grep -E 'error CS|Failed|Exception' "$log" | sort -u | head -20 >&2
      fi
      ;;
    bench)
      log="$out/bench.log"
      "$unity" -batchmode -nographics -projectPath "$probe" -executeMethod SvUnityBench.Run -logFile "$log" \
        -svBenchShapes "${SV_BENCH_SHAPES:-1000000x1,1000000x4,4000000x1,20000x200,20000x500}" -svBenchFrames "${SV_BENCH_FRAMES:-32}"
      code=$?
      echo "bench exit=$code"
      grep -E '^SVBENCH' "$log" | sort -u
      if [ $code -ne 0 ] || grep -q 'SVBENCH FAIL' "$log" || ! grep -q 'SVBENCH done failures 0' "$log"; then status=1; fi
      ;;
    *) echo "unknown step $step" >&2; status=1 ;;
  esac
done
metas=$(cd "$root" && git ls-files --others --exclude-standard -- 'packages/*.meta')
if [ -n "$metas" ]; then
  mkdir -p "$out/metas"
  (cd "$root" && printf '%s\n' $metas | while read -r meta; do mkdir -p "$out/metas/$(dirname "$meta")"; cp "$meta" "$out/metas/$meta"; done)
  echo "Unity wrote .meta files that the repository lacks; commit them (copies under $out/metas):"
  printf '  %s\n' $metas
  status=1
fi
exit $status
