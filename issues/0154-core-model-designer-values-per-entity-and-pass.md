# Issue 0154 - Core model: designer-defined values per entity, contiguous, and a pass between any two entities

Status: closed (2026-11-14, decided by the lead dev under the owner's standing delegation)

Labels: `ai`, `area:core`, `area:unity`, `area:authoring`, `closed`, `owner-requirement`, `supersedes-premises-of-0145-0153`.

## Owner requirement (2026-11-12, verbatim intent)

sv is the thing that gives an entity its dictionary of values, stored contiguously per entity.

- A designer defines a value (health, mana, storage, immune, curse, heal) and marks it as intrinsic, as stat, or as a plain
  number. Nobody writes a component, a `[TlState]` struct, or a `ComponentLookup` per value. No programmer declares health.
- Every entity has its own set: a player, the player's hand, a shield, any object. The set differs per entity.
- Any entity can pass a value to any other entity. The hand has `heal` and it goes to the player's `health`. The shield has a
  `curse` and it goes to the player. This is a general pass, not a targeting or linking system the designer builds.
- Immunity is not a special feature. It is an ordinary designer value: `immune` as an integer or bit mask (101 = damage and
  curse, immune to fire is another bit; 5 times immune is a counter). Rules read it like any other value.
- The usage code is small and reads like a dictionary, not like ECS setup.

## What exists and does not fit

- `src/Sv` core: a slot-indexed board with schema ids for stats and intrinsics, modifiers, a fold, an integer inbox. This is
  the nearest thing to the dictionary and is reusable.
- Schema is fixed at build time and every value must be registered in code (`links.Stat<T>`, `links.Intrinsic<T,T>`) with
  a hand-written `[TlState]` struct per column. That is the opposite of the requirement.
- No entity hierarchy (hand belongs to body). No generic pass; only slot-addressed integer sends through a hand-declared
  effect and target column.
- No designer-visible data format that creates values and entity profiles.

## Needed (to design with the senior, then build)

1. Data format: values with roles (intrinsic, stat, plain, mask), entity profiles (player, hand, shield), per-entity
   membership that can change at runtime (picking up a cursed shield adds `curse`).
2. Storage: contiguous per entity, still deterministic and 0 B warm, still foldable once per tick.
3. Pass: one operation `pass(from entity, value, to entity, value, amount)` valid between any two entities, ordered
   deterministically, usable from data (a clip or a rule) and from code; integer amounts cross entities, floats do not.
4. Parent and part relation (hand to body) so a part can pass to its owner without knowing the owner's id.
5. Masks and counters as first-class values with defined fold law (add, or, clamp).
6. What stays: pinned digests, the fold law, Burst job layout, the Unity adapter as the host.

## Done when

A designer adds `health` and `immune` to a player profile and `heal` to a hand profile in data only; the hand heals the
player, a shield curse lands on the player, an immune bit blocks it; no C# per value; digests pinned at 1 and 11 workers.

Next step: senior lookup (issue 0152) with this file as the main question. Issues 0145 to 0153 are re-scoped by it.

## Decision (2026-11-14)

Delivered by v3. Values are designer data (`values.sv.json` classes: stat, intrinsic, channel, event, kind), every entity of a template has them at fixed lane offsets (contiguous per chunk, 41-46 B per entity at rest), templates are the profiles, any entity passes an amount to any other by an integer record, a part reaches its owner through template channel forwarding (`packs/lego` template `hand`: `"forwards": [{"channel": "Healing", "to": "Owner"}]`, with an `Owner` value of role owner), immunity is an ordinary mask value read by rules (`immune_by_kind_mask`), cards change membership at run time, and no C# is written per value. Receipts: `packs/lego` (22 mechanics, docs/LEGO-REPORT.md), Lego 120 tests, Unity twin replays at 1 and 11 workers.
