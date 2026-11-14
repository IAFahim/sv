# sv v3 brick-set acceptance test ("Lego test")

Date 2026-11-13, branch `v3/lego` (from `v3/int` 880f03b). Question: are the rule bricks of contract v3.0.0/v3.0.1 enough to
write real game mechanics as designer data only? Method: 22 mechanics written as pack rows in `packs/lego/` (values, cards,
templates, rules; no code), one scenario each in `packs/lego/scenarios/` with numbers computed by hand from the contract and
issue 0180 readings, run with `sv run` on the reference interpreter and on compiled kernels (`tests/Sv.Kernels.Lego`), and
asserted by `tests/Sv.Lego.Tests` (90 of 90 pass: reference, compiled shared x1, digest equality, dense x1, shared x4 workers).

Result in one line: every scenario gives the hand-computed numbers and the same digest on both hosts (no production bug found);
13 of 22 mechanics are clean, 7 need tricks, 2 cannot be said as designed. The gaps cluster in four missing bricks: assign,
amount-from-a-cell, per-count/stat-driven modifiers, and a deterministic roll.

Time is tl's job and space is grid-influence's: every timer below is an sv counter intrinsic that decays only when tl sends an
external `Tick` record (channel `Tick`, origin `Timeline`, one per frame per entity that owns a timer). sv holds no timer.

## Table

Bricks: **G** gate (Admit deny/allow, Shape, Absorb; priority, group, condition), **E** emit (Self same frame / Source / Owner,
per Record/Hit/Source/Frame, amount Constant/Offered/Taken/NotTaken, `scale` by a stat, depth limit), **F** flow, **I** channel
`into` intrinsic, **B** fold bounds (floor/ceiling), **V** event with enter/stay hysteresis, **X** edge emit, **C** card
(counted guard, intrinsic > 0), **M** stat modifiers (card rows or host stat ops), **R** forward (template routing to Owner),
**T** external Tick record.

| # | Mechanic (scenario) | Bricks used | Expressible | Scenario result (both hosts) | Missing if not clean |
|---|---|---|---|---|---|
| 1 | Poison over time (`01`) | I PoisonTime, T, E self Poison 4 + PoisonTime -1 when `PoisonTicks>0`, B floor 0, G type mask | yes | V 100,96,92,88,88; ticks 3,2,1,0,0; poison-immune M stays 100 (self-emitted DoT is gated) | DoT damage per stack (amount from the counter) not sayable, see missing 2 |
| 2 | Heal over time (`02`) | C RegenCard, T, E self Healing 1 `scale` RegenRate, B ceiling MaxHealth, V LowHealth 30/40 | yes | R 29,33,37,41,45; LowHealth t,t,t,f (hysteresis holds at 33 and 37); F capped 100 | - |
| 3 | Shield absorb (`03`) | G absorb Amount ByAmountDescending, budget Shield | yes | Shield 10 then 0; Health 100, 85, 80 | - |
| 4 | Lifesteal (`04`) | E victim->Source DamageDealt Taken (when `source.Lifesteal>0`), C, E self Healing Taken `scale` LifestealRate | awkward | V 60; A 50 then 70 one frame later | attacker cannot see what the victim took; needs a hit-confirm round trip and costs one frame (law S2) |
| 5 | Thorns, reflection limit (`05`) | C thorns, E Source same Taken `scale` ThornsRate, depthLimit 1 | yes | B 80; A 90; B 75 (one bounce back, depth 2 stops) | - |
| 6 | Immunity as a number (`06`) | C ImmuneCharges as card, G absorb Hit | yes | 3 hits, 2 absorbed: Health 90, charges 0; next hit 80 | - |
| 7 | Immunity as bit masks by type and by source kind (`07`) | G `bit self.ImmuneMask record.channel`, G `bit self.KindMask record.kind` | yes (awkward above 63) | M 90: rifle poison and every beast record denied | bit index is the id; channels or kinds >= 64 cannot be masked (Op.Bit returns false) |
| 8 | Stun with duration as counter (`08`) | I StunTime, T, E decay, B floor 0, V Stunned, G Move denied while stunned | yes | StunTicks 2,1,0; Stunned t,t,f; Moves 1,1,1,2 | refresh (`max(current, d)`) is not sayable; a second stun adds |
| 9 | Double jump that refills (`09`) | B ceiling MaxJumps, F Jump, G `not held Jumps`, E Landed -> JumpRefill 1 `scale` MaxJumps | awkward | Jumps 1,0,0,2,0,2,0; JumpsDone 1,2,2,2,4,4,**7** | refill is "add MaxJumps and let the ceiling clip" (no assign); 3 jumps in one frame with 2 charges all pass (JumpsDone 7 for 2 real jumps) |
| 10 | Cooldown (`10`) | G deny Cast while Cooldown>0, E admitted Cast -> CooldownTime 3, T decay, F Casts | yes | Casts 1,1,1,1,2; Cooldown 3,2,1,0,3 | - |
| 11 | Crit as a deterministic rule on hits (`11`) | C CritCard, E per Hit Streak +1, E Streak -3 when `HitStreak>=2`, G multi 1.0 when `HitStreak>=2` | awkward | Health 90,80,60,50,30,10; streak 1,2,0,1,3,1 (the 3rd hit since reset at f4 did not crit) | no chance (no deterministic roll), no modulo, no running index within a frame |
| 12 | Execute below 20 % (`12`) | G `below(Health, MaxHealth, 20, 100)` and `source.Execute>0` -> added 100000, B floor 0, V Dead | yes | 15 then 0; Dead f,t,t | sv cannot despawn; the host acts on Dead |
| 13 | Revive once (`13`) | V Dead, X edge enter -> self Healing 50 and ReviveSpend -1, C ReviveCharges, I | awkward | Health 0,50,0,0; Dead t,f,t,t; charges 0 | entity is dead for one frame (Dead edge published) before the revive; "cheat death" in the same frame needs a gate on frame totals, which gates may not read |
| 14 | Damage type conversion (`14`) | C Converter, G multi -0.5, E self Fire Offered `scale` ConvertRate, F FireTaken | yes | C 80, FireTaken 10; fire-immune F 90, FireTaken 0 | - |
| 15 | Stacking buff with a cap (`15`) | I RageGain, B ceiling 5, G five rows in exclusive group 1 (`source.RageStacks >= k` -> additive k x 0.1) | awkward | Rage 3 then 5 (7 clipped); V 370 (+30 %), 220 (+50 %) | Shape values are constants: N stacks need N gate rows; card modifiers ignore the count |
| 16 | DoT snapshot at application (`16`) | E "CastPoison" on the attacker with the victim as record source -> Source PoisonPower 1 `scale` Power, I SnapPower | **no** (capture only) | SnapPower 12 after Power rose to 20; ticks driven by tl (Timeline Poison 12): 88, 76; a second cast adds: 32 | the tick cannot read SnapPower (no amount from a cell); re-application adds instead of replacing (no assign); addressing the victim abuses the record's source field |
| 17 | Core versus external stacking layer (`17`) | M card core_training (added 20, additive 0.5), host ops Multi 0.1 and Added 10 | **no** | Strength 198, then **214** | one fold per stat: an "external" Added joins the core sum; designer intent (180+10) x 1.1 = 209 is not reachable; derived stats have no pack row (issue 0210 item 3) |
| 18 | Hand passes to its owner, owner change (`18`) | R forward Healing -> Owner, I OwnerShift into Owner | awkward | Owner 0 then 1; P1 60, P2 50, then P2 60 | the owner is set by adding the slot difference (no assign); designer data must know slot numbers |
| 19 | Gate by source: only the owner can heal (`19`) | C OwnerOnlyHeal, G `ne record.source self.Owner` deny, I OwnerShift | yes | heal before owner set denied (50); owner heal admitted, stranger denied (60) | owner set by delta, as row 18 |
| 20 | Every damage you take heals 5 (`20`) | C HealPerHit, E self Healing 5 per Hit when `record.taken>0` | yes | 100-34+20 = 86; then 89 | - |
| 21 | Worked example 100 -> 95 (`21`) | C sniper_ward/thorns/leech, G kind deny, E reflect Taken, E leech per Source | yes | P 95, LowHealth false; S2 75 next frame; S1 100 | (Bleeding replaced by Poison: one channel fewer) |
| 22 | Per-Hit emit and absorbed hits (`22`) | as 20 plus G absorb Hit (2 charges) | yes, reading pinned | Health 50-10+15 = 55 | ambiguity: the heal counts offered hits, not taken hits (see below) |

Counts: 22 mechanics; 13 yes, 7 awkward (4, 9, 11, 13, 15, 18, 7 above 63), 2 no (16, 17). Digests (reference = compiled,
shared x1, dense x1, shared x4): 01 05B6700700D82B42, 02 B346BD6F9BCEDEF1, 03 7B34D75614614776, 04 86B23FDD3E7BE8CF,
05 77BC29EDAD1EFA65, 06 D8D9FF5C8AFE1A66, 07 92A70B00E559B25D, 08 47FA18613A92379B, 09 5EF114C7482B6D9D, 10 5FD5ADA96A3585E5,
11 D634D9A7150DDD89, 12 A6F5EF9FE2B0091C, 13 5585F58F2ABAA7F8, 14 3180F2CEF9B0E6C7, 15 1256AD83EFDDBB56, 16 06F8648B3595D1D6,
17 CE059C93789974D1, 18 6E94EC4288CAB6E9, 19 2A4C898972E4D196, 20 A7535706BCD886EE, 21 6C423C98D55FF65D, 22 B8D3F09BC648CCEF.
None pinned in the scenario files (printed only).

## Missing bricks (what a designer cannot say, with a minimal proposal each)

**Status after v3.1.0 (branch `v3/b1-bricks`, `contract/CONTRACT-v3.1.0-bricks.md`, issue 0350):** items 1, 2, 3, 5 and 6 are
closed; 4, 7 and 8 stay open. The seven awkward mechanics are rewritten with the new bricks as `packs/lego/scenarios-bricks/`
(the 22 originals are unchanged and keep every digest above), asserted by `tests/Sv.Lego.Tests/BrickScenarioTests.cs` on the
reference, compiled shared x1, dense and 2/4/7/11 workers:

| # | Replaces | Scenario | Bricks | Hand-computed result | Digest (printed, not pinned) |
|---|---|---|---|---|---|
| 31 | 9 refill | `31_double_jump_refill_by_set` | `JumpSet` mode set, amount `{"cell":"self.MaxJumps"}` | Jumps 1,0,2,1,1; Land and Jump in one frame end at 1 (set 2, then -1) | 4FEE326C66100884 |
| 32 | 8 refresh | `32_stun_refresh_by_max` | `StunRefresh` mode max | StunTicks 2,2,1,0 (refresh 3 on 2 with a tick: max then -1; refresh 1 never lowers) | 47FA18613A92379B |
| 33 | 16 snapshot | `33_dot_snapshot_deals_stored_value` | `PoisonSnap` mode set, Tick emit `{"cell":"self.SnapPower"}` | SnapPower 12 then 20 (replaced, not 32); Health 100,88,76,56 | 54DD3B5C7C2DB1B0 |
| 34 | 18, 19 owner | `34_owner_change_by_source` | `OwnerSet` mode source | Owner = sender's slot 0 then 1; P1 60, P2 60 | 6E94EC4288CAB6E9 |
| 35 | 11 crit | `35_crit_chance_by_roll` | `{"roll":2500}` | crits at frames 1, 2, 6 (basis points 1364, 2281, 2239 from the contract hash); Health 490..390 | CC7C73D9032C7BA9 |
| 36 | 9 overspend | `36_jumps_spent_per_hit_by_spend` | gate `{"spend":"Jumps"}` | 3 leaps with 2 charges: 2 pass (LeapsDone 2, not 3); two records share the budget in canonical order | 74E5DE19D9BAD6B9 |
| 37 | 15 stack table | `37_stack_buff_per_count_card` | card modifier `perCount`, Shape `{"additive":{"cell":"source.RageBonus"}}` | RageBonus 0.30 then 0.50; Health 370, 220 with one gate row instead of five | D26533DB57082930 |

1. **Assign (set) an intrinsic.** *Closed (clause 34: `mode` set/max/min/source on `into`, set/max/min flow terms).* Every channel `into` and flow adds. Refill to max (9), refresh a stun (8), replace a snapshot
   (16), change an owner (18, 19) all become "add a computed difference" or "overshoot and clip". Proposal: a channel option
   `"into": "X", "mode": "set"` (last record in canonical order wins; or `max`/`min` modes), folded before the add terms, so it
   stays one delta per intrinsic and deterministic. `"mode": "source"` (set to the record's source slot) closes owner assignment.
2. **Emit amount from a cell or an expression.** *Closed for cells (clause 35: `{"cell": ...}` amounts, `scale` by an
   intrinsic); a general integer expression is still open.* Amount is a constant or Offered/Taken/NotTaken, optionally `scale` by a stat of
   the emitter (`PackReader.ReadAction`, "amount is an integer, Offered, Taken or NotTaken"; scale "names no stat"). So a DoT
   cannot deal its stored snapshot (16), poison cannot deal `stacks x 4` (1), a refill cannot add `MaxJumps - Jumps` (9).
   Proposal: `"amount": {"cell": "self.SnapPower"}` (integer intrinsic, or a stat rescaled by clause 33), and `scale` accepting
   an intrinsic. A general integer expression is the bigger version.
3. **Shape and modifiers driven by a value.** *Closed (clause 36: `perCount` card modifiers, Shape values from a cell).
   Rules adding stat modifiers is still not a brick.* Gate Shape values and card modifiers are constants; card modifiers fold once while
   the card is held, whatever its count (`Interpreter.Stats` and `StatMachine.Cards`: held = lane > 0). Rules cannot add stat
   modifiers at all (stat ops come only from the host). Proposal: card modifier `"perCount": true` (term x count, saturated), and
   a Shape whose value names a cell: `{"additive": {"cell": "source.RageBonus"}}` read once per record.
4. **Two stat layers.** Clause 31 is one fold per stat; "core then external" (17) needs an ordered second fold. Proposal: a
   modifier `layer: core|external` with the law applied twice (core terms on the base, external terms on the core result), or
   the derived-stat row form that issue 0210 item 3 already asks for (a stat Core feeding stat Strength by coefficient 1.0).
5. **Deterministic roll.** *Closed (clause 37: `{"roll": p}`, SplitMix64 over pack seed, frame, record key, amount, rule id;
   one draw per record).* No chance brick exists, so a crit chance, dodge or proc chance cannot be written (11). Proposal:
   `{"roll": 2500}` true for 25 % of records, from a fixed hash of (frame, record key, amount, rule id); identical on every host
   and replayable because the key is canonical.
6. **Running index within a frame.** *Closed for budgets (clause 38: gate `spend`); a general running index is still open.* Gates and emits read persistent state frozen at the frame start (clause 7, A4), so N
   records in one frame all see the same counter: 3 jumps pass with 2 charges (9), the 3rd hit after a reset does not crit when
   it shares a frame (11). Absorb is the only brick that spends in canonical order. Proposal: a gate effect `"spend": "Jumps"`
   that admits hits while a budget lasts and denies the rest (absorb's order with the opposite verdict), so "consume a charge"
   gates exactly.
7. **Same-frame lethal check.** A gate may not read totals or aggregates (`CheckGate`), so "cheat death" (survive the lethal
   frame) is not sayable; revive is one frame late (13). Proposal: a fold-layer rule `"onFloor": {"emit"...}` or a flow with a
   `when` on totals that adds a constant (flows can read totals but only move channel totals today).
8. **Mask above 63.** `bit` takes a 64-bit value and an index; kinds go to 255 and channels to 255. Proposal: `bit` over an
   intrinsic range (word = index / 32 into consecutive intrinsics), or a `kindSet`/`channelSet` template field.

## Redundant bricks (two ways to say the same thing)

- **Card row versus intrinsic as card.** `"card": "thorns"` (card row naming intrinsic Thorns) and `"card": "RegenCard"`
  (intrinsic directly) guard the same way; template `"cards": {"thorns": 1}` equals `"values": {"Thorns": 1}`. A card row only
  adds stat modifiers. Issue 0180 item 2 already reads both.
- **`held` versus `gt [X, 0]`.** `{"held": "Jumps"}` is literally `Compare(Gt, Jumps, 0)` (`PackReader.Operator`).
- **Channel `into` versus an unconditional flow `add`.** `"into": "Cooldown"` equals `{"add": ["Cooldown", "CooldownTime"]}`.
- **Deny versus Shape to zero.** `deny` and `multi: -1.0` (or `cap: 0`) both give taken 0 and NotTaken = offered; they differ
  only in the trace ("deny by" versus "allow ... taken 0") and in priority resolution. The priority rule makes deny the
  canonical form; Shape-to-zero could be rejected or warned.
- **Self emit into an `into` channel versus a flow.** A same-frame self emit with a constant amount and a flow both end as one
  delta; the emit additionally passes channel-state gates. Two places to put "on X, change Y".
- **Float operators in conditions.** `float`, `bound` and float literals remain (issue 0210 item 2) beside the integer path; no
  lego mechanic needed them.

## Awkward (expressible only through tricks)

- **Owner by slot arithmetic** (18, 19): sending `OwnerShift` amount `target slot - current owner`; the scenario must know slot
  numbers from `SlotPlanner`. Fragile across spawn order.
- **Victim addressed through the record's source field** (16): the host sends `CastPoison` to the attacker with the victim as
  `source`, so the attacker's `to: Source` reaches the victim. Emits can reach only Self, Source, Owner or a reference
  intrinsic that nothing can assign.
- **Refill as overshoot and clip** (9): `JumpRefill` adds MaxJumps and the fold ceiling clips.
- **Stack table** (15): one gate row per stack count in an exclusive group; cap 20 means 20 rows.
- **Crit as a streak counter** (11): two emit rules plus a gate; exact only with one hit record per frame.
- **Revive via an edge** (13): one dead frame; the Dead edge is published to the host before the heal arrives.
- **Lifesteal round trip** (4): an unguarded victim rule `hit_confirm_for_lifesteal` reads `source.Lifesteal` so the attacker
  learns what was taken one frame later; the percent must live on the attacker (`scale` reads only the emitter's stats).
- **A timer is three rows** (1, 8, 10): a channel with `into`, an emit on `Tick` with `-1`, and a fold floor 0, per counter.
  Plus tl must send a Tick to every entity that owns any timer (self emits run only for entities that hold records this frame).
- **DoT loses its source** (1, 16): ticks are self emits with no source, so thorns, lifesteal or a source-kind gate cannot
  attribute a poison tick to whoever applied it (R6 "each stored effect keeps its source" holds for records, not for state).

## Ambiguities found (code reading taken, owner or senior to confirm)

- **Per-Hit unit counts offered hits.** `Interpreter.UnitAmount` uses `unit.Record.Record.Hits` (offered), so "every damage you
  take, heal 5" heals for hits that an absorb ate (22: 55, not 45). G3 says the unit is a field of the rule; whether "per Hit"
  means taken hits is unstated. Both hosts agree, so this is a reading, not a bug.
- **Card modifiers ignore the count** (above, missing 3). Contract clause 31 and plan 6 say cards add terms; nothing says per
  count. Both hosts fold once per held card.
- **Self emits run only for active targets.** A self emit is evaluated in Flow only for entities that have records this frame
  (`Interpreter.Step`: `active` from Partition). Consistent with clause 7 (Decide per active chunk), but it means sv-internal
  "every frame" effects do not exist; tl's Tick is the clock.
- **Emit when without a record.** An emit rule with per Record reading `source.X` gets the record's source; for `NoSource`
  records the source read is the value's schema default (`RefState.Intrinsic` for a non-slot), silently, not a fault.
  Only the reference was read here; the compiled form of this case is not exercised by a lego scenario.

## Not covered

Rewind of these scenarios (replay digests), overflow levels, kills and dying slots inside mechanics, reference-role intrinsics
other than Owner, chains of chains (`any`/`chain`), `nextFrame` self emits, emits per Frame, `over` channel filters.
