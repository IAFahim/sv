#!/usr/bin/env bash
# Atom 105 editor leg: drives the minimal Unity 6000.7.0b5 project in tests/Sv.Editor/unity.
# Phases:
#   probe   — copies src/Sv + packages/com.sv.unity sources VERBATIM (no shim assembly on the
#             compile path) and boots once in batch mode; the editor log captures the exact
#             compile errors that decide whether src/Sv compiles in-editor as-is.
#   measure — re-copies the sources, applies the recorded two-member BitConverter name
#             transform (SingleToUInt32Bits/UInt32BitsToSingle -> SvUnityBits.*, bit-identical
#             semantics via the profile's SingleToInt32Bits/Int32BitsToSingle), activates the
#             SvShim BCL shim assembly, and boots once: SvEditorBatch.Run then performs the
#             whole FF-1M measurement in-process (no domain reload across rounds).
# Output lands in tests/Sv.Editor/receipt/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="$HOME/Unity/Hub/Editor/6000.7.0b5/Editor/Unity"
PROJECT="$ROOT/tests/Sv.Editor/unity"
RECEIPT="$ROOT/tests/Sv.Editor/receipt"
CORE_SRC="$ROOT/src/Sv"
UNITY_SRC="$ROOT/packages/com.sv.unity"
CORE_DST="$PROJECT/Assets/SvCore"
UNITY_DST="$PROJECT/Assets/SvUnity"
SHIM_DIR="$PROJECT/Assets/SvShim"
SHIM_HOLD="$PROJECT/.svshim-hold"

PHASE="${1:-measure}"
if [ "$PHASE" != "probe" ] && [ "$PHASE" != "measure" ]; then
    echo "usage: run-editor.sh [probe|measure]" >&2
    exit 2
fi

if [ ! -x "$UNITY" ]; then
    echo "unity editor not found at $UNITY" >&2
    exit 2
fi

mkdir -p "$RECEIPT"

rm -rf "$CORE_DST" "$UNITY_DST"
mkdir -p "$CORE_DST" "$UNITY_DST"
cp "$CORE_SRC"/*.cs "$CORE_DST"/
cp "$UNITY_SRC"/SvChunk.cs "$UNITY_SRC"/SvRange.cs "$UNITY_SRC"/SvSlot.cs "$UNITY_SRC"/SvChanged.cs "$UNITY_SRC"/SvSystem.cs "$UNITY_SRC"/UnityPopulation.cs "$UNITY_DST"/
rm -f "$CORE_DST"/Sv.csproj

{
    echo "== pristine sources (src/Sv + packages/com.sv.unity) =="
    sha256sum "$CORE_SRC"/*.cs "$UNITY_SRC"/SvChunk.cs "$UNITY_SRC"/SvRange.cs "$UNITY_SRC"/SvSlot.cs "$UNITY_SRC"/SvChanged.cs "$UNITY_SRC"/SvSystem.cs "$UNITY_SRC"/UnityPopulation.cs
} > "$RECEIPT/source-hashes-$PHASE.txt"

if [ "$PHASE" = "probe" ]; then
    mv "$SHIM_DIR" "$SHIM_HOLD" 2>/dev/null || true
    cat > "$CORE_DST/SvCore.asmdef" <<'EOF'
{
    "name": "SvCore",
    "rootNamespace": "Sv",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
EOF
    cat > "$UNITY_DST/Sv.Unity.asmdef" <<'EOF'
{
    "name": "Sv.Unity",
    "rootNamespace": "Sv.Unity",
    "references": [
        "Unity.Entities",
        "Unity.Burst",
        "SvCore"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
EOF
    cat > "$PROJECT/Assets/Editor/SvEditor.Editor.asmdef" <<'EOF'
{
    "name": "SvEditor.Editor",
    "rootNamespace": "Sv.Editor",
    "references": [
        "SvCore",
        "Sv.Unity",
        "Unity.Entities",
        "Unity.Burst",
        "Unity.Collections"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
EOF
else
    rm -rf "$SHIM_HOLD"
    cp "$ROOT/tests/Sv.Editor/asmdefs/measure/SvShim.asmdef" "$SHIM_DIR/SvShim.asmdef"
    # Two recorded compile-surface transforms on the SvCore COPIES (Unity's scripting runtime
    # gates these .NET 7+ features; semantics preserved — see issue 0114):
    #   1. IStateCodec's `static abstract bool Reading` becomes an instance property (receiver
    #      `codec` is in scope at every TCodec.Reading call site), implementations lose `static`.
    #   2. `delegate* unmanaged<...>` becomes `delegate*<...>` (managed convention; Mono has no
    #      runtime-environment default unmanaged convention). Not exercised by the measurement.
    sed -i 's/static abstract bool Reading { get; }/bool Reading { get; }/' "$CORE_DST/StateCodec.cs"
    sed -i 's/public static bool Reading =>/public bool Reading =>/' "$CORE_DST/StateCodec.cs"
    sed -i 's/TCodec\.Reading/codec.Reading/g' "$CORE_DST"/*.cs
    sed -i 's/delegate\* unmanaged</delegate*</g' "$CORE_DST/EventStreams.cs"
    sed -i 's/BitConverter\.SingleToUInt32Bits/SvUnityBits.SingleToUInt32Bits/g; s/BitConverter\.UInt32BitsToSingle/SvUnityBits.UInt32BitsToSingle/g' "$CORE_DST"/*.cs
    sed -i 's/Environment\.TickCount64/SvUnityBits.TickCount64/g' "$CORE_DST/Bulk.cs"
    sed -i 's/OperatingSystem\.IsLinux()/SvUnityBits.IsLinux()/g' "$CORE_DST/NativeArena.cs"
    sed -i 's/ObjectDisposedException\.ThrowIf/SvUnityBits.ThrowIf/g' "$CORE_DST/Drift.cs"
    sed -i 's/Interlocked\.CompareExchange(ref gate, 1, 0)/SvUnityBits.CompareExchangeNint(ref gate, 1, 0)/' "$CORE_DST/EventStreams.cs"
    sed -i 's/Volatile\.Write(ref gate, 0)/SvUnityBits.WriteNint(ref gate, 0)/' "$CORE_DST/EventStreams.cs"
    {
        echo "== compile-surface transforms applied to SvCore copies (measure phase) =="
        echo "1. StateCodec static-abstract -> instance Reading (interface + 4 impls + $(grep -o 'codec\.Reading' "$CORE_DST"/*.cs | wc -l) call sites)"
        echo "2. EventStreams delegate* unmanaged< -> delegate*< ($(grep -c 'delegate\*<' "$CORE_DST/EventStreams.cs") sites)"
        echo "3. BitConverter.SingleToUInt32Bits -> SvUnityBits.* ($(grep -c 'SvUnityBits.SingleToUInt32Bits' "$CORE_DST"/*.cs | grep -v ':0' | wc -l) files)"
        echo "4. BitConverter.UInt32BitsToSingle -> SvUnityBits.* ($(grep -c 'SvUnityBits.UInt32BitsToSingle' "$CORE_DST"/*.cs | grep -v ':0' | wc -l) files)"
        echo "5. Environment.TickCount64 -> SvUnityBits.TickCount64 ($(grep -c 'SvUnityBits.TickCount64' "$CORE_DST/Bulk.cs") sites)"
        echo "6. ObjectDisposedException.ThrowIf -> SvUnityBits.ThrowIf ($(grep -c "SvUnityBits.ThrowIf" "$CORE_DST/Drift.cs") sites)"
        echo "7. OperatingSystem.IsLinux() -> SvUnityBits.IsLinux() ($(grep -c "SvUnityBits.IsLinux" "$CORE_DST/NativeArena.cs") sites)"
        echo "8. nint Interlocked/Volatile -> SvUnityBits atomics ($(grep -c 'SvUnityBits\..*Nint' "$CORE_DST/EventStreams.cs") sites)"
        echo "== byte-identity of untouched lines (diff-lines per file; 0 = only transform lines differ) =="
        for f in "$CORE_DST"/*.cs; do
            b="$(basename "$f")"
            d=$(diff "$CORE_SRC/$b" "$f" | grep -cE '^[<>]' || true)
            echo "$b diff-lines $d"
        done
    } > "$RECEIPT/transform-report.txt"
    cp "$ROOT/tests/Sv.Editor/asmdefs/measure/SvCore.asmdef" "$CORE_DST/SvCore.asmdef"
    cp "$ROOT/tests/Sv.Editor/asmdefs/measure/Sv.Unity.asmdef" "$UNITY_DST/Sv.Unity.asmdef"
    cp "$ROOT/tests/Sv.Editor/asmdefs/measure/SvEditor.Editor.asmdef" "$PROJECT/Assets/Editor/SvEditor.Editor.asmdef"
fi

{
    sha256sum "$CORE_DST"/*.cs "$UNITY_DST"/*.cs
} >> "$RECEIPT/source-hashes-$PHASE.txt"

export SV_EDITOR_RECEIPT_DIR="$RECEIPT"
BOOT_START=$(date +%s.%N)
date +%s.%N > "$RECEIPT/boot-$PHASE-start-epoch.txt"
STATUS=0
"$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod Sv.Editor.SvEditorBatch.Run \
    -logFile "$RECEIPT/unity-$PHASE.log" || STATUS=$?
BOOT_END=$(date +%s.%N)
date +%s.%N > "$RECEIPT/boot-$PHASE-end-epoch.txt"
echo "wall $(echo "$BOOT_END $BOOT_START" | awk '{printf "%.2f", $1 - $2}') s" > "$RECEIPT/boot-$PHASE-time.txt"
echo "editor exit $STATUS (probe-phase compile failure is the expected outcome for probe)"
