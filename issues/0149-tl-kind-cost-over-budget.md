# Issue 0149 - tl's own kind job is over the frame budget at 1M rows (owner decision, tl side)

Status: open

Labels: `ai`, `area:tl`, `open`, `owner-decision`.

## Finding (issue 0143, Unity 7000.0.0a16, 1M rows, W 2.5, 0 percent cross-row, Burst on, safety off, 3 fresh processes)

| Workers | sv total ms | tl kinds ms | Frame ms |
|---|---|---|---|
| 11 | 2.31 | 8.28 | 10.56 |
| 7 | 2.53 | 9.47 | 11.99 |
| 4 | 3.26 | 12.41 | 15.71 |
| 2 | 4.65 | 18.30 | 22.91 |

sv meets its own gate (25 percent of a 30 Hz frame, 8.3 ms) at every worker count. tl's generated kind job alone is 8.3 ms at
11 workers, so the frame is over the gate regardless of sv. No tl change was made.

## Also measured

- The send pass costs 0.6 ms with nothing sent: tl writes every kind column every tick, so every chunk is scanned
  (issue 0143). A "touched" signal from tl (addition only) would remove it.

## Decision needed

Is a 1M-row frame at 30 Hz in scope for the tl test? If yes, the kind job is the next target (tl owners). If the real row
count is lower, this issue closes with the table above as the budget.
