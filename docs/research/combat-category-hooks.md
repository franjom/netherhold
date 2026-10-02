# Mechanic: the Combat category's enter and leave hooks

Source(s):
- `DKII.EXE` v1.70 (GOG), disassembled with Ghidra 12.1.4 on 2026-10-02: the enter hook
  `0x0047A810`, the leave hook `0x0047A980`, the fight-leave routine `0x0045ADC0` and every
  helper they call; field writers and readers scanned over `.text` and `cseg` (`StoreAt`,
  `ReadsAtMulti`, positive control: the hooks' own stores). Facts in our own words (ADR-0046).
- Our side: a temporary census of every move out of category 22 over the tests, the 40
  scenarios and both replay rigs (seeds 1–20), on `2c6ea95`.

Original version: DK2 v1.70 (GOG)

Questions: completes three slots of `state-category-hooks.md` §2. The update hook
`0x0047A870` (combat experience) is not covered. ADR-0319.

Confidence: **CONFIRMED (binary)** for §1–§3; §5's mapping of our collapse onto DK2's order
is **STRONGLY INFERRED**.

---

## 1. Enter, `0x0047A810`

`ECX` is the state object (creature `+0x24E`); every call and store goes through its `+0x08`,
the creature. In order:

1. `0x0048ECE0`, the stop (`Creature_StopMoving`).
2. Word `+0x3D5` = 0.
3. If an action runs (`0x00496D00`: `+0x3A9` nonzero and not 21), end it (`0x00496D20`). The
   end tests the action record's byte `+28`, which is 1 in all 81 records
   (`creature-action-table.md` §1), so any action but 21 is ended.
4. Words `+0x95`, `+0x97`, `+0x99`, `+0x64`, `+0x68`, `+0x66` = 0.
5. Return 1. It never refuses an entry.

## 2. Leave, `0x0047A980`, and the fight-leave routine `0x0045ADC0`

The leave hook calls `0x0045ADC0(creature)`, then `0x004887D0(creature, 0, 0)`, then sets
word `+0x3D5` = 0.

`0x0045ADC0` takes no stack argument: it decides from the creature alone.

1. `0x0048F3F0`: `AttackingId` `+0x3F9` = 0 and `AttackAbnormalTargets` cleared, then the
   unlink `0x0045EF50` (my slot on my creature target freed, `+0x502` = 0). **Every exit from
   Combat drops the creature's own forced target.**
2. An inline copy of that unlink, dead because `+0x502` is already 0.
3. Each of my sixteen attacker slots (ranged `+0x514`, then melee `+0x504`, per index): an
   attacker whose `+0x502` is me gets the victory nibble (`+0x529 |= 0xF`) **only if my health
   `+0x40` is at or below 0**, has `+0x502` cleared, and with `AttackAbnormalTargets` loses its
   forced target too (`0x0048F3F0`). The slot is cleared either way.
4. `+0x232` bit 7 and `+0x233` bit 0 cleared.

Two list walks before and after store nothing. The route helpers `0x00471890` and
`Travel_RouteToRoom` also call `0x0045ADC0` when a walk takes a category-22 creature to a
resume outside 22 (`travel-states.md` §2), because entering a walk runs no hook.

## 3. The fields

| DK2 word | DK2's name | Writers and readers (scan) | Ours |
|---|---|---|---|
| `+0x3D5` | `ReportingTo` (dumper) | written 0 by the constructor and both hooks; read only by the dumper | not modelled; nothing to model |
| `+0x64`, `+0x66`, `+0x68` | none | three angles mod 2048: `0x004A21C0` adds `0x40` to each, torture sets `+0x64` = `0x600`, the hand pick-up and torture clear them | not modelled. **Not** `CurrentlyDoingJobId`, which is state `+0x64` (creature `+0x2B2`) |
| `+0x95`, `+0x97`, `+0x99` | none | a signed triple: torture and `0x004A7040` write it, `0x0049B9F0` reads it as three ints | not modelled |
| `+0x502`, slots `+0x504`/`+0x514` | — | the engagement and the attacker slots | `FightTarget*`, `EngagedMelee`/`EngagedRanged` |
| `+0x3F9`, `+0x3E6` bit 2 | `AttackingId`, `AttackAbnormalTargets` | the forced target | the same names |
| `+0x232` bit 7, `+0x233` bit 0 | none | `+0x233` bit 0 is the acquirer's hero bit (`combat-target-selection.md` §3) | not modelled |

**Speed mode.** `0x004887D0(0, 0)` is mode 0, walk, with no explicit speed, so the
definition's walk speed (`hero-party-walk.md`): ours is `Running = false`. It also ends a
hero party's shared pace, which ours keeps in `PartySpeedRaw`.

**The current action.** Ours has no one field for `+0x3A9`: the swing (`SwingInProgress`),
the idle, cheer and prisoner flourish actions (`ActionUntilTick`) and the tunnel's dig
(`Tunnel.ActionUntilTick`) stand in for it.

## 4. The routes out of category 22

From the census; "before" is `2c6ea95`. Category 22 holds 199–203, 251, 252, 259 and 260.
Since ADR-0319 every row runs the leave hook. New on every row: the attackers released
(§2 step 3) and the forced target dropped; new on the rows "no" in the third column: the
engagement unlinked and the walk speed.

| Route (ours) | Moves | Through `Disengage` before | Left set at the move |
|---|---|---|---|
| The fight ending: nobody found, cannot continue, the forced target gone, the pit knockout, and (§6) an invalid target, out of reach or cannot cast with no new target | 199 → 1 | yes | attacker slots |
| The approach ending | 199 → 1 | yes | attacker slots |
| The flee or withdraw handler returning 0 | 203 → 1 | yes | attacker slots |
| Stun and freeze (`StatusEffectRules.Stand`) | 199 → 1 → 255/218 | yes | attacker slots |
| Defeat (`DefeatRules`) | → 1 | yes | — (not reached in the corpus) |
| The collapse | 199/203/260 → 216 | no | target, run, attacker slots |
| The hand lifting a fighter | 199/203/260 → 75 | no | target, run, slots, a swing |
| A heal shot's `SetState(1)` | 199 → 1 | no | target, run |
| The hero party's knockout | 199/260 → 1 | no | target, run |
| A hero objective from a trigger | 199 → 1 | no | target, run |
| The withdraw's arrival | 202 → 1 | no | sometimes the target |
| The alarm's arrival and standing down | 251, 252 → 1 | no | attacker slots |
| The victory pause ending | 260 → 1 | no | attacker slots |
| Into a walk resuming 202, 251 or 259 | 199/203 → 6 | — | no hook, and no `0x0045ADC0`: the resume is in 22 |

Call to Arms and the pit's room reset reach no Combat state in the corpus. No walk in the
corpus takes a fighter to a resume outside 22, and no hero party's pace was set at a leave.

## 5. The collapse gives no victory

The health routine `0x004B2CE0` loads the dying countdown into health `+0x40`
(`0x004B2FB2`) **before** it calls the collapse `0x0048B770`, which enters 216 through
`0x004596D0`. So the leave hook sees health above 0, and a fighter that collapses releases its
attackers **without** the victory nibble. ADR-0213's pit knockout is this rule, not a pit
special. DK2 gives the nibble at death instead: `Creature_DeathBookkeeping` calls `0x00487C00`,
which runs `0x0045E4A0` over every attacker at health 0 or below.

Ours books the collapse up to a game second after health reaches 0 and keeps health at or
below 0 while dying. So the hook's test is ours as: health at or below 0 **and** no dying
state (`CreatureDeathRules.HasDyingState`), the creature that dies outright.

## 6. A fight re-selects inside 199

`spFighting` lets an invalid target go and seeks another (§2 step 5), and out of reach unlinks
and runs `SelectAVictim` again (step 10), without leaving 199; only finding none returns 0, and
the dispatcher's `SetState(1)` then runs the leave hook (`combat-fight-state.md` §2). The
cannot-cast arm does the same (`combat-spells.md` §9). Ours went through state 1 and back on
all three, which with the hooks built would release every attacker and end the cast on each
re-selection. Since ADR-0319 ours unlinks in place, and only a failed re-selection ends the
state. The alarm and the withdraw enter 199 before they route (`combat-alarm.md` §2); ours
probes the route first, so it routes again after the enter hook's stop.

## 7. Built (ADR-0319)

`CategoryHooks`' Combat enter and leave slots, `CombatRules.EnterCombat`, `LeaveCombat` and
`LeaveFight`; the route helpers' call in `CreatureStateMachine.BeginTravel`. Not modelled: the
words of §3 that have no counterpart here. Both slots are therefore Partial.

## Open questions

- **When an attacker gets the victory nibble:** Q511.
- **Our unlink clears more than DK2's.** `0x0045EF50` writes `+0x502` and one slot; ours also
  clears the attack type, the reach and the approach flag.
