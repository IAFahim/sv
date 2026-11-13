# sv web designer (M10)

`src/Sv.Web` is the whole tool as plain C#: `SvWeb.Check`, `SvWeb.Canonical` and `SvWeb.Simulate` take the four pack files (and a scenario) as text and return JSON. `src/Sv.Wasm` is a thin `browser-wasm` shell with `[JSExport]` wrappers plus the page (`wwwroot`). `tests/Sv.Web.Tests` drives `SvWeb` on plain .NET, so nothing needs a browser to test.

Build and run: `eng/web.sh publish` then `eng/web.sh serve`, open http://127.0.0.1:8123. Needs the `wasm-tools` workload for the SDK in use. If `dotnet workload install` is not allowed on the machine, install a user-local SDK (`dotnet-install.sh --version <sdk> --install-dir <dir>`), put it first on PATH with DOTNET_ROOT and run `dotnet workload install wasm-tools` there.

What it does: edit values, rules, cards, templates and a scenario (kept in localStorage); Check compiles with the same `PackCompiler` the `sv` tool and Unity use and lists diagnostics with codes and offsets; Simulate runs the reference interpreter and shows every layer of every frame (filter by entity and frame), the scenario assertions and the digest; Canonical and Download pack give the canonical files and PackHash.

Measured (2026-11-13, Linux, built-in browser): published size 5.8 MB (gate 30 MB, no AOT, trimmed); runtime ready in about 4 s on first load; first Simulate of the worked example 54 ms; digest E0F445186E2C3672 and PackHash BD70627B1C671888 equal the pins.

Byte identity: exported canonical files are a fixed point (`Canonical(Canonical(x)) == Canonical(x)`, same PackHash) and every generated Unity file for the canonical pack equals the one for the raw edit except `Rules.map.txt`, whose offsets point into the text the designer wrote. Load the exported canonical files into Unity and the map follows them.
