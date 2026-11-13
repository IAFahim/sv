# 0363 input log memory: 16 B per external record x 66 frames x up to 2 for 256-record blocks; 4.2 GB at 2M records/frame

Status: open (design: plan 15.3 budgets the input log at 10-100 MB "capped by the host"; nothing caps it)

Found by BM1 (branch v3/bench). Lego pack, 1,000,000 entities, ring 32 epochs x 2 frames (log span 66 frames).

Receipts (`host.Log.Bytes` printed by every `v3-lego-*` entry, `v3-lego-memory`):
- C, the same 2M records every frame (segments dedupe against the previous frame): input log 64.2 MB.
- c-rotate, scenario C with two alternating record sets (no dedupe): input log 4,224 MB; Log phase 6.0 ms (was 6.9 ms before
  commit 401b69f; the dedupe case went 8.1 -> 1.9 ms).
- memory test, 100,000 random records per frame over 10 % of chunks, three rotating sets: input log 288 MB (6.6 MB with one set).
- Each chunk segment is a chain of 256-record blocks (`InputLog.BlockRecords`), so a chunk with 30 records takes a 4 KB block.

Proposal: pack small segments into shared blocks (or size blocks by segment), and give the host a byte cap with a defined
policy (oldest frames leave the late-input window first). Both are design changes of `InputLog`.
