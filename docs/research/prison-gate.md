---
systems: [rooms, creatures]
---
# An imp takes a prisoner in through the Prison's gate

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70: state 43 `0x00467DD0`, state 44 `0x00468010`, state 46 `0x004683F0`, the casualty chooser `0x0046E610`, the door-opening arm `0x004E2940`, the entry point `0x004E3290`, the step cost `0x004C8BD0` (read 2026-11-05). Owner report, Level 6a, 2026-11-05 |
| Implementation | `ImpTaskSystem.Carrying` (`AtThePrisonGate`, `LeaveByTheDoor`, `UpdateLeavingThePrison`, `RouteTo`), `PrisonRules.GateTileIn`, `MoveOptions.PrisonDelivery`/`PrisonLeaving`. ADR-0681 |
| Tests | `PrisonGateTests`, `PrisonTests` (the 43/44/46 and refusal traces) |
| Confidence | **STRONGLY INFERRED**: every rule below is read in the binary. **UNKNOWN**: where a body lands when the Prison fills during the carry (Q754) |

## 1. The rule

A Prison's cell floor is navigation bit 2 (`0x004EFDE0`, structure-blocks.md). The step cost
`0x004C8BD0` returns 0 for a step between a tile with the bit and one without unless the walk's option
word has bit 2. The imp's prisoner walks have it: `0xE4` (state 43's step into the cell) and `0xEC` (state
44's walk out, and 47/48). `0xE0` and `0xE8` stop at the cell's edge. So the cell is entered and left only by
these walks, which start and end at the door tile. The owner's "doors only let prisoners
through one way" is this.

## 2. The imp's route

1. **Pick-up (state 42, `0x0046E610`).** For an enemy it picks the nearest Prison with space
   (`0x004C6610`) and walks to the room's **entry point** `0x004E3290`: the centre of the door tile
   (`DoorTileX/Y`, `+0x51/+0x55`; the tile north of the first floor tile, jail-break.md §1), option `0xE0`,
   resuming 43.
2. **State 43 (`0x00467DD0`).** On a Prison tile whose door is activated (`+0x59`) and which has room: if the
   imp is **on the door tile**, walk to the centre of the tile **south** of it, option `0xE4`, resuming 44,
   and clear the imp's two fight bits. If it is on another Prison tile, open the door
   (`0x004E2940`, an owner's worker in 43 on the door tile) and walk to the door tile again (`0xE0`, 43).
   Anywhere else, or a Prison without room: the nearest other Prison with space, to its door tile (`0xE0`,
   43); with none, `Creature_DropDraggedThing` and the handler returns 0.
3. **State 44 (`0x00468010`).** On the cell tile it admits the body (exit 1), then walks to the door tile with
   `0xEC`, resuming 46. Its other exits only happen when the Prison changed in the one step between 43's test
   and 44's.
4. **State 46 (`0x004683F0`).** Sets the imp's two fight bits back and sets 10. It runs on arrival at the door
   tile, so the imp is out of the cell when it does.

## 3. Ours

Before ADR-0681 the carry's destination was a free cell tile, routed with `0xE4` from wherever the imp
stood, so it crossed the cell's edge from any rim tile; and state 46's walk was elided, leaving the imp on a
cell tile that no ordinary walk can leave.

Now `PrisonRules.GateTileIn` names the door tile of the nearest Prison with space, `RouteTo` walks it with
`0xE0`, and `AtThePrisonGate` runs 43's arrival test. Admission walks back to the door with `0xEC`.

- A Prison that fills before the imp reaches the door sends it on to another, else the body is put down **on
  the door tile** and the imp goes idle (43's branch). Before, it was put down on a random rim tile (state
  47's room point), which is 44's exit 3 and is reached now only if the Prison fills during the one step in.
- Still elided: 44's exits 2 to 4 and 47/48's walks (the imp stays in the cell on those races), the door
  object and its opening (`0x004E2940`'s toggle, the auto-close count), 43's `Prison_CarrierOpensDoor` for an
  imp on a rim tile.
- A Prison with no door tile (a cell on the map's top row) falls back to the free cell tile.

## 4. Open: Q754

The binary says a Prison full at the door leaves the body on the door tile. The earlier reading (room point on
the rim) came from 44's exit 3 alone. Both are "on the rim". **Record:** Level 6a, a 3x3 or 5x5 Prison with
one place free, an imp carrying a captive; fill the last place by hand drop while the imp is a few tiles
from the door. Look at which tile the body lies on: the door tile (north edge of the cell) or another rim tile.
