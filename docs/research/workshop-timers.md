# The Workshop's effect timers and what they draw from the random stream

Source(s):
- `DKII.EXE` v1.70 (GOG), read in Ghidra 12.1.4 on 2026-11-02: the Workshop's room constructor
  `0x004F4A60` and tick handler `0x004F4B20` (both by disassembly and decompile; neither is a Ghidra
  function, both are reached only through the room-type table `0x006A1E9F`), the position selector
  `0x004E8210`, the room allocator `0x004EC490`, the room constructor `0x004E3800`, the room manager's walk
  `0x004EC450`, the room definition loader's effect copy `0x004EAE2A`, and all 479 calls of the generator
  (`PreCall` on `0x005558D0`) for what the effect and mesh-point paths can reach.
- Shipped `Rooms.kwd`, `Objects.kwd` and `Effects.kwd` (Work Shop, Anvil, Steel Bucket, effect names).
- Addresses and offsets are facts in our own words (ADR-0046).

Original version: DK2 v1.70 (GOG). Implemented as `WorkshopTimerRules` and `WorkshopTimerSystem` (ADR-0633).

## 1. Why it matters

A reload draws the simulation stream (world `+0x15A0`, `dk2-random.md` section 2) whether or not an effect or a
sound follows, so a level with a Workshop draws more than one without. The effects are presentation; the
draws are not. Draws a Workshop makes, all on the world stream (CONFIRMED by each call's seed argument):

| Where | Source line | Range | When |
|---|---|---|---|
| constructor `0x004F4A60` | `Workshop.cpp` 0x22, 0x23, 0x24, 0x25 | `4 x TicksPerSecond` each | when the room is made |
| steam reload | `Workshop.cpp` 0x39 | `16 x TicksPerSecond` | timer at zero |
| lava-spill reload | 0x5D | same | timer at zero |
| lava-spill pick | 0x6C | the Steel Bucket count | timer at zero, count above zero |
| smoke reload | 0xA0 | `16 x TicksPerSecond` | timer at zero |
| lava-bubbling reload | 0xD0 | same | timer at zero |
| lava-bubbling pick | 0xE3 | the Steel Bucket count | timer at zero, count above zero |
| floor-slot pick | `Room.cpp` 0xF42, inside `0x004E8210` | the floor-slot count | steam fires, definition effect `+0x425` set, a slot exists |
| wall-slot pick | `Room.cpp` 0xF42 | the wall-slot count | smoke fires, definition effect `+0x427` set, a slot exists |

The six lines of the handler are the reloads and picks; the slot picks and the constructor's four were
not counted before. CONFIRMED (disassembly of both functions).

## 2. The room's fields (CONFIRMED)

`Room_Construct` (`0x004E3800`) zeroes `+0x49..+0x66` and then calls the type's constructor, slot `+0x01` of the
type table (`0x004F4A60` for type 10). The dumper `0x004E70B6` names the Workshop's overlay:

| Offset | DK2's name | Ours | Timer |
|---|---|---|---|
| `+0x49` | SteamEffectCounter | `TileState.WorkshopSteamTimer` | first |
| `+0x55` | LavaSpillEffectCounter | `WorkshopLavaSpillTimer` | second |
| `+0x4D` | SmokeEffectCounter | `WorkshopSmokeTimer` | third |
| `+0x51` | LavaBubblingEffectCounter | `WorkshopLavaBubblingTimer` | fourth |

All four are dwords in turns. The constructor draws `random(4 x TicksPerSecond)` into `+0x49`, `+0x4D`,
`+0x51`, `+0x55` in that order (so 0 to 15 turns at the shipped 4 a second), and clears `+0xA3`. Ours keeps
the four on the room's lowest tile, as the Hatchery's laying stamp is kept (`room-object.md`): growth and
merging keep the surviving room's, a rebuild after a sale constructs each piece anew, and a rival's capture
keeps the tiles, so keeps the timers.

## 3. The handler, `0x004F4B20` (CONFIRMED)

Fastcall on the room, once a turn (`Room_Tick`, `0x004E4B00`, through `0x004EC450`), after which it returns 1,
so the common health step follows. It first runs the hated-company step `0x004E8C60`, then four blocks in this
order, each `if (timer != 0) timer--; else { reload; act }`. A reload is
`random(16 x TicksPerSecond) + 8 x TicksPerSecond`: 8 to 24 game seconds, so 32 to 95 turns at 4 a second.

1. **Steam** (`+0x49`). If the definition's effect `+0x425` (file `0x386`; Work Shop: 50, Workshop Steam)
   is nonzero it asks `0x004E8210(1, 0)` for a floor slot; with one, it plays the effect at the slot's tile
   centre, height `0x1000`, heading 0, owned by the room's owner, then sound `0x2E9` (`0x004F4C27`).
2. **Lava spill** (`+0x55`). Counts the room's auto-placed objects of type 16 (Steel Bucket) on its furniture
   list (room `+0x0E`, next `+0x12A`, type byte `+0x12C`). With some it draws `random(count)` and walks **that
   many objects along the whole list, whatever their type** (the other timer skips non-buckets): the object
   reached takes its definition's idle effect (`+0x117`, Objects.kwd `0x358`: Anvil 42 Sparks for Forge,
   Steel Bucket 69 Lava Spill, Work Bench 0) at the object's mesh point `0x2712`, owner the room's owner, then
   sound `0x2E9` (`0x004F4DC7`). Nothing happens for an object with effect 0 or no such mesh point.
3. **Smoke** (`+0x4D`, kept to 16 bits). With the effect `+0x427` (file `0x388`; 51, Workshop Smoke) it asks
   `0x004E8210(0, 1)` for a wall slot and its side `s`; the point is the tile centre moved `0x700` toward the
   wall (north `y-`, east `x+`, south `y+`, west `x-`), height `0x1000`, heading `(s & 3) << 9`; then sound
   `0x2E8` (`0x004F4F03`).
4. **Lava bubbling** (`+0x51`). Counts the buckets as above; with some it draws `random(count)` and walks to that
   bucket (skipping non-buckets), and with the effect `+0x429` (file `0x38A`; 93, Lava Bubbling Effect) plays it at the
   bucket's position, heading 0. No sound.

The floor and wall passes of `0x004E8210` walk the room's slab chain in order. The room's effects are the
file's eight words from `0x382`, copied to `+0x421` by `0x004EAE2A` when nonzero (`RoomDef.RoomEffect427Id`,
`RoomEffect429Id` are the two this handler adds to `PortalBuildUpEffectId`).

## 4. Order (CONFIRMED)

`0x004EC490` links each new room at the head of the manager's in-use list and `0x004EC450` walks it from the
head, so the rooms of every player tick **newest first**, one room's whole handler and health step before the
next. Within a Workshop the order is the four blocks above, the hated-company step first (it draws nothing).

## 5. Do the effects or sounds draw the stream? (STRONGLY INFERRED: no)

- Effect creation (world slot `+0x38C` to `0x00440420` to the allocator `0x00440350`) reaches the effect
  initialisers, whose draws are on the static effect stream `0x006D6450`; the only world-seed draw it reaches
  is `0x004B37D0`'s coin (`RenderInfo.cpp` line 0xC9), which is taken only when the animation's owner thing
  casts to a creature, and an effect's animation has no owner thing.
- The mesh-point lookup `0x0059D380` reaches no generator call (115 functions by direct calls). Indirect
  calls are not followed.
- Sounds run on their own LCG (`sound-playback.md`).

## 6. What ours does and where it differs

- `WorkshopTimerSystem`, in the room-tick phase after `HatedCompanySystem`, ticks the Workshops newest first
  (by `RoomListPosition`, ADR-0541). **Deviation:** DK2 ticks all rooms in one list; ours runs each
  room type's system in turn, so a Treasury's per-turn draws (`StoreSweepSystem`) come before every
  Workshop's. The stream's length is right; the order inside a turn where a Workshop timer fires is not.
- A level's own Workshops are constructed when a simulation starts, row by row as the map loads, before
  creatures: the four draws each, merged rooms included. UNKNOWN: whether DK2's loader makes rooms before
  or after its creatures; it sets the seed to 1 first (`0x00513870`).
- A room whose terrain a test sets directly (no `SlabChain` call) starts at zero and reloads on its
  first turn.
- The furniture list leaves out the Workshop Pillars (object 80, one per convex corner, pushed by
  `0x004F5650`), which our simulation does not hold. Only the lava-spill pick's landing object depends on it.
- The lava-spill effect is asked for at the object's own position; the mesh point `0x2712` (a control
  point of the object's model) is UNKNOWN, and so is whether each of the three machines has it.
- The sound sites are left as comments (`0x2E9` at `0x004F4C27` and `0x004F4DC7`, `0x2E8` at `0x004F4F03`).

## 7. The same gap in the other effect handlers (not built)

| Room | Handler | Draws the world stream |
|---|---|---|
| Torture Chamber (12) | `0x004F1D60` | yes: byte timers `+0x49` (reload `random(4 x ticks)`, line 0x25; effect `+0x421` through `0x004E8210(1, 1)`, one more draw) and `+0x4A` (`random(32 x ticks)`, line 0x3F; effect `+0x423`, `0x004E8210(1, 0)`) |
| Temple (13) | `0x004F1100` | yes, only through `0x004E8210(1, 0)` each time the fixed four-second timer `+0x5E` fires with effect `+0x425`; the call's result is ignored |
| Graveyard (14) | `0x004DD3F0` | yes: once a second, with effect `+0x421`, one draw in `Room_RandomTilePoint` `0x004E4D90` (`Room.cpp` line 0x61B) |

Their constructors (`0x004E17D0`, `0x004F15A0`, `0x004DD3A0`) draw nothing.
