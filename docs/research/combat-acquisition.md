# Mechanic: how a creature acquires an enemy — `ProcessFight`, `SelectAVictim`, the fight-starter

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-22. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `Objects.kwd` (object id 94) and `Creatures.kwd` from the reference install

Original version: DK2 v1.70 (GOG)

Questions: advances Q8; opens Q360–Q364. `combat-target-selection.md` owns the
five scoring arms this routine dispatches to; `combat-entry.md` the state and
the AttackType switch that follows; `combat.md` the swing.

**What this settles:** the acquirer has DK2's own name, **`SelectAVictim`**, and
it is not one scan but six; a creature looks for a fight every tick it moves
and every tenth tick it stands; and **the enemy dungeon heart is acquired here**,
as the nearest *Arch Pillar* of a heart room that is not yet a ruin.

---

## 1. Where in the tick

DK2's creature update `0x00488930` (`Creature.cpp`) runs, in order: the fight
bookkeeping — the remembered-entity countdown at `+0x537`/`+0x539` and the
tracking timeout at `+0x53B`, cleared when `+0x53E` reaches 60 — then
**`ProcessFight` `0x0045E9F0`** at `0x00489AC7`, skipped for a `Possessed`
creature; then `0x0045E4F0`; then the **state handler** through the state table
(`0x00476FC0`, at `0x0048A030`); then the **attackability recompute**
`0x0045A8B0` (§4); then the mover (`0x004D59A0` and its neighbours). So
acquisition precedes both the state machine and steering on the same tick, and
`CombatSystem` sits before `CreatureMovementSystem` and every job system.

Creature order (rechecked 2026-10-10): `0x004B7830` runs complete creature
updates newest first, because `0x004B4A10` links at the class-list head; see
[imp-task-stealing.md §8](imp-task-stealing.md#8-update-order-q6). Our combat
pass now also runs newest first (ADR-0429). Original updates interleave each
creature's state and movement; ours retains separate system passes.
No extra Imp-job acquisition call exists among SelectAVictim's twelve callers.

The transition adds no missed scan (binary recheck, 2026-10-11):
`0x0045E9F0` has one direct caller, at`0x00489AC7`, and no absolute pointer
occurrences across the image. Its movement test compares tile coordinates,
not sub-tile displacement, velocity or route creation; modern `ScanIsDue`
matches its counter4 Imp threshold. State6 (`0x00470C80`) moves the Imp after
that scan and does not scan again. Claim work16 (`0x00465CD0`) holds while an
action runs, then validates its task and floor or enters10; it neither scans
nor changes the scan counter. A new job route whose first step stays in the
same tile therefore has no additional acquisition opportunity on that turn.
Claim completion's modeled action hold already has original capture evidence
(mining-cadence.md §6); this read does not establish a job-timing correction.

Owner's original Level7 experiment (2026-10-11): the first claiming Imp flees
when the Giant targets it from an estimated four to five tiles; the Giant
never swings at or kills it. Both working and walking Imps react this way.
This is an observation, not a tick/position capture. Binary scan C (§6)
explains the trigger without changing the ordinary Imp's one-tile sight cap.

Modern Level7 seeds1 and7, hash228: Giant1 registers on Imp27 at142, distance
3.478 tiles. Imp fear reaches160 at143, but absent scan C it keeps traveling
and claiming until withdrawing at153. The Giant starts its swing then and
kills it at156: 562 damage against500 HP. With scan C, acquisition is144,
flee state203 is145, withdrawal146; the Giant drops its target at156 without
ever swinging, and the Imp retains500 HP at180. These are modern probes;
matched original timings and broader casualty parity remain Q582. Hash229's
later deaths (seed7 at237, seed1 at286) exposed the fixed Portal-centre alarm
route; combat-alarm.md §2 now supplies its original random-point selection.
Hash230 still loses Imp27 later: seed1 at244 and seed7 at266, after it returns
to work. Those later approaches need matched original captures; the correction
does not establish full casualty or damage parity. Route
velocity reset remains unbuilt (steering-behaviours.md §7.6); the swing has no
impact-time range test (combat-fight-state.md §4).

`ProcessFight` is one of twelve callers of `SelectAVictim`. Six sit inside
`spFighting` (re-acquisition); one in the possession handler; one at
`0x0048A429`, a same-tile enemy-door loop that sets `AttackAbnormalTargets`
and `AttackingId` before calling with `force`, not a Giant-victim notification.

## 2. The gate: may I look for a fight? (`0x0045A6D0(force)`)

Returns 1 only when every test passes. DK2's own field names throughout.

| test | our mapping |
|---|---|
| my state's table flag byte (`+0x18`, Q185) bit 6 clear — set on stunned 209/210/255/256/263/264, dancing 153, gambling 158, joining 193/278–280, idle 269/272/273 | no such states yet |
| not a captive: state group 14 or state 126 (`0x00473990`) | `PrisonRules.IsImprisoned` |
| not being tortured: state 136 or group 15 (`0x00485210`), unless `TortureVoluntary` | `CombatRules.OnTortureDevice` |
| state ≠ 76 (landing) and ≠ 83 (`0x004E0460`, the middle sleeping state) | landing state and sleeping activity excluded |
| `Leaving` (`+0x3E6` bit 14) → only with `force` | `Activity == Leaving` needs force |
| `+0x16 ≠ 2` | UNKNOWN (Q364) |
| `FreezeTimer` `+0x447` and `ChickenTimer` `+0x44B` both zero | `Activity` not `Frozen` or `Stunned` (ADR-0168); `ChickenTicks` zero (ADR-0302) |
| `Flags` `+0x46F` bit 0 — cleared with the state-242 change on the pick-up path, 9 at construction — or `force` | `!InHand` |
| not `Possessed` (bit 7), not `CreatureDying` (bit 21) | `IsPossessed`, `IsDown` |
| not en route (state 6) toward a threat (251) or fleeing (202), unless `force` | no such states yet |
| not `IsHypnotised` (`+0x3EA` bit 0) | not modelled |
| when the owner's `CallToArmsOn` is set and not `force`: within `CallToArmsFightDistanceTiles` (variable 182, byte at `0x006F2963`, ships 5) of `CallToArmsPosition` (`player+0x13A/+0x13C`) | `CallToArmsRules.WithinFightDistance` (ADR-0279) |
| an `IsHorny` creature: not in states 235–237 and `+0x2F5` zero | not modelled |

## 3. `ProcessFight`: when a creature looks

1. `+0x16 == 2` → nothing. Owner is the **neutral player** (`[0x006F2548]`) →
   nothing: **neutral creatures never look for a fight.**
2. Unless `AttackAbnormalTargets` is set, the gate of §2 with `force = 0`.
3. State 77, or state group 22 (the combat group every fighting and fleeing
   state carries) → already handled, return 1.
4. **`FightProcessCounter`** — DK2's dumper's name for the nibble at bits
   22–25 of `+0x3E6` — is incremented. If my tile differs from the tile of the
   position `vtable+0x2C` returns (STRONGLY INFERRED the previous frame's
   position: the same comparison flags "entered a new tile" for the hero
   room-destroyer at `0x00488F6A`) → look now. Standing: look when the counter
   reaches **10**; an Imp (`+0x36F == 1`, by literal) when it reaches **4**;
   otherwise return 0. The counter is cleared before the call.
5. `SelectAVictim`; its result is the return.

Ours: `CombatRules.ScanIsDue`, with `LastFightScanTileIndex` for the previous
position.

## 4. `IsAttackable` is recomputed every tick (`0x0045A8B0`)

Bit 6 of `+0x3EA` is not stored state but the result of this routine, run after
the state handler. It is **cleared** when any holds: `+0x16 == 2`;
`CombatPitFighter` (bit 15); state 76; `CreatureDying`; `vtable+0x28` true;
captive; tortured unless `TortureVoluntary`; `Leaving`; `InvisibleTimer`
`+0x44F` nonzero; `Flags` `+0x46F` bit 3 clear; the state's flag byte bit 2 —
set on idle 75/76/235–237/242/269/272/273, **the lair walk 79–81 and all three
sleeping states 82–84**, dancing and gambling; or **all eight melee and all
eight ranged slots taken** (§7). Otherwise set.

So a sleeping creature cannot be attacked, as read (Q360 asks the owner). Ours:
`CombatRules.IsAttackable` — down, captive, leaving, held, the lair walk and
sleep, sixteen attackers.

## 5. Seeing and reaching

**Sight, `0x004512F0(map, &a, &b, 0, 1)`.** Both points shift left four
(16.16, tile = `>>16`). While the moving point is not in the target's tile: the
parameter to the next x and y boundary is computed in 2.14 (`(dist << 14) /
delta`, the backward distance `-(frac) - 1`); the nearer boundary is stepped to
and the other axis advanced by `(t · delta) >> 14`; if the step lands exactly on
the other boundary too, or both parameters are equal, it is a **corner**: the
two tiles the line brushes — (new column, old row) and (old column, new row) —
are asked, and only if both are clear the diagonal tile itself. Otherwise the
tile entered is asked. Any blocked answer ends it with no sight. **The tile the
walk starts in is never asked**; the target's tile is, when entered.

The per-tile test `0x004511A0(x, y, 0, 1)`: the map's **navigation byte** (the
grid at `map+0x18` layer 4, `pitch·y + x`) with **bit 3 — `Solid`** — blocks;
then the tile's things: a door (thing type 4) blocks unless its definition flag
`+0x8B` bit 6 (a barricade) is set, which flag 1 looks over. Whether an open
door's thing stays on the tile is UNKNOWN; ours reads the door from the
navigation byte, so an open door is transparent.

**Reach, `0x0045AFF0(me, point, rangeSq)`.** Identical positions → true. The
route request `0x004D0E60(body, me, &point, 0x80 | 0 for an Imp)` fails → false.
The dispatcher `0x004D0E10` builds the waypoints (positions `>> 4`, mode −2,
budget `0x100`, `0x80` for an Imp); none → true. Each leg from the previous
point: `|dx| + |dy| − min(|dx|,|dy|) / 2` in 1/4096 units, summed to `len`;
**true iff `(len >> 12) · len < rangeSq`** — strict, so a walk of exactly four
tiles is out of a four-tile hearing. The search budget is not modelled.

## 6. `SelectAVictim` (`0x0045CB00`), 5,746 bytes

Its assertions name it and two fields: *"…is possessed but has called
SelectAVictim. AttackingSpecificCreature = %d, %d"* prints
`AttackAbnormalTargets` and `AttackingId` (`+0x3F9`); *"…is not a fighter (%d)"*
prints `Flags` bit 0; *"…is already attacking target %d"* prints `+0x502`.

**Setup.** `sight² << 12` and `hear² << 12` from `def+0x77B`/`+0x77F`; my side is
the Turncoat player `+0x455` when `+0x453` is set, else my owner; every best
starts at `0x7FFFFFFF` — **the score the arms are handed.**

**Spell survey** (skipped when the definition is `IsWorker`): the three slots at
`+0x41B` whose level requirement `0x004943F0(def, i)[1]` is met and whose spell
id is nonzero, and `0x0048C800(me, def, i)` allows **or** the creature is a
Support (`def+0x67E == 4`) that `0x00460C50` finds an escape for — the recharge
is waived for a Support with a tile to back on to (`0x0045CCB2`–`0x0045CCD4`;
an earlier reading had it as a second requirement; ADR-0325); the spell
definition from the world's `vtable+0xE8`. Its
flag byte `+0x27`: bit 2 → a friendly spell (range `+0x31`, bit 7 kept as its
kind); bit 0 → an **attack spell**, the one whose next-ready time `stamp +
recharge` is latest kept (`0x004F7AB0` is the recharge in ticks, not a strength
— corrected 2026-09-23, `combat-spells.md` §5); bit 1 with target class 4
(`vtable+0x1B0(def+0x44)[0x6A]`) → a thing-targeting spell. The bits are named
from the roster in `combat-spells.md` §2. The attack survey is built (ADR-0167); the
friendly survey is built too (ADR-0170, `spell-friendly-arm.md`); the thing-targeting spell is not.

**The scans, in order.** Each keeps a running best and a tag; `d` is the
squared distance of `combat-target-selection.md` §2.

| | when | over | accepts |
|---|---|---|---|
| A | `AttackAbnormalTargets` set and `AttackingId` dead | every player's creatures (`[player+0x10FD]` is the game's player list; `+0x6`/`+0x10` its creatures) | attackable, not me, seen within sight (Imp: one tile) or reached within hearing (Imp: none); **no hostility test** |
| B | a thing-targeting spell | the neutral player's `+0x12` list | within sight and seen |
| C | always, unless a live `AttackingId` short-circuits | **my own `+0x504`/`+0x514` — whoever is already attacking me**, melee then ranged per slot | `Creeping` weighting (§6.1), alignment discount, strict `d < best`, attackable, **seen**, not `SneakingAs` my side; sight/hearing distance or **Imp**, and a route request succeeds |
| — | no result yet | `PossessedCreatureFollowing` `+0x482` → its `+0x4D0` | re-acquisition, no strategy runs |
| — | no result yet | **the fight-type dispatch** with the running best | `combat-target-selection.md` §5 |
| D | still none after the route check, not a Turncoat | each hostile player's `+0xC` list — **traps** (`player + 6 + class·2`, class 3), newest first: completed, revealed, not trap 16 (`+0xF6`); its own best from `0x7FFFFFFF` (traps.md §11) | `d <= sight²` and seen; an `IsWorker` only under `0x800` |
| E | none, **or the target is an `IsWorker` creature** (a trap skips it); not Turncoat, not `IsHorny`, not an Imp | each hostile player's heart: `0x004C5F50` = `player+0xB43` tile → centre; its rooms `+0x36`/`+0x8` of type 5 with `+0x9A` bit 0 **clear** (not a ruin); their objects `+0xE`/`+0x12A` with `TypeId` `+0x12C` == **94, Arch Pillar** | heart centre within sight and seen; pillar `d < best` |
| F | a friendly spell | own creatures in group 22 with health `+0x40` below a quarter of level-scaled `Hp` (`def+0x692`, table `+0xE1`), not `CombatPitFighter`, not captive or tortured, `0x0049A7A0` castable | seen or reached; then routed; AttackType 3 |

**Scan C's Imp exemption (CONFIRMED, assembly recheck 2026-10-11):** the
comparisons at `0x0045D35D` and `0x0045D56B` let TypeId1 bypass sight/hearing
distance for a registered attacker. Outer visibility and route checks still
apply; the Imp routes with word0x80, others0. No hostility filter runs here.
Registration itself does not change the victim's state. The accepted attacker
precedes NonFighter dispatch, letting combat-fear.md §3's existing immediate
NonFighter escape apply while its ordinary scan remains capped at one tile.

**Scan E is the heart's blow.** A creature that can see an enemy heart's centre
within four tiles attacks its nearest pillar, outranking a worker it had
already picked; the pillar is a proxy — the health routine hands its delta to
the owner's heart (Q363, `win-and-defeat.md` §3.1).

### 6.1 Two names from the dumper

`+0x3E6` bit 12, the priority weighting's gate (`combat-target-selection.md`
§2 step 3), is **`Creeping`**: a creeping enemy reads at `def+0x760` per cent of
its distance, half for every creature but the Horned Reaper's zero. And the
route-check option is DK2's `MoveOptions` word: `0x80` respects doors, an Imp
passes `0`.

### 6.2 After the scans

1. **Route check**, between the dispatch and scan D (`0x0045D704`–`0x0045D77F`).
   A creature target must answer a route request (`0x004D0E60`, `0x80` for an
   Imp, else `0`); failing that, a creature with an attack spell keeps a target
   it can see (no distance test), anyone else drops it and scan D looks on.
2. **Final gate** on a third of the distance: `d/3 < hear²`, or `d/3 < sight²`
   and seen. Never binding on shipped ranges.
3. **Commit.** `+0x502` = tag; `+0x529` = spell slot `<< 4` | old nibble. Then
   the nibble: **`d < 0x1000` (one tile) and not fight type 4 → melee (1)**;
   else an attack spell → spell (0, or melee if the target is a thing and the
   spell's bit 3 is set); else melee. A thing target: nibble 4 for melee, 2
   for a spell.
4. **Creature target:** `0x0045EDA0(me)` — UNKNOWN, probably the flee decision
   (Q364): false → the fight-starter (§8) and return 1; true → target cleared,
   `0x004611D0(state)` tried, else target restored and the fight-starter.
5. **Thing target:** `AttackRangeSquared` `+0x4FE` = `(MeleeRange + x)² >> 12`
   by the thing's class (`+0x12`): `0x800` for a door (4), `0xC00` for a secret
   door (flag bit 5), `0x400` for a trap (3, `0x0045E065`), `0x800` for an object
   (2) or any class but 0, 3, 4 and 6 (`0x0045E098`; a body, 6, takes its own arm); or the spell's range²; then
   `SetState(199)` unless already in group 22 and not en route to a threat.

## 7. The engagement record (`+0x4FE`…)

| offset | DK2's name | what |
|---|---|---|
| `+0x4FE` dword | `AttackRangeSquared` | how close the engagement must get |
| `+0x502` word | — | the target's tag; copied into the swing (`combat-entry.md` §6) |
| `+0x504` 8 words | — | melee attackers engaged with me |
| `+0x514` 8 words | — | ranged attackers engaged with me |
| `+0x529` byte | `AttackType` | low nibble the kind, high the spell slot |
| `+0x537`/`+0x539` | — | the remembered entity and its countdown (`steering-behaviours.md`) |
| `+0x53B`/`+0x53E` | — | someone retreating I track, and a 60-tick timeout |

The arrays are written by **`0x0045A9A0(target, myTag, ranged)`**: the first
free slot of the chosen array, false when all eight are taken. Every
disengagement walks both arrays of the other side and strikes its own tag out.

## 8. The fight-starter (`0x0045A550`)

Register me on the target (§7) under my nibble; if that fails for a spell,
retry as melee (nibble 1) — a Support creature only if `0x00460C50` allows; if
it still fails, **clear the target**. Then `0x0049AA30(target, 6, 0, 0)`: the
target's creature trigger tree is walked for **event 6** (Q366). With a target
left: spell → `AttackRangeSquared` = the spell's `(range >> 12) · range`; melee
→ **`(MeleeRange + target.FootprintRadius / 2)² >> 12`** (`def+0x63A` is the
footprint word, `steering-behaviours.md` §7). Then `SetState(199)` unless
already in group 22. With no target left, the nibble is restored.

## 9. Built and not built (ADR-0163)

**Built (ADR-0163, extended by ADR-0164):** `CombatSystem` running `ProcessFight`'s
gate and throttle; `SelectAVictim`'s five arms (the NonFighter since ADR-0166) —
the dispatch scan with each arm's score (`CombatRules.Score`), the alignment
discount, `IsAttackable`, sight and reach, the route check, the final gate, the
commit, registration, `AttackRangeSquared`, the fight-starter's event 6, and
`CreatureActivity.Fighting` for state 199; the spell survey and scan F
(ADR-0167, ADR-0170); scan C's registered-attacker precedence, distance exemption,
visibility, route and interleaved strict tie order; scan D and the fight against a trap (ADR-0340); **scan E**,
with three virtual Arch Pillars at the builder's exact sub-tile positions
(furniture-placement.md §6). Virtual Heart target ids encode slab and pillar
ordinal, so acquisition, approach, pending impacts and shot aim
resolve the same point. Equal-distance pillars follow the newest-first order
in furniture-placement.md §6. Virtual ids stand in for original object handles.
`LineOfSight` reproduces §5's walk; `CombatTargetRules.CanReach` §5's route sum.
What a fighting creature then does is combat-fight-state.md.

**Not built:** the `Creeping` weighting; the thing-targeting survey and scan B;
scan A; the Turncoat skips. `SneakingAs` is built (ADR-0446). The flee
check is built since ADR-0165 (combat-fear.md §3.4).

## 10. What is left

Q360 sleeping and lair-walk creatures unattackable (owner); Q361 the Call to
Arms radius; Q364
`0x0045EDA0`, `+0x16 == 2`, `vtable+0x2C`; Q365 the spell survey; Q366 creature
trigger event 6.

## Confidence

**CONFIRMED (binary):** §1's order within the creature update; every test in §2
and §4 and the state ids they name; §3's counter, its two thresholds and the
Imp literal; the neutral-owner refusal; §5's walk, corner rule, per-tile bit and
door test, and the reach sum with its strict compare; §6's setup, the six scans'
lists and gates, scan E's room type 5, ruin bit and object id 94, the dispatch,
§6.2's route check, final gate and nibble rules; §7's layout; §8's
registration, fallback and the two range formulas; the names `SelectAVictim`,
`FightProcessCounter`, `AttackingId`, `AttackAbnormalTargets`,
`AttackRangeSquared`, `Creeping`, `CallToArmsFightDistanceTiles`.

**CONFIRMED (data):** object 94 is *Arch Pillar*; room type 5 is the Dungeon
Heart; variable 182 ships 5.

**STRONGLY INFERRED:** `vtable+0x2C` is the previous frame's position; state 242 is the held creature; state 83 is asleep proper.

**UNKNOWN:** §10.
