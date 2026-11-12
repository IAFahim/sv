# 0312 generated kernels raise warning CS0219 (unused `effect`) for packs without a self-admit gate

Status: open

Found by T2 (`tests/Sv.Diff.Tests`, `Generated_kernels_compile_without_warnings`): 243 of 680 generated packs. Kernel projects
build with `TreatWarningsAsErrors` (as `tests/Sv.Kernels.Reference` does), so such a pack breaks the build.

Receipt: seed 101 (`PackGenerator.Make(101, new GeneratorOptions())`, full pack in the receipts folder):
`Kernels.g.cs(861,17): warning CS0219: The variable 'effect' is assigned but its value is never used`, inside
`static int SelfAdmit(...)` whose `switch (channel)` has only empty cases.

Suspected cause: `src/Sv.Rules/Emit/Kernels.cs:404` writes `var effect = default(Effect);` unconditionally; it is read only when
some case of `kernel.SelfAdmit` emits a rule.