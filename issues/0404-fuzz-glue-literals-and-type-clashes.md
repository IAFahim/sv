# 0404 fuzz: glue: timeline names broke the generated C#, and cards could clash on one type

Status: closed (2026-11-26, fixed: names are escaped, clashes are pack errors)

Found by: Claude red-team pass, tests/Sv.Fuzz v4json (glue check), sv commit d929d03

## What was wrong
The v3 and v4 tl glue wrote card, channel and timeline names into C# string literals as they were. A quote, backslash, newline, U+2028 or U+0085 in a name ended the literal or the line, so the emitted file did not compile. Separately, two cards differing only in case (`poison`, `Poison`), or a card and an outbox channel (`poison` and `PoisonCard`), became the same type name (`PoisonCard`, `PoisonCardFeed`) and the glue declared it twice.

## Fix
`Names.Literal` escapes everything outside printable ASCII as \\uXXXX; `TlGlueEmitter` and `ColumnsGlue` use it. `ColumnsGlue.Problems` lists every type two names would share and `ColumnsGlue.Files` throws PackException with those lines; `sv pack` reports them as pack errors.

## Tests
`RedTeamGlueTests` (literal escaping, hostile timeline names in both glue files, card/card and card/outbox clashes). Corpus: `tests/Sv.Fuzz/corpus/v4/glue-*.json`.
