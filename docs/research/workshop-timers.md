---
systems: [rooms]
---
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

Original version: DK2 v1.70 (GOG). Implemented as `WorkshopTimerRules` and `WorkshopTimerSystem` (ADR-0634).

## 1. Why it matters

A reload draws the simulation stream (world `+0x15A0`, `dk2-random.md` section 2) whether or not an effect or a
sound follows, so a level with a Workshop draws more than one without. The effects are presentation; the
draws are not. Draws a Workshop makes, all on the world stream (STRONGLY INFERRED by each call's seed argument):

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
not counted before. STRONGLY INFERRED (disassembly of both functions).

## 2. The room's fields (STRONGLY INFERRED)

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

## 3. The handler, `0x004F4B20` (STRONGLY INFERRED)

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
   Steel Bucket 69 Lava Spill, Work Bench 0) at the object's mesh point `0x2712` through `0x0059D380`, a
   three-dimensional point, z included; the owner is the object's own owner word (`+0x28`, `0x004F4D7B`), not
   the room's; the heading is (mesh point minus the object's position, atan2 of x then y) & 0x7FF
   (`0x004F4D4C..0x004F4D6B`, `0x004F4D7F`); then sound `0x2E9` (`0x004F4DC7`). Nothing happens for an object
   with effect 0, or when the lookup fails (`0x004F4D3D`). **Ours:** the owner is the object's; the point is the
   object's floor position at height 0 and the heading 0, because the simulation has no mesh point (marked
   deviation, Q733a).
3. **Smoke** (`+0x4D`, kept to 16 bits). With the effect `+0x427` (file `0x388`; 51, Workshop Smoke) it asks
   `0x004E8210(0, 1)` for a wall slot and its side `s`; the point is the tile centre moved `0x700` toward the
   wall (north `y-`, east `x+`, south `y+`, west `x-`), height `0x1000`, heading `(s & 3) << 9`; then sound
   `0x2E8` (`0x004F4F03`).
4. **Lava bubbling** (`+0x51`). Counts the buckets as above; with some it draws `random(count)` and walks to that
   bucket (skipping non-buckets), and with the effect `+0x429` (file `0x38A`; 93, Lava Bubbling Effect) plays it at the
   bucket's position, heading 0. No sound.

The floor and wall passes of `0x004E8210` walk the room's slab chain in order. The room's effects are the
file's eight words from `0x382`, copied to `+0x421` by `0x004EAE2A` when nonzero (`RoomDef.RoomEffect427Id`,
`RoomEffect429Id` are the two this handler adds to `RoomEffect425Id`).

## 4. Order (STRONGLY INFERRED)

`0x004EC490` links each new room at the head of the manager's in-use list (`0x004EC559..0x004EC57E`) and
`0x004EC450` walks it from the head along each room's `+0x21`, reading the next link before the call
(`0x004EC464`), so the rooms of every player tick **newest first, all types together**. `Room_Tick`
(`0x004E4B00`) runs a room's handler (slot `+0x15`; a zero return ends the routine), then the border step
`0x004E7CD0`, the stamp at `+0x73`, the effect object's re-placement and the health step, all before the next
room. Within a Workshop the order is the hated-company step (it draws nothing), then the four blocks above.

## 5. Do the effects or sounds draw the stream? (STRONGLY INFERRED: no)

- Effect creation: world slot `+0x38C` (`0x00509580`) forwards through `+0x388` (`0x0050E490`), which calls
  `[[world+4]+0x10]` slot `+0x90` (`0x00440420`, vtable slot at `0x0066CD24`); that calls its slot `+0x98`
  (`0x00440350`, at `0x0066CD2C`), which calls `0x004347E0`. It reaches the effect
  initialisers, whose draws are on the static effect stream `0x006D6450`; the only world-seed draw it reaches
  is `0x004B37D0`'s coin (`RenderInfo.cpp` line 0xC9), which is taken only when the animation's owner thing
  casts to a creature, and an effect's animation has no owner thing.
- The mesh-point lookup `0x0059D380` reaches no generator call (115 functions by direct calls). Indirect
  calls are not followed.
- Sounds run on their own LCG (`sound-playback.md`).

## 6. What ours does and where it differs

- `RoomTickSystem` runs each room's steps before the next room's (section 8).
  Its shared owner-list ordinal does not fully mirror the independent manager
  order; the [Q733(d) control](room-manager-order.md) records the measured
  draw-order consequence. The general manager-order adapter remains unbuilt.
- A level's own Workshops are constructed when a simulation starts, row by row as the map loads, before
  creatures: the four draws each, merged rooms included. UNKNOWN: whether DK2's loader makes rooms before
  or after its creatures; it sets the seed to 1 first (`0x00513870`).
- A room whose terrain a test sets directly (no `SlabChain` call) starts at zero and reloads on its
  first turn.
- The furniture list leaves out the Workshop Pillars (object 80, one per convex corner, pushed by
  `0x004F5650`), which our simulation does not hold. Only the lava-spill pick's landing object depends on it.
- The lava-spill effect is asked for at the object's own floor position, height 0, heading 0, owned by the
  object; the mesh point `0x2712` (a control point of the object's model) is UNKNOWN, and so is whether each
  of the three machines has it (section 3, Q733a).
- The wall-slot phases (room `+0x92`, `+0x96`, `0x00454230`) are written once per room, when first met
  (`0x004544B1..0x00454507`), and the checkerboard's `room+0x80` by `0x004EF5E0`; ours recomputes both from the
  room's current chain on each call, so after a room grows or merges the bound of a slot pick can differ. The
  draw count is unaffected, only which slot is picked (Q733e). The floor test (`0x004541AA`, `(x + y + room+0x80)`
  even, interior cell `+0x0C == 0xFF`) is the one ours uses.
- The sounds are asked by `WorkshopTimerRules.RequestSound` (`0x2E9` at `0x004F4C27` and `0x004F4DC7`, `0x2E8` at
  `0x004F4F03`), in the category of the terrain at the effect's point; the spill's sound sits where the effect does
  (above), where DK2's sits at the mesh point. They draw nothing (section 5); see
  [sound-effects-combat.md](sound-effects-combat.md).

## 7. The same gap in the other effect handlers (not built)

| Room | Handler | Draws the world stream |
|---|---|---|
| Torture Chamber (12) | `0x004F1D60` | yes: byte timers `+0x49` (reload `random(4 x ticks)`, line 0x25; effect `+0x421` through `0x004E8210(1, 1)`, one more draw) and `+0x4A` (`random(32 x ticks)`, line 0x3F; effect `+0x423`, `0x004E8210(1, 0)`) |
| Temple (13) | `0x004F1100` | yes, only through `0x004E8210(1, 0)` each time the fixed four-second timer `+0x5E` fires with effect `+0x425`; the call's result is ignored |
| Graveyard (14) | `0x004DD3F0` | yes: once a second, with effect `+0x421`, one draw in `Room_RandomTilePoint` `0x004E4D90` (`Room.cpp` line 0x61B) |

Their constructors (`0x004E17D0`, `0x004F15A0`, `0x004DD3A0`) draw nothing.

## 8. The room walk: DK2's handlers and our steps (ADR-0634)

The table `0x006A1E9F`, slot `+0x15` (read 2026-11-02), and the walk's steps in the order one room takes them. Every
handler returns 1 (the table's handlers read here), so a room's border and health steps always follow.

| DK2 handler | Type | Ours | Draws the world stream |
|---|---|---|---|
| `0x004F39A0` | 1 Treasury | `StoreSweepSystem` (sparkle every turn, sweep every 16th) | yes |
| `0x004D9A60` | 5 Heart | `StoreSweepSystem`, `HeartDefenceSystem` (`0x004D9690`, called first), `HeartFrameStep` (opening, regeneration, ruin) | the sparkle; the ruin and destruction paths |
| `0x004DBBE0` | 3 and 25 Portals | `PortalSystem`, counting live | yes |
| `0x004DE0A0` | 4 Hatchery | `HatcheryLayStep` | yes, the lay |
| `0x004D6270` | 15 Casino | `CasinoSystem` | yes, one inline site |
| `0x004E0740`, `0x004F2E90`, `0x004DDD70` | 6, 7, 9 | `HatedCompanySystem` | no direct site |
| `0x004F4B20` | 10 Workshop | `HatedCompanySystem`, then `WorkshopTimerSystem` | yes |
| `0x004E7CD0` (in `Room_Tick`) | every room | `RoomBorderFlameSystem` | no |
| the health step (in `Room_Tick`) | every room | `RoomHealthSystem` | no |

- **Hatchery.** `Hatchery_Tick` only tests `room+0x49 + room+0x4D <= turn`, lays, stamps `+0x49` and returns 1.
  World_UpdateTurn runs the thing pass first, so eggs, coops and chickens step before the lay:
  `ObjectMovementSystem` interleaves the bird, Egg and Coop processes (ADR-0699); the default
  `HatcherySystem` retains only synthetic-data hatching before `TrapSystem`
  (object class list 2 of `0x004B7830`, after the shots, before the traps). The coops are not the handler's: Room_Rebuild's Hatchery arm
  (`0x004DE220`, hatchery.md section 2) places them at room-change time, one `random(4) << 9` draw each; ours does
  the same in `HatcheryRules.RebuildCoops` (ADR-0672).
- **Heart.** `0x004D9A60` is the Heart's whole handler and has no draw site of its own: only `0x004D9690`'s sparkle
  draws. `HeartFrameStep` runs the opening, regeneration and ruin at the Heart's turn. `Room_Ruin`'s Heart arm
  (`0x004E9130`) only toggles `+0x9A` bit 0 and recodes tiles. `Heart_OnDestroyed` (`0x004DA560`) draws, but its only
  caller is `Object_OnDelete`, in the thing pass: **ours omits those draws.** The message post at `0x004D9AFD`
  (`0x27` at 50% health, `0x28` at 10%) has no counterpart in ours, which has only HeartAttacked.
  `DungeonHeartSystem(frames: false)` keeps the verdict tail at the tick's end; DK2 runs it from `Player_Tick`
  (`0x004BA0B4`), in the middle of each player's frame: an approximation.
- **Portal.** The pick (`0x004DBF90`) recounts the owner's portal-born creatures (list `+6`, link `+0x10`,
  `+0x3E6 & 2`) and the capacity (`0x004DCE30`) each time; the arrival creator (`0x004DC9A0`) links the creature
  (`Creature_Construct`, `0x004872A0`) and sets that bit at once. A second ripe portal sees the first's arrival.
- **Bridge.** `0x004D5FA0` (type 8) has two passes: every `5 x TicksPerSecond` turns, phased by the room's id, a walk
  advances burning tiles (state `0x10` sets `room+0x49`), and while `room+0x49` is 1 a sale pass runs every turn,
  selling up to eight state-`0x12` tiles through `Player_SellRoomTile` (`0x004C5DB0`), which reaches the room rebuild's
  draws and appears to refund the sale value. `BridgeBurnSystem` is our stand-in, a per-tile countdown run in the
  work phase before the walk (Q67), with no refund; it is not a step.

DK2 handlers with no step of ours: Torture, Temple, Graveyard (section 7); the Combat Pit's own turn `0x004D7A60`
(two sound timers, as the Torture Chamber's, drawing `random(4 x ticks)` and `random(32 x ticks)` and the slot
pick, and message 9: its creature states are `CombatPitSystem`'s, in the thing pass); the hero gate type 21
`0x004DF6E0` (five inline draws); the Prison `0x004E2AC0` (door auto-close, no draws); the Lair and Crypt
`0x004B4C20` (nothing). Every step of ours has a DK2 counterpart; `BridgeBurnSystem` stands in for one.

Left where they are, because DK2 does not run them inside `Room_Tick`: the Hatchery object steps in `ObjectMovementSystem` (thing pass),
`CasinoLeverSystem` (a command), the verdict tail of `DungeonHeartSystem` (player frame), `PrisonSystem`,
`TempleSacrificeSystem` and `GraveyardSystem` (tile and creature hooks).

**Order of rooms.** The production sort remains `RoomListPosition` descending,
then lowest tile descending (as `RoomLists.Of`). The [independent manager-order
control](room-manager-order.md) owns Q733(d)'s current evidence and implementation limits.
