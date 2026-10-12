# Call to Arms

**STRONGLY INFERRED**, DKII.EXE v1.70, read-only Ghidra 12.1.4/PyGhidra in Claude's
project, 2026-09-29. No original run has been compared (Q483). Shipped record values
are CONFIRMED from `KeeperSpells.kwd`, `Shots.kwd` and `Objects.kwd`. Implementation:
`CallToArmsRules`, `CallToArmsSystem`, `KeeperSpellRules.CastAtPlace` (ADR-0279).

## 1. Claim audit

| Claim | Source | Result | Status |
|---|---|---|---|
| Spell 3 record | `KeeperSpells.kwd` id 3 | cost 10000, upkeep 90, no cooldown, location selector 4, target selector 4, shot 5 with data1 10, bonus shot the same | CONFIRMED |
| Upgrade changes only the upkeep | `0x004C17A0`, `0x004C10E0`, variable 227 | `UpgradedCallToArmsCostPercentage` (70) scales the per-second upkeep; the cast cost is the record's | STRONGLY INFERRED |
| Player cast path | action `0x36` (54) `0x005132D0` → `0x004BBD30` → `0x004BB8A0` | packet: spell id in the high word, position x/y in 4096ths; cost, cooldown, validators, shot, payment, both clocks | STRONGLY INFERRED |
| Location 4 | `0x00517F10` | no ownership: refuses only the navigation grid's solid bit, and tile flag `0x2000` when cast flag bit 1 is set | STRONGLY INFERRED |
| Target 4 at a place | `0x005179D0` | a room owned by the caster applies the Portal interior, Heart middle and Prison/Temple/Pit interior exclusions; anything else passes | STRONGLY INFERRED |
| The flag is an object | `Shots.kwd` 5, `0x004ADE10`, `Objects.kwd` 10 | shot 5 has one health and flag 4, dies on its first tick; its death process puts object 10 down at its tile, refused while the keeper's standing flag is in the hand | STRONGLY INFERRED |
| Flag process states | `0x0049E686` table entries 12–15, 19, 20 | 12 activates, 13 hands over, 14 recalls every second, 15 releases and destroys, 20 then 19 fade and destroy without a release | STRONGLY INFERRED |
| Activation | `0x004BDDA0` | flag bit 2, tile and tag written; a second flag while the first is held destroys the second; a standing first flag fades; the prepaid clock is set once; recruit walk | STRONGLY INFERRED |
| Prepaid clock | `0x004C1A90` | `CallToArmsManaTick = turn + (cost / upkeep) × ticks per second`, integer division: 111 s shipped | STRONGLY INFERRED |
| Upkeep | `0x004C17A0` | once the clock is past and the flag stands (`0x004C0090`), 90 (70 % upgraded) joins the second's drain | STRONGLY INFERRED |
| Unaffordable upkeep | `0x004C10E0` | after the payment, upkeep above the mana left sends the flag to state 20 and returns; the release for a missing flag sits inside a guard that requires the flag | STRONGLY INFERRED |
| Removal | action `0x3F` (63) `0x005136E0` → slap action 6 `0x004C0FE0` → `0x0049C2A0` | the keeper's own flag is thrown and set to state 15; refused in states 12, 15, 19, 20 or while ending | STRONGLY INFERRED |
| Release | `0x004BDFC0` | clock and bit cleared; owned non-workers under the call, not captive or joining, idled and their bit cleared when idle took | STRONGLY INFERRED |
| Heeding states | `0x00455DA0`, `0x00455EA0`, `0x00455F30`; hooks `0x00455E70`/`0x00455E90` | below | STRONGLY INFERRED |
| Readers | `0x004888C0`, `0x0045A6D0`, `0x00478800`, `0x004778E0`, `0x00477B40`, `0x004E8DA0`, `0x004752B0` | below | STRONGLY INFERRED |
| Message 84 | table `0x006A0B00` row 84, `0x00455DA0`, `0x004778E0` | flags `0x2032`: fifty seconds, tab, no duplicates; Data1 and Data2 zero | STRONGLY INFERRED |

## 2. The cast and the flag

The position helper `0x004BB8A0` wants the spell entry, cost at most the mana
(`0x004C1740`), no cooldown left, then the selected validators. The cast helper
creates the record's shot at the position, debits the cost, adds it to the one-shot
loss and stamps both clocks. Availability is not tested there; the interface gates it,
as for the other keeper spells.

Shot 5's death process (`0x004ADE10`) reads the object type from the shot's first
data word. For type 10 it first nudges the position a tile when the tile is a Heart
slab whose `0x00455000` answer has bit 0 or 1 (unread, not modelled); then, with the
keeper's call on, it makes no object when the standing flag is in the keeper's hand
(`0x004BCAE0`). Object 10 starts in process state 12 (its record's start state).

State 12 (`0x0049FBA0`) runs the activation and, on animation ended/wrapped flags
`+0xF1 & 6`, installs art5 and moves to 13. State 13 (`0x0049FC50`) sets the recall
deadline one second on and moves to 14. State 14 (`0x0049FCB0`) reruns the activation
whenever the deadline is past, every third time with an effect; an ending flag moves
to 15. State 15 (`0x0049FD80`) waits for completion of the slap's art7, releases the creatures and zeroes the object's health
(`0x004B2AD0`), which the cleanup pass removes. States 20 (`0x0049FDC0`) and 19
(`0x0049FE40`) install art6, mark ending and wait for its completion before the
same health zero, with no release. Selectors and timing are built; see
[object-drawing.md §8](object-drawing.md#8-furniture-eggs-and-chickens).

## 3. Activation, recruit and release

`0x004BDDA0(player, tile, tag)`: with the call on and a different tag, a held old flag
destroys the new one and returns; an existing old flag is set to state 20 and the bit
cleared. Then bit 2 on, the tile and tag stored, and `0x004C1A90` when the clock is
zero. The recruit walks the owner's creature list: not a worker, the owner's tag, not a
Reaper on its `+0x2F5` timer, and either (not joining 193, category through a walk not
22 or 26, not a captive) or `TortureVoluntary`; not already bit 20. A voluntary torture
victim is first set to 135 with its job ids cleared (not modelled). A route to the tile
(`0x004D0E60`) and a taken `SetState(190)` set bit 20. Held creatures sit in state 78,
which refuses 190; ours holds idle, so the hand is tested explicitly.

`0x004BDFC0(player)`: clock zero, bit off; owned non-workers with bit 20 or under
`0x004888C0`, not captive or joining, `SetState(1)` and, when it took, bit 20 off.

## 4. The heeding states

190 (`0x00455DA0`): the owner's bit off ends it with bit 20 cleared; otherwise
`0x00471890(tile centre, 191, 0)`; a refused route posts message 84 to the owner,
clears bit 20 and ends. 191 (`0x00455EA0`): bit off ends it; a creature whose tile
differs from the flag's on **both** axes gets `SetState(190)`, and `SetState(192)`
follows unconditionally. 192 (`0x00455F30`): from a random direction of four (up,
right, down, left, table `0x006BC350`) the first neighbour within a Chebyshev distance
under three of the flag's tile whose navigation cell is open takes a walk to one of
nine sub-tile spots (`0x00698EC8`, a three-by-three grid) resuming 192; none clears bit
20 and ends. 192 never tests the bit. The category's enter hook `0x00455E70` sets the
run speed and the leave hook `0x00455E90` takes it off (`0x004887D0`). Idle 89
(`0x004778E0`) clears bit 20 and, with the bit on, walks to the flag resuming 191 and
sets bit 20, or posts message 84.

## 5. What reads the call

- `0x004888C0`: the owner's bit, the creature's bit 20, or 190–192 through a walk.
  Read by the needs' busy test, the alarm, the threat responder and the drop landing.
- The fight gate `0x0045A6D0`: with the bit on and no force, per-axis absolute
  differences from the flag's tile centre, squared, shifted down twelve and summed,
  strictly above `CallToArmsFightDistanceTiles`² × 4096 refuses (variable 182, five tiles).
- The job dispatcher `0x00478800` admits only jobs 10 and 16 while the bit is on.
- The drop landing `0x004E8DA0`: a keeper's creature with no room idles instead of 250.
- The Scout's alarm `0x004752B0` is refused while the bit is on.

Once the flag has faded, `0x004C0090` is false: no upkeep, and no release. The bit
stays on, jobs stay refused and idle creatures keep walking to the old tile until a
new cast replaces the dead tag. This is the code's consequence, unobserved (Q483).

## 6. Built and not built

Built: the cast command (action 54) with selectors 4/4, the Create Object process, the
flag's process states, activation, recruit, release, the slap (action 63), the prepaid
clock and the upkeep, the unaffordable fade, message 84, states 190–192 with the run
speed hooks, and the five readers above. The observation (schema 9) carries each
creature's `0x004888C0` answer and the keeper's call.

Not built: the Heart-slab nudge, the flag's effects and sound, the thrown flag's flight
on a slap, `TortureVoluntary` recruits, the Reaper timer, and the unread order between
DK2's object and creature updates (ours runs creatures first, then flags, and chains a
flag's states). The hand's own relocation of the flag works through the recall reading
the flag's current tile. Selectors other than 4/4 and spells other than 3 at a place
are refused. Q483 asks for the original run: the flag's tile against the click, the
prepaid period, the milling radius and what a keeper out of mana is left with.
