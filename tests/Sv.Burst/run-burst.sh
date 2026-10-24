#!/usr/bin/env bash
# Atom 6.2 real-Burst leg: copies the shared Sv.Burst sources into the minimal Unity 6.7
# project, runs the editor in batch mode (SvBurstBatch.Run), which compiles the kernels with
# the real Burst compiler at FloatMode.Strict, checks mono/burst digest identity and dumps the
# compiled code for the FMA scan. Output lands in tests/Sv.Burst/receipt/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="$HOME/Unity/Hub/Editor/6000.7.0b5/Editor/Unity"
SRC="$ROOT/src/Sv.Burst"
DST="$ROOT/tests/Sv.Burst/unity/Assets/Scripts/SvBurst"
RECEIPT="$ROOT/tests/Sv.Burst/receipt"

if [ ! -x "$UNITY" ]; then
    echo "unity editor not found at $UNITY" >&2
    exit 2
fi

mkdir -p "$DST" "$RECEIPT"
for f in Bcl.cs Kernels.cs Scenario.cs; do
    cp "$SRC/$f" "$DST/$f"
done

sha256sum "$SRC"/Bcl.cs "$SRC"/Kernels.cs "$SRC"/Scenario.cs > "$RECEIPT/source-hashes.txt"
sha256sum "$DST"/Bcl.cs "$DST"/Kernels.cs "$DST"/Scenario.cs >> "$RECEIPT/source-hashes.txt"

export SV_BURST_RECEIPT_DIR="$RECEIPT"
"$UNITY" -batchmode -nographics -quit \
    -projectPath "$ROOT/tests/Sv.Burst/unity" \
    -executeMethod Sv.Burst.SvBurstBatch.Run \
    -logFile "$RECEIPT/unity-batch.log"
