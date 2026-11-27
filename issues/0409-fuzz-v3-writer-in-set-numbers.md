# 0409 fuzz: v3: the pack writer turned unknown channel or kind numbers in an in-set into names

Status: closed (2026-11-26, fixed: numbers with no name are written as numbers)

Found by: Claude red-team pass, tests/Sv.Fuzz v3json (O10), sv commit d929d03

## What was wrong
An `in` condition on `record.channel` or `record.kind` may list numbers. `PackWriter` looked every member up as a name: a number with no channel came out as `"?50"`, which the reader refuses, and a number past int range (4294967297) wrapped to 1 and came out as that channel's name, changing the rule. Breaks O10.

## Fix
Only members in int range that name a channel or kind are written as names; the rest are written as numbers.

## Tests
`RedTeamPackTests.Channel_numbers_outside_the_schema_in_an_in_set_survive_a_write_and_read_with_the_same_hash`. Corpus: `v3-roundtrip-hash-*`, `v3-roundtrip-refused-*`.
