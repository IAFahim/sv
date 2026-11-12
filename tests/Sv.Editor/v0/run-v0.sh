#!/usr/bin/env bash
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
P=${V0_PROJECT:-D:/GitHub/_work/sv-proto/c-harness}
O=${V0_OUT:-D:/GitHub/_work/sv-proto/c-out}
U="${UNITY:-C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe}"
TAG=${1:-run}
PROBES=${2:-h,e,c,d,g}
mkdir -p "$O" "$P/Assets/SvV0"
cp "$ROOT"/StatFoldSets.cs "$ROOT"/StatFoldHost.cs "$ROOT"/unity/*.cs "$ROOT"/unity/*.asmdef "$P/Assets/SvV0/"
"$U" -batchmode -nographics -quit -projectPath "$P" -job-worker-count 11 -logFile "$O/$TAG-compile.log" -executeMethod SvV0.V0Batch.Compile
echo "compile exit=$? errorCS=$(grep -c 'error CS' "$O/$TAG-compile.log") warningCS=$(grep -c 'warning CS' "$O/$TAG-compile.log") errorBC=$(grep -c 'error BC' "$O/$TAG-compile.log")"
"$U" -batchmode -nographics -quit -projectPath "$P" -job-worker-count 11 -logFile "$O/$TAG.log" -executeMethod SvV0.V0Batch.Run -v0Out "$O/$TAG.txt" -v0Tag "$TAG" -v0Probes "$PROBES" -v0Dir "$O"
echo "run exit=$? errorCS=$(grep -c 'error CS' "$O/$TAG.log") errorBC=$(grep -c 'error BC' "$O/$TAG.log")"
