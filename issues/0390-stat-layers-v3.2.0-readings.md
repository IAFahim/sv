# 0390 stat layers and derived stats (v3.2.0): readings taken

Status: closed (2026-11-26, the owner approved `contract/CONTRACT-v3.2.0-stat-layers.md` as written: "Approve both", asked together with the v3.0.0 release; shipped in v3.0.0)

Additive amendments over `v3.0.1` and `v3.1.0` implementing Clauses 39 (`core` then `external` stat fold layers) and 40 (`derive` linear and curve row form on stats). Packs omitting `"layer"` and `"derive"` stay byte-identical and preserve every existing `PackHash` and digest.

Readings taken where the request could admit alternatives:

1. Derived terms live in the `core` layer (`Added` terms before `core` additive, multi, cap, and before `external`).
2. Derived edges read `from`'s final resolved value (post-`external` of `from`), evaluated in topological order (`PackPlan.StatOrder` and `StatMachine` rank).
3. `PackReader` accepts `"derive"` as either a single object or non-empty array; `PackWriter` canonically writes an array when non-empty and omits it when empty.
4. Linear `"coefficient"` uses basis-point scale `10000` (matching `StatLaw.Derived`); curve `"min"` and `"step"` use `scale(from)` while `"curve"` entries use `scale(target)`.
5. Both `Host` (`FoldTemplate`) and `Reference` (`ApplyCommands`) compute initial derived template baselines on entity spawn so frame-0 `bound moved` and digests stay aligned.
