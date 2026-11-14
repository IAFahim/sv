#!/usr/bin/env bash
set -u
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"
export TL_ROOT=${TL_ROOT:-$root/../tl}
failed=0
note() { echo "== $*"; }
run() {
  local label=$1
  shift
  local out
  out=$("$@" 2>&1)
  local status=$?
  local line
  line=$(printf '%s\n' "$out" | grep -E "Passed!|Failed!" | tail -1 | sed -E 's/ - [A-Za-z0-9.]+\.dll.*//')
  if [ $status -ne 0 ] || [ -z "$line" ]; then
    failed=1
    echo "FAIL $label"
    printf '%s\n' "$out" | grep -E "error|Failed |Assert|Expected|Actual" | head -12
  else
    echo "ok   $label: $line"
  fi
}

note "mirrors"
bash eng/engine-sync.sh check || failed=1
bash eng/tl-plain-sync.sh check || failed=1

note "build"
dotnet build -c Release src/Sv.Pack -v q -p:NuGetAudit=false >/dev/null || { echo "FAIL build Sv.Pack"; exit 1; }

core="Sv.Laws.Tests Sv.Scenario.Tests Sv.Diff.Tests Sv.Lego.Tests Sv.Host.Tests"
extra="Sv.Twin.Fixtures Sv.NorthStar3 Sv.Web.Tests"
for leg in default noavx2 scalar; do
  case $leg in
    default) flags="";;
    noavx2) flags="DOTNET_EnableAVX2=0";;
    scalar) flags="DOTNET_EnableHWIntrinsic=0";;
  esac
  note "leg $leg"
  for project in $core; do
    run "$leg $project" env $flags dotnet test "tests/$project" -c Release -p:NuGetAudit=false
  done
done

note "default leg, integration suites (TL_ROOT=$TL_ROOT)"
for project in $extra; do
  run "default $project" dotnet test "tests/$project" -c Release -p:NuGetAudit=false
done

note "checked build (SV_CHECKED: the arena's canonical-order check)"
for project in $core; do
  run "checked $project" dotnet test "tests/$project" -c Release -p:NuGetAudit=false -p:SvChecked=true
done
dotnet build sv.slnx -c Release -v q -p:NuGetAudit=false >/dev/null

note "README chapter 1 sample"
if dotnet run --no-build -c Release --project samples/Sv.Hello | diff -u --strip-trailing-cr samples/Sv.Hello/expected.txt - >/dev/null; then
  echo "ok   samples/Sv.Hello prints samples/Sv.Hello/expected.txt"
else
  failed=1
  echo "FAIL samples/Sv.Hello output differs from samples/Sv.Hello/expected.txt"
fi

if [ $failed -ne 0 ]; then
  echo "v3-verify: FAILED"
  exit 1
fi
echo "v3-verify: all green"
