U="C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe"
P=D:/GitHub/_work/sv-proto/c-harness
O=D:/GitHub/_work/sv-proto/p2-out/bench
mkdir -p "$O"
rm -rf "$P/Assets/SvBench"; mkdir -p "$P/Assets/SvBench"
cp D:/GitHub/_work/sv-proto/p2-unity/tests/Sv.Editor/bench/*.cs D:/GitHub/_work/sv-proto/p2-unity/tests/Sv.Editor/bench/*.asmdef "$P/Assets/SvBench/"
bash D:/GitHub/_work/sv-proto/p2-unity/eng/unity-sync.sh sync "$P/Packages/com.sv.core" >/dev/null
"$U" -batchmode -nographics -quit -projectPath "$P" -job-worker-count 11 -logFile "$O/compile.log" -executeMethod Sv.Unity.TlTests.SvTlBench.Compile
echo "compile exit=$? errorCS=$(grep -c 'error CS' "$O/compile.log") warningCS=$(grep -c 'warning CS' "$O/compile.log") errorBC=$(grep -c 'error BC' "$O/compile.log")"
for w in ${WORKERS:-11 7 4 2}; do for rep in 1 2 3; do
  t=tl-w$w-$rep
  "$U" -batchmode -nographics -quit -projectPath "$P" -job-worker-count $w -logFile "$O/$t.log" -executeMethod Sv.Unity.TlTests.SvTlBench.Run -svOut "$O/$t.txt" -svTag "$t"
  echo "$t exit=$? $(grep -c '^cell' "$O/$t.txt") $(date +%T)"
done; done
if [ -n "$STANDIN" ]; then for rep in 1 2 3; do
  t=standin-$rep
  "$U" -batchmode -nographics -quit -projectPath "$P" -job-worker-count 11 -logFile "$O/$t.log" -executeMethod SvBench.SvBenchRun.Run -svOut "$O/$t.txt" -svTag "$t" -svShapes "$STANDIN"
  echo "$t exit=$? $(grep -c '^cell' "$O/$t.txt") $(date +%T)"
done; fi
