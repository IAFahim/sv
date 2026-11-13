# 0365 RawArena counts an interval at Mark and again at EndLayer (the "double interval count" from the handoff)

Status: open (no measured hot-path cost; a fix moves HardCap verdicts, so it is not a performance-only change)

Found by BM1 (branch v3/bench), checking the note in docs/V3-HANDOFF.md section 12.

`RawArena.Mark` flushes and does `interval++`; `EndLayer` does `interval++` again when the layer fits. After the Decide layer the
EdgeEmits layer is checked with 4 intervals where 3 have happened (external sends, Decide, EdgeEmits), so the capacity test
`records/256 + intervals x writers > blocks` keeps one extra `writers` blocks of slack (75 blocks = 19,200 records with 11
external writers and 64 parts) per completed layer of the frame. `Reset` (end of Seal) puts it back to 1 every frame.

Receipts: `v3-lego-c`, `v3-lego-c-rotate` and `v3-lego-r` at 1M print `rawGrows=0` (raw capacity 4,325,376), so in Grow mode it costs nothing measured.
Its effects: a layer can be rerun once (Grow) or discarded (HardCap) while it physically fits by up to one `writers` worth of
blocks. Owner decides whether HardCap should count each layer once (drop the increment in `EndLayer`).
