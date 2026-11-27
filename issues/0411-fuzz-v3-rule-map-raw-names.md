# 0411 fuzz: v3: Rules.map.txt wrote rule names raw and broke its six tab fields

Status: closed (2026-11-26, fixed: control and line-break characters are escaped)

Found by: Claude red-team pass, tests/Sv.Fuzz v3json (map check), sv commit d929d03

## What was wrong
Rule names are free text. A tab, newline, carriage return, U+0085 or U+2028 in one went into `Rules.map.txt` as is, so the line split into extra fields or extra lines and tools reading the map by line and tab got the wrong rule.

## Fix
The map writes the name through `Field`, which escapes backslash, control characters, U+007F, U+0085, U+2028 and U+2029 as \\uXXXX.

## Tests
`RedTeamPackTests.A_rule_name_with_tabs_and_line_breaks_keeps_every_rule_map_line_at_six_fields`. Corpus: `v3-rule-map-*`.
