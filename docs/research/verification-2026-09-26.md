# Verification of ADR-0227 to ADR-0234, 2026-09-26

- **Scope:** ADR-0227 (pit spectators), 0228 (idle fallback), 0229 (jail break), 0230 (torture
  information), 0231 (blueprints in play), 0232 (the negative-claims audit), 0233 (door
  orientation), and Q427 as committed in ADR-0234 (`5fb22d9`).
- **Method:** each cited routine re-read in `DKII.EXE` v1.70 with Ghidra 12.1.4 on a private,
  read-only copy (`D:\Ghidra\claude ghidra\verifier-1`). The positive control, `ScanAll 006F2590`,
  gave 226 hits. Negative claims were re-run with `CallScanAll` (rel32 and absolute, `.text` and
  `cseg`), `ScanAll` (every initialised block, each address and the three bytes below it) and
  `InsGrep`. Every query kept a known hit as its control.
- **Query trap, twice this session:** direct operands print padded (`[0x006f2590]`), while indexed
  ones print unpadded (`[EAX*0x4 + 0x6ee540]`). The Ghidra runner also splits arguments on spaces,
  so `+ 0x409]` became `+`, which matches every line. Both mistakes returned 0 hits or every
  line, and the control exposed each one.
- **Verdicts:** CONFIRMED (the binary agrees), WRONG (it does not), UNSURE (it leaves it open).

## Summary of findings

| # | ADR | Finding | Verdict |
|---|---|---|---|
| 1 | 0232 | `0x006F286B` **is** written: variable 224 stat 4, shipped **60** | WRONG |
| 2 | 0234 | `0x0048B120` has 20 call sites, not twelve | WRONG (count only) |
| 3 | 0229 | The door column follows the slab list; ours is always leftmost. It follows the camera at placement (owner) | WRONG |
| 4 | 0227 | The pit finder measures to `FirstSlab`; ours to the lowest tile | UNSURE |
| 5 | 0228 | Two more `AngerNoWork` sites: states 59/109 (per turn) and `0x004E88A0` | missing from the docs |
| 6 | 0227 | Level data can supply job 26 through `+0x409`; no shipped level does | claim needs qualifying |
| 7 | 0229 | `PrisonJailBroken` is set only per released creature, and every lock toggle clears it | WRONG (inert) |
| 8 | 0227, 0229 | Room `+0x27` is DK2's own `CentrePos`, not a guess | the doc undersells it |

## 1. ADR-0232: `0x006F286B` has a writer — WRONG

- **Doc:** ADR-0232 §3 re-confirms "no store at all to `0x006F286B` (Q337)". `combat-damage.md` §2
  says the one indexed store to `+0x31B` belongs to another structure.
  `MeleeDamageRules.PossessionIncreasePercent` guesses variable 98 (12).
- **Binary:**
  - `0x0051DC70` stores a word at `[EAX*2 + 0x6F2863]`, with `EAX` = record `+0x14` and the value
    from record `+0x18`. `0x006F2863` is `global+0x313`, so stat 4 lands on `global+0x31B` =
    `0x006F286B`.
  - The handler sits in the variable handler table at slot `0x006A6BC0`. Its id, `0xE0` = 224, is
    stored at `0x0051C750` by `0x0051AA90`.
  - The dispatcher `0x00521790` calls `handler[id]` for each 32-byte VARIABLES record with id
    below `0xF3`. Record `+0x10` is the id, `+0x14` the first payload field, and so on. The
    same table routes ids 4–16 to `0x0051CB80`, which is Q69's second dispatcher, and that
    agrees with the table reading.
  - Variable 224 is `CreatureFirstPersonStats` (`kwd-variables.md`), with payload
    `(statId, value, level)`.
  - `GlobalVariables.kwd` ships stat 4 = **60**. None of the 66 level files overrides it.
  - Stat 4 is `MeleeDamage`: variable 65's twin handler `0x0051DC40` fills the 2-D table at
    `global+0xCF`, and stat 4's row starts at `global+0x117`, which is `combat-damage.md`'s
    `statRow[MeleeDamage]`.
- **Consequence:** with `(ceiling + v) / ceiling`, a possessed creature's melee is **×1.6** and
  one influenced by possession **×1.3**. Ours gives ×1.12 and ×1.06.
- **Why the audit missed it:** the audit scanned for the absolute address and for
  `+0x31B` displacements. This store reaches the address as base plus index, and neither
  search can see that.
- **Confidence:** CONFIRMED for the store and the shipped value. STRONGLY INFERRED that the
  global file's VARIABLES chunk passes through this dispatcher: the dispatcher's own caller,
  `0x0051E0A0`, was not traced.
- **Action:** rewrite Q337 as answered and correct `MeleeDamageRules`. Variables 98 and 113 still
  have no reader.

## 2. ADR-0234 (Q427): caller count — WRONG, conclusion CONFIRMED

- **Doc:** `0x0048B120` "has twelve callers" (ADR-0234; the `LevelUp` comment in
  `CreatureLevelRules.cs`; the Q427 row).
- **Binary:**
  - `CallScanAll` finds **20** rel32 sites.
  - Twelve are in named functions.
  - Eight are in regions Ghidra has no function for: `0x0048D302`, `0x004AFC15`, `0x00505E45`,
    `0x00505E50`, `0x005143B8`, `0x0052022C`, `0x0052053B` and `0x0052096F`.
  - The experience adder's call is at `0x004579D4`.
- **Confirmed:**
  - `+0x403` byte stores: the constructor's 1 at `0x004875C9`, `0x0048AFA0` at `0x0048B06A`, and
    `0x0048B120` at `0x0048B1E6`.
  - Wider stores spanning `+0x403` exist only in `0x005306F0`, `0x0055DF60`, `0x005604C0`,
    `0x00566FC0`, `0x0058DDxx` and `0x0065C120`. `0x005604C0` is called only from `0x0055xxxx`.
    None is a creature routine.
  - `0x0048AFA0` has three callers.
  - The rescale matches ours: the cap at 10, a 12.12 divide of health by the old maximum, a
    12.12 multiply by the new one, and the difference applied through `0x004B2CE0`. Level 1
    uses the raw `def+0x692`.

## 3. ADR-0229: which tile is the door — WRONG (owner-observed)

- **Doc/code:** `JailBreakRules.DoorTile` walks interior tiles in ascending index, so the door is
  always above the leftmost top-row floor tile.
- **Binary:** `0x004E32C0` walks the slab list from `FirstSlab` (`+0x2D`/`+0x2E`) through each
  tile's `+4/+5`. The door is the tile north of the first floor tile whose N, NW and NE are not floor.
  In a 5×5 Prison all three top-row floor tiles qualify, so the list order decides.
- **Owner, 2026-09-26 (screenshots, all taken with the minimap's north up):** the door is always
  in the map's north wall, which confirms the N test. Its column depends on the camera when the
  room is placed. Built with the default camera, it is right of centre, and repeat builds put it
  there again. Built with the camera turned 180°, it is left of centre. So placement orders the
  slab list by camera direction.
- **Test:** `JailBreakTests.TheDoorIsNorthOfTheFirstFloorTile…` pins (5, 2), our order.
- **Answered (Q439):** build order, not the camera (`room-slab-chain.md`, ADR-0240).

## 4. ADR-0227: the pit finder's distance — UNSURE

- **Code:** `Finder` measures to `room.TileIndices[0]`, the lowest index.
- **Binary:** `0x004D8510` measures to `FirstSlab` (`room+0x2D`, `+0x2E`, tile centre `+0x800`),
  the head of the same slab list as §3.
- **Effect:** this only changes which pit wins when several qualify.
- **Related:** the route point `0x004E5000` (listed as a guess in ADR-0227) is now read. It
  walks the same list from `FirstSlab`, at most `Size` steps, and returns the first tile that is
  not interior and whose `0x00455000` byte is 0. On a pit that is the first rim tile in list
  order.
- **Q439.**

## 5. ADR-0228: `AngerNoWork` has two more sites — missing from the docs

Readers of `def+0x6C7` (`InsGrep 0x6c7]`, plus `0x6c4]`–`0x6c6]` for wider loads):

| Reader | What it is | In ours |
|---|---|---|
| `0x004780E0` | the happy chooser, once per failed pass | built (ADR-0228) |
| `0x00457B40` | state 139 in the pit, buckets 0 and 3 every turn | built |
| `0x00478F00` | the first call of state 59's handler `0x004762D0` and state 109's `0x004859E0`: charges bucket 3 **every call** | 59 and 109 not built |
| `0x004E88A0` | a room method, reached from `0x004E8660` when a creature on a Library or Training Room tile is not researching or training | not built |

- **What `0x004E88A0` does:** once a second (`now % var19 == 0`), for each non-worker in the
  room within the squared distance `0x9000` of the intruder, and in state 56 (Library) or 112
  (Training Room):
  - it charges that creature's `AngerNoWork`;
  - or, past a bucket-5 threshold of 10% of `def+0x76A`, sets it to 59 or 109.
- **So:** an intruder disturbs researchers and trainees. `training.md` §2 says states 108–113
  award nothing, which is true, but 109 does charge anger.
- **Loader:** `0x00493A0B` divides the file value by var19 and floors it at 1, as ADR-0228 says.
- **Q438.**

## 6. ADR-0227 and 0232: "the only code that selects job 26" — CONFIRMED for code, qualified for data

- **Code:** the only `PUSH 0x1A` into the job dispatcher `0x00478800` is at `0x004786D7`, in
  `0x00478680`. The dispatcher has 13 call sites.
- **Data paths into the dispatcher:** four sites pass a job from memory.
  - Creature `+0x409`, through `0x00477850` and `0x004787F0`.
  - `+0x6E` and `+0x68`, through 89.
- **Writers of those fields:**
  - `+0x409` is written only by `0x0048BE20` (16 call sites) and two direct stores.
  - Its data-driven callers are the level loader at `0x0052077B`, whose byte `+0x4F` is a
    placed hero's `Objective`, and the party spawn at `0x0051A4A7`.
  - `+0x6E` is only cleared or copied.
- **Shipped data:**
  - Placed heroes in all 66 levels have objectives 13, 18, 19, 22 and 23 only (`kwd things`).
  - Party members have the same set (`hero-parties-and-effect-generators.md`).
  - No creature's happy, unhappy, angry, hate or alternative table lists 26 (all 46 creatures).
- **Verdict:** the claim is true for shipped data. A custom level could author job 26.
- **Handler callers:** `0x00457780` has exactly two, `0x00457F2A` and `0x0047897E`. `0x00478680`
  has one, `0x00477A92`.

## 7. ADR-0229: `PrisonJailBroken` — WRONG (inert)

- **Code:** `Release` sets `Broken` unconditionally.
- **Binary:**
  - `0x004E2E70` calls `0x004E2D40(1)` inside its loop, once per released creature. In the
    no-door branch it does so only when a rim tile is found. A release that frees nobody
    leaves the flag alone.
  - The lock toggle `0x004E3680` starts with `0x004E2D40(0)`, and so does `0x004E2B30`.
    `jail-break.md` §2 lists only the set.
- **Impact:** nothing in ours reads `Broken`, so only the hash and the snapshot differ.

## 8. Room `+0x27` is `CentrePos` — the doc undersells it

- **Doc:** ADR-0227 lists "room `+0x27` as the room's centre" as a guess.
- **Binary:** DK2's room dumper names `+0x27` `CentrePos` (`room-object.md`). The meaning is
  confirmed.
- **Still open:** how DK2 computes it. `CombatPitSpectatorRules.Middle` (the bounding-box middle)
  remains ours, and 122's turn uses it too.

## ADR-0227 — checked and CONFIRMED

- **Entry mask:** category 31's mask is `0x8002383B`. `ESI` is loaded at `0x00479B47` and
  stored to `0x00699E7C` at `0x0047A6CE`. It lacks bit `0x10000`.
- **Categories:** 138 is category 16 (`0x0047E117`); 142, 143 and 144 are category 31
  (`0x0047E1A6`, `0x0047E1D1`, `0x0047E1FC`).
- **Transition rule:** `0x00476D30` admits a move when the source's bit is in the target's mask,
  so 138→142 is refused.
- **Job 26, first arm:**
  - `0x004539B0(0x10, owner, 1)` on the creature's tile. With the third argument set, a tile
    owned by another player passes when that owner's table allies the creature's keeper
    (`0x004BF3D0`, `ECX` = the tile owner's player).
  - The floor test `0x00454110` must be 0.
  - It stays when a fight is found or `now − room+0x53 < var19×4` (`JNC` at `0x00457806`).
- **Finder call:** `ECX` = creature `+0x2A`, arguments `(position, &room, creature)`, then
  `0x00471A70(room, 142, 0x80)`.
- **Fight test `0x004D7CF0`:**
  - It walks the slab list, floor tiles only.
  - For each thing with `+0x12` = 0 it calls `0x00478050` on thing `+0x24E` and compares with 22.
  - It stamps `room+0x53` and returns `RET 4`.
- **Finder `0x004D8510`:**
  - Type byte 16.
  - Reachable is `0x00471740(room, 0x80)` = `0x004E5000`, then `0x004B35C0`, then `0x004D0E60`.
  - Capacity is `0x004E53B0(def+0x672)`, i.e. `+0x6B` plus the need ≤ `+0x67`.
  - Distance is `(dx²>>12)+(dy²>>12)`, with strict `<` for both the nearest and the nearest
    friendly.
  - The objection test `0x0045C640` runs with `ECX` = the seeker (`[EBP+0x10]` at `0x004D864D`).
    A seeker with bit `0x100` objects to bit `0x80`; any other seeker objects to bit `0x100`.
    That matches our `IsGood` = `1<<8` and `IsEvil` = `1<<7`.
- **State 142 (`0x00457D60`):**
  - The neighbour count is 8 on floor and 4 on the rim.
  - Crowded means another tile-list thing with `+0x12` = 0. A crowded creature calls
    `0x004E50F0(pos, &out, count, 1, 1)`, with `ECX` = the attached room `+0x3B9`.
  - Otherwise `random(100)` ≤ 35 (`JBE`) moves it.
  - The point is `random(0x555)+0x555`, x first, and the walk is `0x00471890(pt, 0x8F, 0x80)`.
  - Refused: `SetState(1)`, then job 26.
- **Neighbour search `0x004E50F0`:**
  - It uses table `0x006BC350` for 4 neighbours and `0x006BC370` for 8, from a random start.
  - Each tile must be the same room and not solid.
  - With the fourth argument, the tile must be off the floor; without it, floor-ness must match.
    With the fifth, nobody may stand on it (`0x00453670(0)`).
- **State 143 (`0x00457F40`):**
  - Crowded, it goes back to 142.
  - It turns toward the attached room's `+0x27` through `0x004B2AE0` (`ECX` = creature).
  - Animations 8 and `0x14` are tested at `+0xB` bit 0.
  - The cheer is action 69 when `random(100)` > 30, otherwise 70. Action 69 takes the parameter
    `random(2)+1`.
  - Then `SetState(0x90)`.
- **State 144 (`0x004580D0`):**
  - It looks at `Age` 1 or when unmoved (`+0xAC`/`+0xB0`).
  - Moved and 142 taken, it ends the action and returns 1.
  - Its quiet test is `>` (`JBE` at `0x004581BD`), on any room tile (`+2 & 0xFFF`).

## ADR-0228 — checked and CONFIRMED

- **State 89's order (`0x004778E0`):**
  - dying (`+0x3E6` bit 21);
  - clears bits 20 and 3;
  - casino `+0x3F2` → 1, then 155;
  - Call To Arms (player `+0x76` & 4) → 191, or message `0x54`;
  - lair job;
  - `+0x6E`, then `+0x68`;
  - `0x004780E0`, then `0x00478680`;
  - `+0x7E` = now + 12, `SetState(3)`.
- **Happy chooser:**
  - It returns at category 22.
  - Gate: `+0xFD` is 0, or `now − last ≥ var19×5`.
  - It charges `0x00478B80(3, def+0x6C7, 3)` on a failed pass.
- **Fallback `0x00478680`:**
  - `def+0x686` bit 21 skips Train;
  - Train when `0x004BAB20(7)` is 3, then job 26 when `(16)` is 3, then Drink when `(15)` is 3;
  - the Heart is `0x004C6460(5)`, aimed ±2 tiles by `random(2)` on each axis, x first;
  - it returns 0.
- **Job ids:** our `JobType` values Train 4, `CombatPitSpectate` 26 and Drink 9 are the pushed
  literals.
- **Availability `0x004BAB20`:** player `+0x5A`, 9-byte rows keyed by room type, count `+0x56`;
  it returns 1 with no row.
- **State 3:**
  - `0x00477680`: an action running → 1; `now < +0x7E` → `0x004776C0`; else 0.
  - `0x004776C0` with `+0xA7` set rolls `random(100)`: below 25 wanders, 25–74 plays action
    `0x2B`, 75–99 action `0x2C`. A missing action falls through to the wander.
  - A played action clears `+0xA7`. For a creature with `+0x36F` = 3, `random(100)` ≤ 9 plays
    sound `0x35`.
  - The wander is `SetState(0x57)` with `+0xA7` = now + `0x84`.
- **Loader:** `0x00493A0B` divides the file value by var19 and floors it at 1 (`0x00493A1D`).
  Goblin 50 → 12.

## ADR-0229 — checked and CONFIRMED

- **Door routine `0x004E32C0`:**
  - It zeroes `+0x51`/`+0x55` first and tests N, NW and NE.
  - It places objects `0x6D` and `0x74`.
  - Its one caller is the rebuild `0x004EDFC0`, at `0x004EE045`.
- **Contact arm (`0x004E8660`, room type `0x0B`):**
  - The rescuer must be a non-worker.
  - The room must have `+0x6B` ≠ 0.
  - The rescuer's `+0x46F` must have `0x20` set.
  - Its keeper must not be allied with the owner, or the owner is `[0x006F2548]`.
  - It must not be in category 14, not in or resuming 122, and not in category 22.
- **The prisoner loop:**
  - It skips a dying prisoner.
  - The ally test runs with `ECX` = the prisoner's player and the argument = the rescuer's
    owner (`0x004E8781`).
  - The neutral player is excluded.
  - `SetState(0x7A)` runs with `ECX` = `EDI`, the rescuer (`0x004E879C`).
  - Our `AreAllied` is symmetric, so the direction does not matter.
- **State 122 (`0x00473C60`):**
  - It needs a Prison tile and both door words non-zero.
  - Off the door, a walk `(door, 0x7A, 0x80)`.
  - On it, a turn to `+0x27`, then `0x004E3680` if `Activation` is 0, then `0x004E35C0` and
    return 1 if `DoorOpen` is 0.
  - Otherwise `0x004E2E70(1, owner, 0)`, `SetState(1)`, `SetState(0x57)`.
- **Release `0x004E2E70`:**
  - Floor tiles only; `+0x12` = 0; not dying; owner, or everyone.
  - With a door, a random point in the tile south of it, x first, options `0x68`, then `0x6C`.
  - Without, `0x004E8430`.
  - Callers: `0x00473DB5`, `0x004E2C63` and `0x004E2E66`.
  - `0x004E2E60` = `(1, 0, 1)`, called from `0x004E5CBB` and `0x00514462`.
- **State 121 (`0x004739E0`):** it follows `jail-break.md` §6 step by step.
- **Auto-close count `+0x61`:** it is written only by the reset (`0x004E2921`), the two imp arms
  (`0x004E29D4`, `0x004E2AB1`, each var19 × 2) and the countdown (`0x004E2AFC`). The `cseg` hit
  at `0x00653019` is a text string disassembled as code.

## ADR-0230 — checked and CONFIRMED

- **Reward choice `0x004F2370`:**
  - A victim of the hero player (`0x006F254C`) skips the class step.
  - Class 0 gives 3 and 7, class 1 gives 4 and 9, class 2 gives 5 and 8; then 10; then 11.
  - The pick is `rand(n+1)`.
  - Reward 11 is not recorded; the others go through `0x004C2B60`.
  - Speeches: `0x61`, then `0x60` for 3–10 or `0x3B` for 11.
- **Tables:**
  - `0x004C2BC0` compares the two players' `+0x10FC` indices, then reads the 12×7×7 dword table.
  - Callers: the panel's `0x00423AEC` and seven in `0x004F2370`.
  - `0x004C2B60` has one caller. `0x00423A80` has eleven.
- **JobClass:** `def+0x67D` is loaded at `0x00493464` from source `+0x12F2`, which is file
  `0xF02` + `0x3F0`.
- **The call:** the torture hook's call at `0x0047B64D` runs when the victim's owner ≠ the room
  owner and virtual `+0x28` holds.

## ADR-0231 and ADR-0233 — checked and CONFIRMED

- **Door tile test `0x00450DB0`:**
  - owner match; route-map bit 3 clear; no room; no thing of class 4 or 3;
  - a Barricade passes at that point;
  - otherwise both of (N, S) or both of (E, W) must be solid, from `0x006BC350`.
- **Trap tile test `0x00450EB0`:** the same without the neighbour test.
- **Placement:** the world slots are `+0x244` (`0x005093E0`) and `+0x24C` (`0x00509430`), in the
  vtable at `0x0066E3EC`.
- **Orientation:**
  - Slot `+0x208` is `0x00509200`, the same route-map bit 3.
  - `0x004C33B0` starts at 2, skips for a Barricade (`+0x8B` & `0x40`), tests W and E → 1, then
    N and S → 0.
  - The loader `0x00520F40` does the same, starting at `0x00520F7B`.
  - Pushes into `0x004B7B50` are `(type, owner, &pos, orientation, &out)`, and the trigger path
    `0x00518D47` pushes 0 as the fourth.
- **Callers:**
  - `0x004B7B50`: three.
  - `0x004C33B0`: `0x00513233` and the computer keeper `0x0050007D`.
  - `0x004C3860`: `0x00513293` and `0x004FFF9A`.
- **Price:**
  - Door `+0x8F` word; `0x004A7100` copies it from source `+0x2A2` = file `0x22E` + `0x74`.
  - Player gold `+0x7E`; message `0x37`; `0x004BA420(−cost)`; `+0x177` += cost.
- **Order:** `0x005066E0(0 or 1, type, pos, player)`. The decompiler shows orientation there;
  the pushes show `EDI`, the type.
- **UNSURE (inherited):** ours reads "solid" as terrain `IsSolid`, where DK2 reads route-map bit 3.

## ADR-0232 — the rest, checked and CONFIRMED

- **Heart position:**
  - `+0xB43`/`+0xB45` are written only at `0x004B883E`/`0x004B8845` (`EBP` = 1 from
    `0x004B8662`) and in the setter `0x004C5F60`.
  - The setter's callers are `0x004DB91B` and `0x0050B926`.
  - No wider store spans either word.
- **Variable scans (address plus three bytes below):**
  - Store only: variables 6, 11, 22, 175, 176 and 193, and `0x006F28E0`, `0x006F28F2`,
    `0x006F2910`, `0x006F2912`.
  - One reader each: variable 112 (`0x0047BB4E`) and variable 180 (`0x004D9BCE`).
- **Wide loads that span a neighbour, all masked:**
  - `0x0047BB4E` reads a dword over `0x006F28F2` and masks with `AND 0xFFFF`.
  - `0x004D9BCE` masks with `AND 0xFF`.
  - The dword loads at `0x006F2974` (`0x0041D201`, `0x00460BD4`) mask with `AND 0xFF`.
- **Other globals:**
  - `0x0075B174`: one writer (`0x005A6C6E`) and three readers.
  - `0x006A11F0`: one reference, a read at `0x004D0E11`.
- **Indexed access into `0x006F2xxx`:** two stores only, `0x0051DC59` and `0x0051DC7A`.
  The second is finding 1.
- **One caller each:** `0x00519F90` (`0x00519199`), `0x004B0C90`, `0x004A6DF0`, `0x004C9400`
  and `0x00494D00`. `0x00491AE0` has two.
- **`0x004D9A60`:** its only reference is `.data` `0x006A1F81`. The room-record numbering was
  taken from `portals-room-handler-table.md`, not re-derived.
- **Fields:**
  - `def+0x5C4` is read only in `0x00494D00` (twice) and the copier `0x0051F3B0`.
  - `+0x6B2` is read only by the copier.
  - The word stores to `+0x404`/`+0x406` come only from the constructor (`0x004875DA`,
    `0x004875E7`). The dword stores elsewhere are all at `0x0053xxxx` and above.
- **Out-parameter:** at `0x004E5D5D` only `EAX` is tested after the call.
## Behaviour no test would catch

- **0227:**
  - job 26's own quiet window (a stamp with no fight on);
  - the allied-pit arm;
  - the finder's nearest-pit and opponent-free preference;
  - its capacity test;
  - 142's refused walk re-running job 26;
  - 144 ending the cheer when moved;
  - the `Age`-1 exception;
  - the 35 and 30 thresholds.
- **0228:**
  - job 26 before Drink;
  - the 75 split between actions 43 and 44;
  - a re-wander on a roll under 25;
  - a refused wander going idle.
- **0229:**
  - 121's step onto the door opening it;
  - the no-door rim release;
  - the random point staying in the tile south of the door;
  - the turn before unlocking.
  - The door test pins our tile order (finding 3).
- **0230:** reward 11's reveal (unbuilt); the speech.
- **0233:** nothing reads `Orientation`, so only its own tests hold it.
- **0234:** the level cap of 10 inside `LevelUp` on the `Grant` path, and a dead creature not
  promoting.

## Proposed open questions

| Q | Question | How to close |
|---|---|---|
| Q438 | What do states 59 and 109, and room method `0x004E88A0`, do to a researcher or trainee near an intruder? Both charge `AngerNoWork`; 59/109 do so every turn | Read `0x00478F00`'s watch arm and `0x004871A0(5)`, then decide whether to build them |
| Q439 | In what order does DK2's slab list run? It decides the Prison's door tile (`0x004E32C0`) and the pit finder's distance (`0x004D8510`) | Read the room builder's slab insert, or record the door after a tile-by-tile build. Owner (2026-09-26): north wall; default camera gives right of centre, turned 180° gives left |
| Q337 | Rewrite as answered (finding 1); no new number | — |
