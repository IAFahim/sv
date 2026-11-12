#!/usr/bin/env bash
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
O=${V0_OUT:-D:/GitHub/_work/sv-proto/c-out}
E="${MONO_HOME:-C:/Program Files/Unity/Hub/Editor/7000.0.0a16/Editor/Data/MonoBleedingEdge}"
mkdir -p "$O"
dotnet run -c Release --project "$ROOT/StatFoldHost.csproj" -- dotnet10 "$O/statfold-dotnet10.bin"
DOTNET_EnableHWIntrinsic=0 dotnet run -c Release --no-build --project "$ROOT/StatFoldHost.csproj" -- dotnet10-nohw "$O/statfold-dotnet10-nohw.bin"
"$E/bin/mono.exe" "$E/lib/mono/4.5/csc.exe" -nologo -unsafe+ -optimize+ -out:"$O/statfold-mono.exe" "$ROOT/StatFoldSets.cs" "$ROOT/StatFoldHost.cs"
"$E/bin/mono.exe" "$O/statfold-mono.exe" mono-jit "$O/statfold-mono.bin"
sed -e 's/value = value + entries\[i\].Value;/value = (float)(value + entries[i].Value);/' -e 's/additive = additive + entries\[i\].Value;/additive = (float)(additive + entries[i].Value);/' -e 's/value = value \* additive;/value = (float)(value * additive);/' -e 's/value = value \* (1f + entries\[i\].Value);/value = (float)(value * (float)(1f + entries[i].Value));/' "$ROOT/StatFoldSets.cs" > "$O/StatFoldSetsCast.cs"
"$E/bin/mono.exe" "$E/lib/mono/4.5/csc.exe" -nologo -unsafe+ -optimize+ -out:"$O/statfold-mono-cast.exe" "$O/StatFoldSetsCast.cs" "$ROOT/StatFoldHost.cs"
"$E/bin/mono.exe" "$O/statfold-mono-cast.exe" mono-cast "$O/statfold-mono-cast.bin"
python "$ROOT/statfold_ref.py" "$O/statfold-dotnet10.bin" "$O/statfold-mono.bin"
