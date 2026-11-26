# Unity CI runner

The `sv Unity` workflow (`.github/workflows/unity.yml`, job `Unity adapter compile, EditMode tests, million-entity bench`) runs on a self-hosted runner labelled `self-hosted`, `unity`. Until one is registered for IAFahim/sv the job stays `queued`; nothing else in CI waits for it.

The job runs `bash eng/unity-ci.sh`: it finds the newest installed Unity 7 editor (or the binary in `UNITY`), creates or reuses its own probe project at `~/.cache/sv-unity-probe-<version>` (never tl.unity's probe), compiles `com.sv.engine` and `com.sv.unity.v4` headless, runs the EditMode suite (the package has no PlayMode-platform assembly; its play-mode proof is a `[UnityTest]` in the EditMode suite; `SV_UNITY_STEPS="compile EditMode PlayMode bench"` adds a PlayMode run), then runs `eng/unity-bench/SvUnityBench.cs` in batch mode and prints `SVBENCH` lines with the frame time of each shape. The logs are uploaded as the `sv-unity-logs` artifact.

| | |
|---|---|
| Runner name | `sv-unity-licensed-linux` |
| Labels | `self-hosted`, `Linux`, `X64`, `unity` |
| Host | the machine with the licensed Unity 7 editor, the same one as tl.unity's runner is fine |
| Runner folder | `~/actions-runner-sv` (separate from `~/actions-runner-tl-unity`: one runner registration per repository) |
| Service | `actions.runner.IAFahim-sv.sv-unity-licensed-linux.service` |

## Install it

Run on the Unity machine as the user that owns the licensed editor, with `gh` signed in as an owner of IAFahim/sv:

```
mkdir ~/actions-runner-sv && cd ~/actions-runner-sv
curl -fsSL -o r.tgz https://github.com/actions/runner/releases/download/v2.338.2/actions-runner-linux-x64-2.338.2.tar.gz && tar xzf r.tgz && rm r.tgz
./config.sh --unattended --url https://github.com/IAFahim/sv --token "$(gh api -X POST repos/IAFahim/sv/actions/runners/registration-token --jq .token)" --name sv-unity-licensed-linux --labels unity --replace
printf 'DOTNET_ROOT=%s/.dotnet\nUNITY_NO_AUTH_BROKER=1\n' "$HOME" >> .env
sudo ./svc.sh install "$USER" && sudo ./svc.sh start
```

Put `$HOME/.dotnet` at the front of the line in `.path` before starting the service. If the editor is not under `~/Unity/Hub/Editor/7*` or on the `unity` CLI's list, add `UNITY=/path/to/Unity` to `.env`. To pin com.tl.unity next to sv in the probe, add `TL_UNITY_ROOT=/path/to/tl.unity`.

Then start the job once: re-run the queued `sv Unity` run from the Actions tab, or `gh workflow run unity.yml --ref <branch> -R IAFahim/sv`.

## Is it online

```
gh api repos/IAFahim/sv/actions/runners --jq '.runners[]|{name,status,busy}'
```

## Restart it

```
sudo systemctl restart actions.runner.IAFahim-sv.sv-unity-licensed-linux.service
journalctl -u actions.runner.IAFahim-sv.sv-unity-licensed-linux.service -n 50 --no-pager
```

The runner log of the last error is `~/actions-runner-sv/_diag/Runner_*.log`. If the job fails at the licence, open the Editor once on that machine and sign in.

## What a run tells you

- `compile exit=0 errorCS=0 warningCS=0 errorBC=0`: the adapter compiles on that editor without errors, warnings or Burst errors.
- `EditMode exit=0 total=90 passed=90 failed=0`: every EditMode test passed (90 on 2026-11-25).
- `SVBENCH entities 20000 recordsPerEntity 500 ... step median X ms`: the Unity-side frame time for that shape on that machine. Compare it with `dotnet benchmarks/Sv.Bench/bin/Release/net10.0/Sv.Bench.dll v3-scale` on the same machine.
- `Unity wrote .meta files that the repository lacks`: Unity created `.meta` files for new package files (they must come from Unity, not by hand). The log prints each file between `--- <path>` and `--- end`, and the artifact has copies under `metas/`; commit them.

The first run after a package change can be slower while Unity imports and Burst compiles; the runner keeps the probe's `Library`, so later runs are warm. Two Unity jobs on one machine at the same time (sv's and tl.unity's) slow each other's benchmark numbers; the compile and test results are unaffected.

Fork pull requests do not run here: the workflow guards the job by repository.
