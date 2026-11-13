U="C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Unity.exe"
R=${R:-D:/GitHub/_work/sv-proto/m8-unity}
P=${P:-D:/GitHub/_work/sv-proto/m8-harness}
O=${O:-D:/GitHub/_work/sv-proto/m8-out}
T=${T:-gate}
MODES=${MODES:-EditMode}
mkdir -p "$O"
bash "$R/eng/engine-sync.sh" check || exit 1
"$U" -batchmode -nographics -quit -projectPath "$P" -logFile "$O/$T-compile.log"
echo "compile exit=$? errorCS=$(grep -c 'error CS' $O/$T-compile.log) warningCS=$(grep -c 'warning CS' $O/$T-compile.log) errorBC=$(grep -c 'error BC' $O/$T-compile.log)"
[ -n "$COMPILE_ONLY" ] && exit 0
for m in $MODES; do
"$U" -batchmode -nographics -projectPath "$P" -runTests -testPlatform $m -testResults "$O/$T-$m.xml" -logFile "$O/$T-$m.log" ${FILTER:+-testFilter $FILTER}
echo "$m exit=$? $(grep -o '<test-run [^>]*' $O/$T-$m.xml | grep -o 'total="[0-9]*"\|passed="[0-9]*"\|failed="[0-9]*"' | tr '\n' ' ') errorCS=$(grep -c 'error CS' $O/$T-$m.log) errorBC=$(grep -c 'error BC' $O/$T-$m.log)"
done
