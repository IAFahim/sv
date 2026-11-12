# Issue 0144 - U5: Mono player check (not possible on this editor)

Status: closed 2026-11-12. Verified by an independent fresh clone of proto2/int at 4fcd3e3 (logs verify2-out). Remaining items live in 0149, 0150, 0151.

Tried once (cheap path): scratch editor method `PlayerSettings.SetScriptingBackend(Standalone, Mono2x)`,
then `-runTests -testPlatform StandaloneWindows64 -testFilter SvPlayerTwinTests` (one build, 135 s, 1/1).
Result: the backend reads back `CoreCLR` right after setting Mono2x, the player ran `.NET 10.0.10`
(NUnit `clr-version="10.0.10"`), digest DA47AD9B5CD74335. Unity 7000.0.0a16 ships no Mono player:
`Data/PlaybackEngines/windowsstandalonesupport/Variations` has only `*_coreclr` and `*_il2cpp`. The
`Mono2x` enum value exists but is silently ignored. So Mono float bits cannot be checked on the only (and
newest) editor on this PC; the Mono stop-and-report condition cannot trigger here.

What is proven instead: new PlayMode test `SvPlayerTwinTests` (sv core managed fold scenario, CoreCLR pin
DA47AD9B5CD74335) passes in a CoreCLR Windows player and in the editor (PlayMode 2/2). The same test is
the ready-made check for an IL2CPP player (not run: 1-2 GB build, not asked).
Backend restored to CoreCLR, scratch script removed.
