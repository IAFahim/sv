#!/usr/bin/env sh
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"
dotnet build sv.slnx -c Release -p:NuGetAudit=false -v q >/dev/null
digests='FullyQualifiedName~CrossHostDigest|FullyQualifiedName~BurstDigestTests'
leg() {
  name=$1
  shift
  echo "== leg $name: flags"
  env "$@" SV_ISA_LEG="$name" dotnet test tests/Sv.Tests -c Release --no-build -p:NuGetAudit=false --filter 'FullyQualifiedName~IsaLegTests'
  echo "== leg $name: pinned digests A2ACAA69E55F79D8 2B43BC1549AAE751"
  env "$@" SV_ISA_LEG="$name" dotnet test tests/Sv.Tests -c Release --no-build -p:NuGetAudit=false --filter "$digests"
}
leg default
leg noavx2 DOTNET_EnableAVX2=0
leg scalar DOTNET_EnableHWIntrinsic=0
echo "== leg ns2.1: flags"
dotnet test tests/Sv.Portable.Tests -c Release --no-build -p:NuGetAudit=false --filter 'FullyQualifiedName~PortableLegTests'
echo "== leg ns2.1: pinned digests A2ACAA69E55F79D8 2B43BC1549AAE751"
dotnet test tests/Sv.Portable.Tests -c Release --no-build -p:NuGetAudit=false --filter "$digests"
echo "all ISA legs green"
