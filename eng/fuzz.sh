#!/usr/bin/env bash
set -u
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

first=${1:?usage: eng/fuzz.sh <first-offset> <rounds> [default|noavx2|scalar|contended]}
rounds=${2:?usage: eng/fuzz.sh <first-offset> <rounds> [default|noavx2|scalar|contended]}
mode=${3:-default}
step=${FUZZ_STEP:-10000}
out=${FUZZ_OUT:-$root/fuzz-out}

case $mode in
  default) flags=(); prefix=();;
  noavx2) flags=(DOTNET_EnableAVX2=0); prefix=();;
  scalar) flags=(DOTNET_EnableHWIntrinsic=0); prefix=();;
  contended) flags=(); prefix=(taskset -c 0,1);;
  *) echo "unknown mode $mode" >&2; exit 2;;
esac

if [ -d "$HOME/sv-proto/_work/bench.lock" ]; then
  echo "fuzz: a benchmark holds $HOME/sv-proto/_work/bench.lock; wait until it is gone" >&2
  exit 3
fi

dotnet build tests/Sv.Diff.Tests -c Release -v q >/dev/null || { echo "fuzz: build failed" >&2; exit 1; }
receipts=tests/Sv.Diff.Tests/bin/Release/net10.0/receipts
mkdir -p "$out"
found=0
for ((round = 0; round < rounds; round++)); do
  offset=$((first + round * step))
  log="$out/offset-$offset-$mode.log"
  env "${flags[@]}" SV_FUZZ_OFFSET=$offset "${prefix[@]}" dotnet test tests/Sv.Diff.Tests -c Release --no-build --logger "console;verbosity=detailed" > "$log" 2>&1
  status=$?
  summary=$(grep -E "^\s+(Passed|Failed): [0-9]+" "$log" | tr -s ' ' | tr '\n' ' ')
  if [ $status -ne 0 ]; then
    found=1
    mkdir -p "$out/offset-$offset-$mode"
    if [ -d "$receipts" ]; then
      cp -r "$receipts"/. "$out/offset-$offset-$mode/"
    fi
    echo "FAIL offset $offset ($mode): $summary -> $out/offset-$offset-$mode"
    grep -E "seeds failed|failed;|first:|seed [0-9]+:" "$log" | head -5
  else
    echo "ok   offset $offset ($mode): $summary"
  fi
done
exit $found
