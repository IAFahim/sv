#!/usr/bin/env bash
set -u
root=$(cd "$(dirname "$0")/.." && pwd)
U=${U:-$HOME/Unity/Hub/Editor/7000.0.0a16/Editor/Unity}
P=${P:-$HOME/sv-proto/player-smoke}
SETTINGS=${SETTINGS:-$HOME/sv-proto/m8-harness/ProjectSettings}
KEEP=${KEEP:-0}
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) export SMOKE_TARGET=${SMOKE_TARGET:-win64}; exe=smoke.exe;;
  *) export SMOKE_TARGET=${SMOKE_TARGET:-linux64}; exe=smoke.x86_64;;
esac
export SMOKE_BACKEND=${SMOKE_BACKEND:-coreclr}

rm -rf "$P"
mkdir -p "$P/Assets/Smoke/Runtime" "$P/Assets/Editor" "$P/Assets/StreamingAssets/twin" "$P/Packages" "$P/out"
if [ -d "$SETTINGS" ]; then
  cp -r "$SETTINGS" "$P/"
fi

for pack in Reference Lego NorthStar3; do
  mkdir -p "$P/Assets/Smoke/Packs/$pack"
  cp "$root/packages/com.sv.unity.v4/Tests/Packs/$pack"/*.g.cs "$P/Assets/Smoke/Packs/$pack/"
done
(cd "$root/packages/com.sv.unity.v4/Tests/Editor/Twin" && find . -name "*.twin.txt" -exec cp --parents {} "$P/Assets/StreamingAssets/twin/" \;)
cp "$root/packages/com.sv.unity.v4/Runtime/csc.rsp" "$P/Assets/Smoke/Packs/csc.rsp"
cp "$root/packages/com.sv.unity.v4/Runtime/csc.rsp" "$P/Assets/Smoke/Runtime/csc.rsp"
cp "$root/eng/player-smoke/SmokeRunner.cs" "$P/Assets/Smoke/Runtime/"
cp "$root/eng/player-smoke/SmokeBuild.cs" "$P/Assets/Editor/"

if [ -n "${SV_GIT_REF:-}" ]; then
  engine_package="https://github.com/IAFahim/sv.git?path=/packages/com.sv.engine#$SV_GIT_REF"
  adapter_package="https://github.com/IAFahim/sv.git?path=/packages/com.sv.unity.v4#$SV_GIT_REF"
else
  engine_package="file:$root/packages/com.sv.engine"
  adapter_package="file:$root/packages/com.sv.unity.v4"
fi
cat > "$P/Packages/manifest.json" <<EOF
{
  "dependencies": {
    "com.sv.engine": "$engine_package",
    "com.sv.unity.v4": "$adapter_package",
    "com.unity.entities": "7.0.0",
    "com.unity.burst": "2.0.0",
    "com.unity.collections": "7.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0"
  }
}
EOF
asmdef() {
  printf '{\n  "name": "%s",\n  "references": [%s],\n  "includePlatforms": [%s],\n  "allowUnsafeCode": true,\n  "autoReferenced": %s\n}\n' "$2" "$3" "$4" "$5" > "$1"
}
engine='"Sv.Engine.Core", "Sv.Engine.Host", "Sv.Unity.V4", "Unity.Burst", "Unity.Collections"'
asmdef "$P/Assets/Smoke/Packs/SvSmoke.Packs.asmdef" SvSmoke.Packs "$engine" "" false
asmdef "$P/Assets/Smoke/Runtime/SvSmoke.Runtime.asmdef" SvSmoke.Runtime "\"SvSmoke.Packs\", $engine" "" true
asmdef "$P/Assets/Editor/SvSmoke.Editor.asmdef" SvSmoke.Editor "" '"Editor"' true

"$U" -batchmode -nographics -projectPath "$P" -executeMethod SmokeBuild.Build -logFile "$P/out/build.log"
built=$?
echo "packages: $engine_package target: $SMOKE_TARGET backend: $SMOKE_BACKEND"
echo "build exit=$built errorCS=$(grep -c 'error CS' "$P/out/build.log") warningCS=$(grep -c 'warning CS' "$P/out/build.log") errorBC=$(grep -c 'error BC' "$P/out/build.log")"
grep -E "SVSMOKE build" "$P/out/build.log"
status=1
if [ $built -eq 0 ] && [ -f "$P/Build/$exe" ]; then
  timeout 900 "$P/Build/$exe" -batchmode -nographics -logFile "$P/out/player.log"
  status=$?
  burst=$(find "$P/Build/smoke_Data/Plugins" -name 'lib_burst_generated.*' 2>/dev/null | head -1)
  il2cpp=$(find "$P/Build" -maxdepth 1 \( -name 'GameAssembly.*' -o -name 'GameAssembly' \) 2>/dev/null | head -1)
  echo "player exit=$status burst=$( [ -n "$burst" ] && echo aot || echo missing ) il2cpp=$( [ -n "$il2cpp" ] && echo yes || echo no )"
  grep -E "SVSMOKE (FAIL|summary)" "$P/out/player.log"
fi
if [ "$KEEP" != "1" ] && [ $status -eq 0 ]; then
  rm -rf "$P"
fi
exit $status
