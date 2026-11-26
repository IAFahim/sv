#!/usr/bin/env bash
set -u
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

usage="usage: eng/columns-fuzz.sh <first-seed> <seeds> [default|noavx2|scalar|all]"
first=${1:?$usage}
seeds=${2:?$usage}
mode=${3:-all}

case $mode in
  all) legs="default noavx2 scalar";;
  default|noavx2|scalar) legs=$mode;;
  *) echo "$usage" >&2; exit 2;;
esac

if [ -d "$HOME/sv-proto/_work/bench.lock" ]; then
  echo "columns-fuzz: a benchmark holds $HOME/sv-proto/_work/bench.lock; wait until it is gone" >&2
  exit 3
fi

dotnet build tests/Sv.Columns.Tests -c Release -v q -p:NuGetAudit=false >/dev/null || { echo "columns-fuzz: build failed" >&2; exit 1; }
failed=0
for leg in $legs; do
  case $leg in
    default) flags=();;
    noavx2) flags=(DOTNET_EnableAVX2=0);;
    scalar) flags=(DOTNET_EnableHWIntrinsic=0);;
  esac
  out=$(env "${flags[@]}" SV_COLUMNS_FUZZ_OFFSET="$first" SV_COLUMNS_FUZZ_ROUNDS="$seeds" \
    dotnet test tests/Sv.Columns.Tests -c Release --no-build -p:NuGetAudit=false --filter "FullyQualifiedName~DeepFuzzTests" 2>&1)
  line=$(printf '%s\n' "$out" | grep -E "Passed!|Failed!" | tail -1 | sed -E 's/ - [A-Za-z0-9.]+\.dll.*//')
  if printf '%s\n' "$line" | grep -q "Passed!"; then
    echo "ok   $leg seeds $first..$((first + seeds - 1)): $line"
  else
    failed=1
    echo "FAIL $leg seeds $first..$((first + seeds - 1))"
    printf '%s\n' "$out" | grep -E "seed [0-9]+|frame [0-9]+|Exception :" | head -8
  fi
done
exit $failed
