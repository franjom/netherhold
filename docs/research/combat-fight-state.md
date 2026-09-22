# Mechanic: the fight itself — `spFighting`, the swing's two clocks, and the target's answer

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-22. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `Creatures.kwd` from the reference install, for the two clocks' shipped values

Original version: DK2 v1.70 (GOG)

Questions: advances Q8; answers the mechanism half of Q327; opens Q367–Q371
(Q367 answered in combat-fear.md, which also corrects §1 and §2 step 6 here).
`combat-acquisition.md` owns who is picked; `combat.md` §6 and `combat-damage.md`
the blow's arithmetic; `combat-cadence.md` the two clocks' fields. This is what
happens between them: the state that holds a target, walks up to it, swings and
listens for the target's answer.

**What this settles:** a melee swing is a creature *action* that winds up for
`def+0x5CA` ticks and lands once, started at most every `MeleeRecharge` ticks;
the target it lands on is the one the swing began at; a struck creature turns
on its attacker only when its own target is already double-teamed; and fear —
read here in outline — is combat-fear.md's, built with ADR-0165.

---

## 1. State 199 hands to state 200

State 199's handler `0x0045EB20` is a thin prologue: it drops a dead target
(unlinking from its arrays for creature targets, `0x0045E450`), and on the turn
the state was entered (`state+0x18` equals the game turn) it **sets the run
speed** (`0x004887D0(creature, 1, 0)`, the speed-mode setter — combat-fear.md §6) and, unless the attack is at an ally, raises
**player event 6 at the creature's tile** (`0x004C4600(world+0x2E54, 6, owner,
x, y, 1)`) — the "your creature is fighting" notice. Then it tail-calls
**`spFighting` `0x0045F030`**, DK2's own name (combat-entry.md §1).

## 2. `spFighting`, in order

`this` is the state object: `+0x8` the creature, `+0xC` the world, `+0x10` the
move-option word, `+0x18` the turn entered. `EDI` is the engagement record
`creature+0x4FE` throughout (combat-entry.md §6).

1. **A dead target is dropped**, as in §1. Two steering body bits are cleared
   (`body+0x10E` bit 7, `+0x10F` bit 0); the approach sets the first again (§3).
2. **Assertions**, then return 0: possessed; not a fighter (`Flags +0x46F` bit 0)
   and not `AttackAbnormalTargets`.
3. **No target → `SelectAVictim`**; success returns 1. Otherwise an AttackType
   nibble of `0xF` — set by §6 on every attacker whose target has died — goes to
   `0x00460FE0`, the victory arm (§6); anything else returns 0.
4. **Standing in a room** (`tile+0x2` room word) notifies it (`0x004E7FE0`).
5. **Target validity.** A thing target (nibble ≥ 2) must be live with health
   `+0x40 > 0`; a creature target must be live (else the *"attacking invalid
   tag"* assertion), `+0x16 ≠ 2`, and — unless I am a `CombatPitFighter` —
   `IsAttackable`. Invalid: a fighter unlinks and clears the target and goes
   on with none; an `AttackAbnormalTargets` creature clears and returns 0.
6. **The fear checks**, run only when `0x00460C50` finds an **escape**: a
   neighbouring tile I can route to that holds no hostile creature attacking me
   (combat-fear.md §3.1; an earlier reading had this inverted). Then the withdraw
   check `0x0045EDA0` — under a third of health, more than five tiles from my
   heart, the state a turn old — hands to the withdraw state `0x004611D0`; else
   the counter `0x0045EC80` over `CurrentFear` (`+0x526`, filled by `0x0045E4F0`)
   against the level-scaled `Fear` sends the creature into state **203** with its
   `BackOffTimer`. All of it is combat-fear.md §3; **built (ADR-0165)**.
7. **Busy** (`0x00496D00`: current action `+0x3A9` nonzero and not 21) → return 1.
   A swinging creature neither moves nor looks.
8. **Support re-acquires every tick**: a fight type 4 creature without
   `AttackAbnormalTargets` unlinks, runs `SelectAVictim`, keeps a new target,
   and restores the old one if none is found (the *"failed to re-aquire"*
   assertion is this path failing to restore).
9. **In reach?** `0x0045E8F0`: `d < AttackRangeSquared` **and** line of sight
   (`0x004512F0`, flag 3 set when a door stands on the target's tile). Yes → §4.
10. **Out of reach.** Not busy; then, for a creature target without
    `AttackAbnormalTargets`, **unlink and run `SelectAVictim` again** — every
    tick out of reach. No target → return 0 (the state ends). A target whose
    state is en route to a threat or fleeing keeps `+0x53D`, else it is cleared.
    Then **the approach** `0x00471D10(state, target, AttackRangeSquared,
    options)`: §3. Its failure for a spell nibble (0 or 2) with line of sight
    tries a point part-way to the target (`isqrt` of the distance, one to
    four steps) that the terrain flag `+0x17B` bit 8 accepts, and routes there.

## 3. The approach (`0x00471D10`)

Copies the target's position; `0x004B35C0(creature, &pos)` must accept it; a
**route request `0x004D0F60(body, creature, &pos, 2)`** to that exact position;
for a door target that fails, the four adjacent offsets in the table at
`0x0069A070` are tried with `0x004D0E60`, the first that routes becomes the
destination. Then **`SetState(8)`** — state 8, "en route", handler
`0x00470E30`, the one carrying the fear-threshold string — with the state's
fields filled: options at `+0xDF`/`+0xEB`, the destination at `+0xE3`/`+0xE7`,
the target tag at `+0xEF`, the range² at `+0xF1`, and 10 at `+0xF5`. So walking
to a fight is its own state, which returns to 199 when it arrives; its body is
**unread (Q368)**. Ours re-requests the route only when the target changes tile.

## 4. The swing, two clocks

In reach, `0x004B2AE0(&out, &targetPos)` must return a zero word (a region
test, UNKNOWN). Then the **AttackType switch** (`0x004602C0`): nibbles 0 and 2
→ the spell arm `0x0045FB65`; 3 → the ally-spell arm `0x00460011`, which casts
and then clears the target; **1 and 4 → the melee arm `0x004600DD`**:

1. **Gate B, `0x0048C9B0`**: `now ≥ TimeLastUsedMelee + MeleeRecharge`, the
   stamp `+0x427` by DK2's own name and the interval in ticks
   (combat-cadence.md §3), divided by the haste/slow word at `+0x3AF`/`+0x3B3`
   (not modelled). Not yet → return 1, standing still.
2. **Start action 17** (`0x004969D0(creature, 0x11, 1, target, 0, 0)`; failing
   asserts *"failed to set melee instance"*). Then `AttackCreatureHits`
   (`+0x4D9`) is incremented, **`TimeLastUsedMelee = now`**, and for a creature
   target **the target is told** (§5, `0x0045AA20(target, myTag, 1)`).
3. **Gate A**, in the action's handler `0x00498E30` every tick the action is
   current: `now ≥ +0x3A5 + def+0x5CA`, where `+0x3A5` is the action's start
   stamp (creature-action-table.md §3) and `def+0x5CA` is file `0xCF9` in
   ticks — **`MeleeHitDelay` here, shipped name Q336**. Not yet → return 1 and
   keep the action. Bile Demon 5 ticks, Goblin 3, Troll 1, Imp and every
   unique 0.
4. **The blow lands once** on `+0x399`, the tag the action was started with,
   **with no range test**: only liveness and `+0x16 ≠ 2`. A sleeping creature
   (state 83) is woken first (its bed's `+0xF3` bit flipped, its action ended,
   state 84 or 1). `+0x445 ≠ 0` on the target ends the swing with nothing.
   Then the arithmetic of combat-damage.md, `0x004B2CE0(target, delta,
   attacker's owner, 0)` — **the health routine takes the attacker's player id
   as the source**, which is what stamps a heart's killer — and the routine
   returns 0, ending the action.

So the period of a melee attacker standing in reach is `MeleeRecharge` ticks,
the blow landing `MeleeHitDelay` ticks into each; a fresh creature's stamp is
0 from the constructor `0x004872A0`, so its first swing waits `MeleeRecharge`
turns from the start of the level.

**Spell arms**, read and not built: `0x0048C800(creature, def, slot)` must
allow; action 16 starts with the slot and target; `+0x41B + slot·4` is stamped
with the turn; the target is told with flag 0. A spell arm that cannot cast
falls to `0x0045FEA0`: within a quarter of `AttackRangeSquared` and of sight²
it **steps back** by a vector from its heading (`+0xF4`) scaled by
`def+0x5BC` (`0x00460DA0`, `0x004B1F60`), else sets the run speed.

## 5. The target's answer (`0x0045AA20(target, attackerTag, melee)`)

Called on the target when a swing or cast begins at it. Not when the attacker
is an object (thing class 3).

**Target already in the combat group (22):** returns if fleeing (203) or the
gate `0x0045A6D0(0)` refuses. Otherwise decide whether to **turn on the
attacker**: yes when the target has no live creature target of its own; no when
its target *is* the attacker; and when its target is someone else, **yes only if
that someone has more than one attacker** — a walk over both arrays stopping at
the second nonzero slot. Turning: unlink from the old target, `+0x502` =
attacker, and unless struck by melee or being Support, survey the three spell
slots for an attack spell (bit 0 of the spell's `+0x27`, level allowing) — the
first found is used with nibble 0 and range², else melee with nibble 1 and
`(MeleeRange + attacker.FootprintRadius / 2)² >> 12`. Registration
(`0x0045A9A0`) failing clears the target. Then `SetState(199)` unless already in
the group, or en route toward 199.

**Target not fighting:** if the gate `0x0045A6D0(0)` allows, the target is not an
Imp, the attacker is a creature and a route to it exists (`0x004D0E60`, no door
rule): **`SetState(1)`** and route to the attacker's position (`0x00471890`,
`0x80`, Imp `0`). It walks toward whoever hit it, and acquires in its own time.

## 6. When the target dies

`0x0045EE60`, called on a creature's death, walks its own two arrays: every
attacker whose `+0x502` is the dead creature has that cleared, its `+0x3F9`
handled, and — **when the dead creature's health is ≤ 0** — its nibble ORed to
`0xF`. Next `spFighting`, a nibble of `0xF` with no target reaches the **victory
arm `0x00460FE0`**: a creature with `NeedBodyForFightIdle` (`def+0x686` bit 20)
routes to the nearest neutral-player thing within `0x2000` and remembers it at
`state+0xA7`; anyone else enters **state 260, "celebrating"**, and starts
action 55 on the spot. Not built (Q369); ours stands idle.

## 7. Built and not built (ADR-0164)

**Built:** `FightRules` and `CombatSystem.Fight` — §2 steps 1, 3, 5, 7, 9, 10
and §3's route request for melee; §4's two gates with `MeleeHitDelay` parsed
from `0xCF9`, the blow through `MeleeDamageRules.Blow` with no stance, the
bearing from target to attacker and no player modifier, landing on the swing's
own target without a range test; §5 for melee, with the spell survey replaced by
melee; the four fighting arms' scores (`CombatRules.Score`) so every fight
style but `NonFighter` acquires; and **the heart as a target**, hit through
`WorldState.DamageHeart` with the attacker's player as the source (Q363: the
heart tile stands in for the Arch Pillar).

**Built since (ADR-0165):** the fear checks, the flee and withdraw states
(combat-fear.md). **Not built, named:** state 8's own body (§3, Q368); the victory arm (§6, Q369); the spell
arms and the step-back (§4); haste and slow on Gate B; waking a sleeper on the
blow; `+0x445`; the room notice and player event 6 (§1); the NonFighter arm.

## Confidence

**CONFIRMED (binary):** §1's prologue and event; §2's order and every test in
it, including `0x00460C50`'s neighbourhood walk, the withdraw conditions and the
health-third of `0x0048F250`, the fear counter's clamps, threshold and flee
state; §3's request, door offsets, state 8 and the fields filled; §4's switch
table read entry by entry, both gates and their stamps, the action start, the
blow's liveness-only test and the source argument; §5 in full; §6's unlink,
the `0xF` nibble, state 260 and action 55; the names `TimeLastUsedMelee`,
`FearlessUntil`, `AttackCreatureHits`, `NeedBodyForFightIdle`.

**CONFIRMED (data):** `0xCF9` and `0xEF8` ticks for the seven creatures tabled
in combat-cadence.md §3.

**STRONGLY INFERRED:** that state 8 walks to the destination and returns to
199; that `+0x526` is a fear accumulator (its threshold and name point that
way; its writer is unread).

**UNKNOWN:** Q368–Q371 (§7); Q367 is answered in combat-fear.md.
