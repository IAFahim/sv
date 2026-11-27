# 0412 fuzz: v3: a card named like another card's value made a rule switch cards after a write and read

Status: closed (2026-11-26, fixed: the reader refuses names that could mean two cards)

Found by: Claude red-team pass, tests/Sv.Fuzz v3json seed 4 (O10), sv commit d929d03

## What was wrong
A rule's `card`, a template's `cards` key and `held` look a name up among card names first, then intrinsic names. With cards `sniper_ward` (value SniperWard) and `SniperWard` (value Fury), a rule on `sniper_ward` was written back as `"card":"SniperWard"` and read back as Fury. Two cards with one name had the same problem. Breaks O10.

## Fix
`PackReader` reports DuplicateName for two cards with one name, and for a card whose name is an intrinsic other than its own value. Every pack in the repo still reads with the same hash.

## Tests
`RedTeamPackTests.A_card_name_that_could_resolve_to_two_different_cards_is_refused`. Corpus: `v3-card-name-*` (expected: rejected).
