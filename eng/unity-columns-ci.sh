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
probe="${SV_COLUMNS_PROBE:-${XDG_CACHE_HOME:-$HOME/.cache}/sv-columns-probe-$version}"
out="${SV_UNITY_OUT:-$root/artifacts/sv-columns-unity}"
steps="${SV_UNITY_STEPS:-compile EditMode bench}"
echo "unity $unity ($version), probe $probe, steps: $steps"
bash "$root/eng/columns-sync.sh" check || exit 1
if pgrep -af 'Unity' | grep -F -- "-projectPath $probe" >/dev/null; then echo "an Editor already has $probe open" >&2; exit 1; fi
mkdir -p "$out"
if [ ! -f "$probe/ProjectSettings/ProjectVersion.txt" ]; then
  rm -rf "$probe"
  "$unity" -batchmode -nographics -quit -createProject "$probe" -logFile "$out/create.log" || { echo "project creation failed: $out/create.log" >&2; exit 1; }
fi
cat > "$probe/Packages/manifest.json" <<JSON
{
  "dependencies": {
    "com.sv.columns": "file:$root/packages/com.sv.columns",
    "com.sv.columns.entities": "file:$root/packages/com.sv.columns.entities",
    "com.unity.entities": "7.0.0",
    "com.unity.test-framework": "1.6.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.imgui": "1.0.0"
  },
  "testables": ["com.sv.columns", "com.sv.columns.entities"]
}
JSON
rm -rf "$probe/Assets/SvColumnsBench" "$probe/Assets/SvColumnsArena"
cp -r "$root/packages/com.sv.columns.entities/Samples~/Arena" "$probe/Assets/SvColumnsArena"
mkdir -p "$probe/Assets/SvColumnsBench/Editor"
cp "$root/eng/unity-columns/SvColumnsBench.cs" "$root/benchmarks/Sv.Columns.Bench/BenchPack.cs" "$probe/Assets/SvColumnsBench/Editor/"
cat > "$probe/Assets/SvColumnsBench/Editor/SvColumnsBench.Editor.asmdef" <<'JSON'
{
  "name": "SvColumnsBench.Editor",
  "references": ["Sv.Columns", "Sv.Columns.Unity", "Sv.Columns.Entities", "Unity.Burst"],
  "includePlatforms": ["Editor"],
  "allowUnsafeCode": true,
  "autoReferenced": false
}
JSON
printf '%s\n' '-nullable:enable' > "$probe/Assets/SvColumnsBench/Editor/csc.rsp"
status=0
counts() { echo "errorCS=$(grep -c 'error CS' "$1" 2>/dev/null) warningCS=$(grep -c 'warning CS' "$1" 2>/dev/null) errorBC=$(grep -c 'error BC' "$1" 2>/dev/null)"; }
burst_gate() {
  if [ "$(grep -c 'error BC' "$1" 2>/dev/null)" -ne 0 ]; then
    status=1; echo "Burst could not compile a job:" >&2; grep -E 'error BC' "$1" | sort -u | head -20 >&2
  fi
}
for step in $steps; do
  case "$step" in
    compile)
      log="$out/compile.log"
      "$unity" -batchmode -nographics -quit -projectPath "$probe" -logFile "$log"
      code=$?
      echo "compile exit=$code $(counts "$log")"
      grep -E 'warning CS' "$log" | grep -F 'com.sv.columns' | sort -u | head -20
      grep -E 'warning CS' "$log" | grep -F 'SvColumnsArena' | sort -u | head -20
      burst_gate "$log"
      rsp="$(find "$probe/Library/Bee" -name 'Sv.Columns.rsp' 2>/dev/null | head -1)"
      if [ -n "$rsp" ]; then
        echo "Sv.Columns csc defines: $(grep -o -- '-define:[A-Za-z0-9_]*' "$rsp" | sed 's/-define://' | sort -u | tr '\n' ' ')"
        echo "Sv.Columns csc references: $(grep -c -- '-r:' "$rsp"), for example:"
        grep -o -- '-r:"[^"]*"' "$rsp" | grep -iE 'netstandard|System.Runtime|System.Private|mscorlib' | head -6
      fi
      if [ $code -ne 0 ] || [ "$(grep -c 'error CS' "$log")" -ne 0 ]; then
        status=1; grep -E 'error CS' "$log" | sort -u | head -20 >&2
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
      grep -E '^SVCOLUMNS' "$log" | sort -u
      burst_gate "$log"
      if [ $code -ne 0 ] || [ "$total" -eq 0 ] || [ "$failed" -ne 0 ]; then
        status=1
        grep -E 'error CS|Failed|Exception|Expected|But was' "$log" | sort -u | head -40 >&2
        grep -n -A30 -E '^[A-Za-z.]*Exception: ' "$log" | head -150 >&2
        python3 - "$xml" >&2 <<'PY' || true
import sys, xml.etree.ElementTree as ET
for case in ET.parse(sys.argv[1]).iter("test-case"):
    if case.get("result") == "Failed":
        message = case.find("failure/message")
        print("FAILED", case.get("fullname"), (message.text or "").strip()[:400] if message is not None else "")
PY
      fi
      ;;
    bench)
      log="$out/bench.log"
      "$unity" -batchmode -nographics -projectPath "$probe" -executeMethod SvColumnsBench.Run -logFile "$log" \
        -svColumnsShapes "${SV_COLUMNS_SHAPES:-ops:20000x500,ops:1000000x500,records:20000x500,records:1000000x10,mixed:1000000x500+10}" -svColumnsFrames "${SV_COLUMNS_FRAMES:-24}" -svColumnsKernels "${SV_COLUMNS_KERNELS:-best,scalar,burst,burstscalar}"
      code=$?
      echo "bench exit=$code"
      grep -E '^SVCOLUMNS' "$log" | sort -u
      burst_gate "$log"
      if [ $code -ne 0 ] || grep -q 'SVCOLUMNS FAIL' "$log" || ! grep -q 'SVCOLUMNS done failures 0' "$log"; then status=1; fi
      ;;
    *) echo "unknown step $step" >&2; status=1 ;;
  esac
done
metas=$(cd "$root" && git ls-files --others --exclude-standard -- 'packages/com.sv.columns/*.meta' 'packages/com.sv.columns/**/*.meta' 'packages/com.sv.columns.entities/*.meta' 'packages/com.sv.columns.entities/**/*.meta')
if [ -n "$metas" ]; then
  mkdir -p "$out/metas"
  (cd "$root" && printf '%s\n' $metas | while read -r meta; do mkdir -p "$out/metas/$(dirname "$meta")"; cp "$meta" "$out/metas/$meta"; done)
  echo "Unity wrote .meta files that the repository lacks; commit them (copies under $out/metas):"
  printf '  %s\n' $metas
  (cd "$root" && for meta in $metas; do echo "--- $meta"; cat "$meta"; echo "--- end"; done)
  status=1
fi
exit $status
