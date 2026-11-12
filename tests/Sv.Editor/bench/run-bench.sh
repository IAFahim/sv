#!/usr/bin/env bash
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
P=${SV_PROJECT:-D:/GitHub/_work/sv-proto/c-harness}
O=${SV_OUT:-D:/GitHub/_work/sv-proto/c-out/bench}
U="${UNITY:-C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe}"
REPS=${REPS:-3}
SHAPES=${SHAPES:-P1,P2,P3}
mkdir -p "$O" "$P/Assets/SvBench"
cp "$ROOT"/*.cs "$ROOT"/*.asmdef "$P/Assets/SvBench/"
for rep in $(seq 1 $REPS); do
  t=bench-$rep
  "$U" -batchmode -nographics -quit -projectPath "$P" -job-worker-count 11 -logFile "$O/$t-compile.log" -executeMethod SvBench.SvBenchRun.Compile
  echo "$t compile exit=$? errorCS=$(grep -c 'error CS' "$O/$t-compile.log") warningCS=$(grep -c 'warning CS' "$O/$t-compile.log") errorBC=$(grep -c 'error BC' "$O/$t-compile.log")"
  "$U" -batchmode -nographics -quit -projectPath "$P" -job-worker-count 11 -logFile "$O/$t.log" -executeMethod SvBench.SvBenchRun.Run -svOut "$O/$t.txt" -svTag "$t" -svShapes "$SHAPES" $EXTRA
  echo "$t run exit=$? cells=$(grep -c '^cell' "$O/$t.txt") $(date +%T)"
done
