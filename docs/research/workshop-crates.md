# Mechanic: Workshop crates — made, stored, carried and built

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25. What was read:
  - **The crate maker** `0x004F5240` and its helpers: `0x00506BA0` (the object an order makes),
    `0x004F57F0` (a crate into a room), `0x004F5860` (a crate out of one), `0x004E5FF0` (the
    room's object list), `0x004F5040` (the crates set down) and `0x004E69B0` (a place freed).
  - **The Workshop's capacity extra** `0x004F6140`, and the two furniture-slot tests,
    `0x00454110` (floor) and `0x00454230` (wall).
  - **States 68** (`0x00470B20`) **and 69** (`0x00470BC0`), and the dispatcher's handler
    call at `0x004772AA`.
  - **The task handlers for kinds 9 and 10**: the Tier A searcher `0x00468520`, the
    whole-map handler `0x0046BD70`, the drop scan's per-tile handler `0x0046C1A0` (through
    the switch `0x004687C0`), kind 8's handler `0x0046B9C0` and its crate branch
    `0x00468910`, and the task table's find `0x00508200`, raise `0x00507BB0`, holder
    `0x00508110`, claim `0x00508160` and release `0x00508040`.
  - **The blueprint list** at `player+0xA21`: its read `0x00505750`, add `0x00505680` and
    remove `0x005056E0`, with the callers of each.
  - **States 32, 34, 35, 36 and 37**: `0x00466DF0`, `0x00467040`, `0x004671B0`,
    `0x004672F0` and `0x00467460`. The state-35 and 36 bodies have no prologue, so they were
    forced at their exact addresses. Also read: the destination helper `0x0046E480`, the
    nearest-room finder `0x004C6610`, the pick-up `0x0048B4A0`, the put-down `0x0048B500`,
    the door's construction setter `0x004A66C0` and the trap's `0x004A96D0`.
  - **The push to the order list** `0x005066E0`, and its callers in the door loader
    `0x00520F40` and the door placement `0x004C33B0`.
- The shipped `Objects.kwd`: all 18 crates, by flags (`0x344`) and room capacity (`0x35C`).
- Addresses are facts about the binary in our own words; no decompiler output enters this
  repository (ADR-0046).

Original version: DK2 v1.70 (GOG)

Questions: advances ADR-0135; builds the half of ADR-0203 it left out ("what carries a crate
to its blueprint"); Q432.

Confidence: **STRONGLY INFERRED (binary)** throughout. The crate flags and capacities are
**CONFIRMED** from shipped data.

---

## 1. Kinds: 0 is a door, 1 a trap

There is one kind byte, used in three places:
- **In the order list's entry** (`player+0x219`, byte 2). `0x00506BA0` reads the door
  definition (the database's `+0x138` slot) for kind 0 and the trap definition (`+0x160`)
  for kind 1.
- **On a crate** (`+0x14A`). The crate maker writes the order's kind there.
- **In the blueprint handlers.** Kind 0 goes with the keeper's door list (`player+0xE`,
  construction dword `+0x109`, crate byte `def+0xB1`, thing type 4, task kind 10, states 36
  and 37). Kind 1 goes with the trap list (`player+0xC`, construction bit `+0x10C & 0x20`,
  crate byte `def+0xBC`, thing type 3, task kind 9, states 34 and 35).

The state labels (34 and 35 "installing a Trap", 36 and 37 "installing a Door") agree. So do
the door constructor `0x004A5070`, which starts `+0x109` set, and the trap constructor
`0x004A7940`, which starts bit 5 set.

**Ours had the kind backwards**: `TrapKind = 0`, `DoorKind = 1`. It was consistent with
itself, so no behaviour changed, but it was wrong against the binary; it is corrected.
`state-category-hooks.md` §3.4 also had the construction fields swapped in one sentence.

**A crate's type byte** (`+0x14E`) is not the trap or door id. It is the definition's crate
object id: `0x00506BA0` returns that byte as the object to make, and the maker copies it to
`+0x14E`. So a crate matches a blueprint when its kind is the blueprint's kind and its
object is the blueprint's crate. Ours derives both from the object type.

The shipped crates are 18 objects. Each has flags `0x810010` (the Jack-In-The-Box
`0x800010`) and room capacity 0. Bit `0x100`, `CanBePickedUp`, is clear on every one, so
**the hand cannot lift a crate**.

## 2. The blueprint list, `player+0xA21`

The list is 128 words of thing ids, with its count at `+0x103`. `0x00505680` appends an id
unless it is already there or the list is full. `0x005056E0` removes one and closes the gap.

- **Who adds.** Three callers of `0x00505680`: the door loader `0x00520F40`, for each
  authored door whose `+0x109` is set; the door placement `0x004C33B0`; and `0x004C3860`.
  The trap loader does not add, and does not push an order either (the push `0x005066E0` has
  the same three callers). So **a trap blueprint authored in a level is never ordered and
  never built.** The shipped levels author none (`doors-and-traps.md`).
- **Who removes.** State 37; the searchers `0x0046BD70` and `0x00464A80`, for a dead or
  finished entry; `0x004C3A20` and `0x004C3670`; the door and trap updates `0x004A55F0` and
  `0x004A84E0`; and `0x00505C90`. The last five were not read.

## 3. The crate maker, `0x004F5240`

This runs when the head order is paid for (`state-category-hooks.md` §3.4). In order:
1. **Every creature attached to the Workshop is set to state 68.** The loop runs until the
   room's attached count (`room+10`) is zero; leaving the Manufacture category unlinks each
   creature.
2. **The crate is made** on the room's first chain tile (`+0x2D`, `+0x2E`). Its object is
   the order's crate (`0x00506BA0`), its owner is nobody, and its kind and type byte are set
   from the order.
3. **The capacity test** (`0x004E53B0`) runs with the crate's units, the object's room
   capacity: 0 for every crate. On a failure, `0x004E69B0` would free a place by sending an
   attached creature to state 1, and the test runs again. Step 1 has already emptied the
   room, so that does nothing here.
   - **With space:** the crate takes the room's owner (vtable `+0x24`), joins the room's
     object list (`0x004E5FF0`: the list head, the room id written to `+0x138`, the units
     added to `+0x6B` and `+0x6F`), and the crates are set down again (`0x004F5040`, §4).
   - **Without:** it stays on the first tile, belonging to nobody.
4. **Then:**
   - sound `0x10F` at the crate;
   - `ItemsManufactured` goes up;
   - message `0x1A` goes to the keeper;
   - a burst of the room definition's effect `+0x421`, one for every nine tiles (at least one)
     at random tiles, and its effect `+0x423` at the crate.

**The Workshop's capacity extra** is `0x004F6140`. It counts:
- the room's furniture, meaning its furniture-list objects (`+0xE`, next `+0x12A`) whose
  object id is on the definition's zero-terminated list at `+0x431`;
- the room's listed objects (`+0xC`, next `+0x126`).

It admits while the listed objects divided by 3 are fewer than the room's tiles (`+0xA7`)
less the furniture. It is part of every capacity test on a Workshop, so **a Workshop full of
crates also refuses a manufacturer.**

## 4. Where a Workshop's crates lie, `0x004F5040`

The room's listed objects are taken head first, which is the newest. For each one, the
routine walks the room's tile chain and counts the tiles that neither furniture test
accepts. The *n*-th crate goes on the (*n* / 3)-th such tile, facing the room's position. Its
offsets within the tile cycle through `+0x249`, `−0x249` and a `0x300` height, so three
crates sit side by side.

The two furniture tests:
- **`0x00454110`, the floor slot.** It accepts a room tile whose map byte `+0x0C` is `0xFF`
  (an interior tile). By room type:
  - types 3, 5, 8, `0x11` and `0x16`: never;
  - the Guard Room, Workshop and Casino (9, 10, `0xF`): only on the room's checkerboard
    parity (`x + y + room+0x80`, even);
  - the Graveyard (`0xE`): unless object 75 is on it;
  - every other type: always.
- **`0x00454230`, the wall slot.** It accepts a tile with a straight wall on one side: the
  side tile carries map bit 8 and terrain flag `0x10000`, both tiles along the wall are the
  same room, and both diagonals are wall. The tile must also be of the parity the room first
  saw in that orientation (`room+0x92` horizontal, `+0x96` vertical, each set lazily from
  the coordinate).

The decompiler printed `0x00454230` as always returning 0; the disassembly returns 1 at
`0x004545AA`. Both tests match what `AutoFurnitureRules` computes for placing furniture
(`FloorSlots`, and the wall walk). Neither asks the room's special-tiles or special-walls
flag.

## 5. States 68 and 69: every worker ends the turn idle

**68** (`0x00470B20`):
- On its first turn (`Age == 1`) it ends a running action (`0x00496D20`).
- It reads the creature definition's animation record 8 (27-byte records at `def+0x370`
  through `0x00494860`), flag `+0xB` bit 0. With the flag set and no action running, it
  starts action 35 (`0x23`) for one or two plays (`random(2) + 1` into `+0x3AE`).
- **It then sets 1 and 69, and returns 0.** It sets 1 first because 68 to 69 is refused:
  same group, target category Refused.

**The dispatcher** sets state 1 after any handler returns 0 (`0x004772AA`–`0x004772B7`,
`imp-state-exits.md` §1). So the creature ends the turn idle with the action playing. Without
the flag, or with an action still running, 68 returns 0 at once, which is also idle.

**69** (`0x00470BC0`) waits for the action and then sets 63 if the keeper has an order, else
returns 0. Nothing that sets 69 leaves the creature there, so it never runs. The only setter
is 68.

So **every crate made sends every worker in that Workshop idle**, and they choose a job again
through the ordinary selection. That is the binary's reading; Q432 asks the owner.

## 6. Kinds 9 and 10: a crate to a blueprint

Tier A's table (`0x00698658`) is `0E 08 09 01 03 0D 0C`. `0x00468520` sends 9 and 10 to
`0x0046BD70`. Every handler below finds or raises the task at the blueprint's tile, skips it
if another creature holds it, walks the imp to the crate through the imp wrapper `0x004651A0`
(options `0xE8`), and records the task on the imp: task id `+0x8A`, blueprint tile `+0x8C` and
`+0x90`, crate type `+0x94`, kind `+0x98`. It then claims the task (`0x00508160`).

- **The whole-map handler, `0x0046BD70`.** It walks the blueprint list from the head.
  - An entry that is dead or finished is removed, and the walk starts again from the head.
  - An entry another creature holds is passed over.
  - For any other entry, the keeper's rooms are taken in list order (`player+0x36`). The
    first Workshop holding a crate of the kind and object (`0x004F5B00` trap,
    `0x004F5B70` door: the first match down the room's list) is the job, if both the
    blueprint and the crate are reachable (`0x00471630`).
  - No distance is measured.
- **The drop scan's handler, `0x0046C1A0`** (third in the nine-step chain,
  `imp-task-priority.md` §2.3). It walks the things on a tile the imp's keeper owns, for a
  blueprint of either kind, and picks the Workshop with a crate for it whose position (`+0x27`)
  is nearest. For a trap, "nearest" is to the imp; for a door, to the door. Only the crate's
  reachability is tested.
- **Kind 8's crate branch, `0x00468910`.** Kind 8's handler (`0x0046B9C0`) takes a crate
  (flag `0x10`) that belongs to nobody on the imp's own floor or on water, and asks this
  branch first. The branch walks the keeper's whole trap list (kind 1) or door list (kind 0)
  for blueprints needing that crate. It takes the nearest to the imp that is not held and is
  reachable, and walks to the crate. Only if no such blueprint exists does kind 8 carry the
  crate to a Workshop with space (`0x004C6610`), in state 30.

## 7. States 34 to 37

**34 and 36** (`0x00467040`, `0x004672F0`):
- They walk the things on the imp's own tile for an object that nobody holds, of the crate
  flag, of the task's kind and type byte.
- For the first one, they start the walk to the blueprint's tile centre, resuming in 35 or
  37 (option `0xE8`, no teleport).
- If the walk starts, the crate leaves its room: if the tile's room is the one on the crate's
  `+0x138`, `0x004F5860` takes it out of the list, makes it nobody's and sets the rest down
  again. The imp then picks the crate up (`0x0048B4A0`, into `+0x3C5`).
- With no such crate, or no walk, the task is released and the imp sets 10.

**35 and 37** (`0x004671B0`, `0x00467460`):
- They put the crate down where the imp stands (`0x0048B500`).
- They walk the keeper's trap or door list for one at the task's tile that is under
  construction and needs this crate. If found:
  - the crate is destroyed (vtable `+0x20`);
  - the thing is built: the trap through `0x004A96D0` (bit 5 cleared, then its set-up), the
    door through `0x004A66C0(0)` (`+0x109` cleared, its navigation byte and look
    refreshed);
  - a sound plays (`0x328` trap, `0x327` door);
  - the blueprint leaves the list.
- A door whose definition has flag `0x20` (the Secret Door) also gets terrain event `0x11`
  and `0x00450FF0`.
- Either way the task is released and the imp sets 10.
- A carried id of zero returns 0, which is idle.

**32**, a crate carried home (`0x00466DF0`):
- The crate is put down.
- On a tile of a room the imp's keeper owns that passes the capacity test, `0x004F57F0` lists
  it, the sound `0x326` plays, and the imp sets 10.
- Otherwise it asks the destination helper `0x0046E480` again. For a crate that helper takes
  the nearest Workshop with space (`0x004C6610`) and a random tile of it that is not a
  furniture slot (`0x004E8430`), then walks there resuming in 32.

## 8. What is built (ADR-0214)

- `CrateRules`: the kinds; the blueprint list; the Workshop's listing, unlisting and
  capacity extra; the three-to-a-tile arrangement; the build.
- `ManufactureRules`: the door loader's pushes (doors only); the crate maker's sending of the
  workers to 68; the crate left to nobody when the Workshop is full.
- `RoomCapacityRules`: the Workshop extra on every capacity test.
- `AutoFurnitureRules.FurnitureSlots`: both slot tests.
- `CreatureJobSystem`: 68, which idles the creature on the turn it runs.
- `ImpTaskSystem`:
  - the three handlers, in their places (Tier A third, the drop scan third, kind 8's branch);
  - states 34 to 37;
  - state 32 for a crate, listing it with the eviction.

**Ours:**
- A crate is listed while its keeper owns it and it lies on one of that keeper's Workshop
  tiles. Newest means the highest id.
- Rooms are taken in `RoomIndex` order. Distances are to a room's first tile.
- Tiles are walked in index order, not chain order.
- The task hold is read from the other imps' states.
- 68's celebration takes no time and its flag is not read, because both arms end idle.
- The eviction takes the working creature with the lowest id.

## 9. Read, not built

- **Blueprints placed in play** (`0x004C33B0`, `0x004C3860`) and the five unread removers.
- **State 76's crate branch** (`0x00464A80`). It also takes a held crate job from an imp it
  judges to be farther away, through `0x0048AD30(10)`.
- **The trap's completion** past its flag, the Secret Door's terrain update, the sounds,
  message `0x1A` and the maker's effects.
- **The unknown `+0xF6` tests in `0x005066E0`**: a door must have `+0xF6 == 0`, a trap
  `+0xF6 == 1`.
