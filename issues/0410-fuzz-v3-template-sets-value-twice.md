# 0410 fuzz: v3: a template could set the same value twice and the writer wrote a duplicate key

Status: closed (2026-11-26, fixed: the reader refuses it)

Found by: Claude red-team pass, tests/Sv.Fuzz v3json (O10), sv commit d929d03

## What was wrong
A template's `values` and `cards` both become settings on intrinsics. Two cards counting in one intrinsic, or a value and a card on the same one, gave two settings for one value; which one won depended on order, and the written pack held a duplicate JSON key that the reader then refused. Breaks O10.

## Fix
`PackReader` sorts a template's settings and reports DuplicateName ("template X sets V twice; two of its values or cards land on the same value").

## Tests
`RedTeamPackTests.A_template_whose_cards_land_on_the_same_value_twice_is_refused`. Corpus: `v3-template-duplicate-*` (expected: rejected).
