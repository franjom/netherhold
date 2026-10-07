# The last category 0 states: room returns, the research cheer, and the unreached

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-10-07 by three read-only passes, each
  with a call scan (`SetState` `0x00476D30`, `Creature_KickToState` `0x0048AD30`, `0x00491830`, the
  walks `0x00471890`/`0x00471A70`), the shared push-and-jump tails, the register arguments traced by
  hand, a raw scan of `.text` for every immediate equal to the id, the `+0xDF` stores
  (`travel-states.md` §2.4) and the room table's `+0x19`/`+0x1D` (`portals-room-handler-table.md`).
- **Built:** ADR-0402 (60, 62, 70, 154). **Unreachable:** 0, 2, 61, 72, 73, 74, 207, 208, 212, 213,
  215, 245, 258, 262. **Left unbuilt by the owner's choice:** 158 (Q571).
- The dispatcher skips the handler of state 0 (`0x00477282`) and sets 1 when a handler returns 0
  (`0x004772B4`–`0x004772B7`).

## 1. 62, 70 and 154: back into a Library, Workshop or Casino that changed — CONFIRMED

Siblings of the Temple's 107 (`temple-states.md` §7), the Training Room's 113 and the Prison's 126
(`refused-states.md` §4, §5). Each kicks every place-holder of the room (head `+0x0A`, next `+0x238`)
from the room type's slots:

| state | handler | slots `+0x05` freed / `+0x09` tiles changed / `+0x0D` owner changed | back to |
|---|---|---|---|
| 62 | `0x00476320` | `0x004E17E0` (`0x004E17FA`) / `0x004E10D0` (`0x004E111B`, also slot `+0x11` after a load) / `0x004E1830` (`0x004E184A`) | 53 on its own keeper's Library |
| 70 | `0x00470C10` | `0x004F58A0` (`0x004F58D2`) / `0x004F5530` (`0x004F55CA`) / `0x004F5940` (`0x004F5972`) | 63 on its own keeper's Workshop |
| 154 | `0x00456FB0` | `0x004D7100` (`0x004D7120`) / `0x004D70D0` (`0x004D70ED`, slots `+0x09` and `+0x0D`) | 145 on its own keeper's Casino |

Each handler: at `Age` 1 or less, 1; then on a tile of its own keeper's room of the type
(`0x004539B0`), `SetState(back)` and 1; anywhere else 0, idle. 62 and 70 also end a running action
every turn first (`0x00496D00`, `0x00496D20`); 154 does not. The Workshop's kick loops end the
holder's action before the kick, the Library's do not; the Library's re-home books (`0x004E1590`)
and the Workshop's crates (`0x004F5630`); the Casino's freed slot releases its effect. All three are
also kicked by `Room_NeighbourTerrainChanged` `0x004E44E0` (jump table `0x004E4560`: Library pushes
`0x3E` at `0x004E4523`, Workshop `0x46` at `0x004E452B`, Casino `0x9A` at `0x004E453D`, to the shared
kick at `0x004E4544`).

**Ours.** `RoomEvictRules`: the room's size stamp at the holder's next turn, as for 107, 113 and 126;
the handlers are `RoomEvictRules.BackIn` from `ResearchStep`, `WorkshopStep` and `CasinoStep`. Not
built, as for the others: the freed and owner-changed slots (the job's room test idles the holder),
the load slot, `0x004E44E0`, and the books', crates' and effect's handling (Q575). The neighbour trigger is the common one in play: a wall
dug or a tile claimed beside the room. A jackpot winner leaves the Drink job (ADR-0396), so ours
does not evict it; DK2's kick reaches every place-holder in the room's list.

## 2. 60 and 61: the research cheer — CONFIRMED

**Entry.** `0x004E0870`, called only by the research adder `0x004C2DC0` at `0x004C2FC2` when an item
is done: once `0x004E1AC0` finds a shelf in the chosen Library, every place-holder of that Library is
kicked to 60 (`0x004E08B0`), then the item is granted by its kind (`0x004E0D70` rooms, `0x004E0BE0`
doors, `0x004E0A50` traps, `0x004E0F00` spells).

**60** (`0x004761F0`): at `Age` 1 a running action is ended. With art slot 8 absent (`0x00494860(8)`,
`+0xB` bit 0), 0. While an action runs, 1. Otherwise action 35 for `random(2) + 1` plays
(`0x004969D0`, simulation stream, line `0x1A5` at `0x00476248`); started, `SetState(1)`,
`SetState(61)` (`0x00476267`, `0x00476272`) and 0, so the dispatcher sets 1 in the same call.

**61** (`0x00476290`): while an action runs 1; with a research target (player `+0x213`)
`SetState(53)` and 1; otherwise 0. Its one entry is 60's, which leaves it in the same call (as 69,
`refused-states.md` §2): its handler never runs, and the cheering researcher is idle, playing action
35, and back to research only through the job choice.

Owner, 2026-10-07: in play the researchers cheer, wander off and pick a new job, research again if
it is available, which is this reading.

**Ours.** `ResearchStep.Cheer` in `ResearchTreeRules.Deliver`, before the item's grant, and 60's arm in
`ResearchStep`. The leave from the Research category releases the place. The action's length is the
counted clip (`AnimationTimingRules.CountedHoldTicks`), LIKELY. Not read: whether state 1's handler
cuts action 35 short.

## 3. 158: the Saturday-night dance — left unbuilt (Q571)

**Entry.** `0x004C5780`, which the player tick `0x004B9740` calls at `0x004B9B36` while world
`+0x723A` bit 0 is set: every creature of the player not in a group-1 state and not already in 158 is
kicked to 1 and then to 158 (`0x004C57BC`, `0x004C57C8`).

**The flag.** Its one writer, `0x0050A850` (called by the world turn at `0x0050A788`), clears it every
turn and reads the host's local clock (`GetLocalTime` through `0x005B5D20` and `0x005B5CD0`): it sets
it on a Saturday from 00:58:00 to 00:59:59 (`0x0050A8D7`), and at 00:58:00 exactly posts message 98
to the local player (`0x0050A8CA`). It returns at once, leaving the flag as it is, when
`[0x0075AD4B]` is 3 (`0x0050A858`; that mode not read). Its readers are 158 (`0x004576EE`), the
player tick (`0x004B9B2D`) and the Dungeon Heart's room update `0x004D9A60` (`0x004D9F89`), which
with the flag set spawns room effect `0x316` and sets room `+0x3D` to 1, and with it clear removes
it and spawns `0x33E` (presentation, not read further).

**158** (`0x004576E0`): with the flag clear, 0. At `Age` 1 a running action is ended. Whenever no
action runs: action 57 with art slot 28, else action 35 with art slot 8, else 0.

**Ours.** Not built: the simulation would need the host's clock as an input, which a lockstep and
replay design must inject and record; a message 98; the kick in the player tick; and the Heart's
effect. Q571.

## 4. 72, 73 and 74: a walk to the Heart to pray — unreachable, STRONGLY INFERRED

72 (`0x00459320`) walks to a tile diagonally two from the keeper's Heart centre, resuming 73
(`0x004593C7`); 73 (`0x004593E0`) turns to the Heart, then `SetState(1)` and `SetState(74)`
(`0x00459445`, `0x00459450`); 74 (`0x00459460`) plays action 27 and after a game second spawns the
Heart room's effect (`+0x421`). 73 and 74 are entered only from 72 and 73, and nothing enters 72 by
any channel.

## 5. 207 and 208: an eight-turn stun — unreachable, STRONGLY INFERRED

207 (`0x00480FB0`) restarts action 20 each turn (its only start in the binary) and after eight turns
`SetState(1)`; 208 (`0x00480FF0`) the same wait without the action. No push, store or table slot of
`0xCF` or `0xD0` is a state: each immediate is an effect, a sound, a count or UI. Their only live
trace is the transition table, which refuses 77 and 255 from them.

## 6. 0, 2, 212, 213, 215, 245: no entry — unreachable, STRONGLY INFERRED

| state | handler | what it would do |
|---|---|---|
| 0 | none | the constructor starts a creature in 1 (`0x00476B62`); the dispatcher skips 0 |
| 2 | `0x00477660` | idle's inner body until a deadline (`+0x7E`), without a wait for an action |
| 212 | `0x004816C0` | a 15-turn pause, then 1 |
| 213 | `0x004816F0` | the same pause, playing sound `0xCB` each turn |
| 215 | `0x004818E0` | a walk off its room to a neighbouring tile, resuming 214 (the tile search `0x004E6D50` read only in part) |
| 245 | `0x00481850` | a grow-in: its scale up by 600 a turn to `0x1000`, then `+0x3CD` cleared and 1 |

No call, tail, register argument, `+0xDF` store or room-table slot gives any of them; each handler's
address appears only in its state-table fill. Ours uses `CreatureStateMachine.NoState` (−1), never 0.

## 7. 258 and 262: behind a gate that never passes — unreachable, STRONGLY INFERRED

Their only entries are in the Horned Reaper's proximity scan `0x0048FA70` (called from the creature
update at `0x00488DCC` for an `IsHorny` creature): a Skeleton in sight to 258 (`0x0048FBBA`), a Dark
Angel to 262 (`0x0048FC8D`). The scan goes on only when the Reaper's `+0x4C3` is at least now + 20
(`0x0048FA80`–`0x0048FA90`). `+0x4C3`'s writers are the constructor, which stores the creation turn
minus 20 (`0x004877DE`), and the scan's own three stamps past the gate, which store now; so the test
never holds, and the scan, with its Imp and Mistress sounds, is dead. The comparison reads as the
reverse of a 20-turn cooldown. The handlers (`0x00474DE0`, `0x00474EE0`) would play action 54 at the
Reaper in sight, 258 turning to it. A write to `+0x4C3` that the displacement scan cannot see (a
block copy, a load) is the remaining doubt; a loaded value is still at most the saved turn. Play can
settle it: Q573.
