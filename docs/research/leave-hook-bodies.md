# Mechanic: four leave hooks and the Workshop's maker count

Source: `DKII.EXE` v1.70 (GOG), Ghidra 12.1.4, read instruction by instruction on
2026-10-02. Each hook's prologue, epilogue and every call's `ECX` were read; field names
are DK2's own, from the dumpers (`debug-dumpers.md`). Confidence is **CONFIRMED** (binary)
unless a line says otherwise. Built as ADR-0322; what each slot lacks is in
`CategoryHooks.Slots`. `state-category-hooks.md` §3 owns the hook table.

## 1. Casino leave `0x0047AF20` and the fearless countdown

- **The leave**, in order: if creature `+0x3EA` bit 13 (`CasinoGamblingFearless`) is set
  (`AND ECX,0x2000; CMP; JNZ`), clear it (`AND DH,0xDF`) and write `FearlessUntil`
  (`+0x3D7`, dword) = `0x00517490()` (the turn) + `[0x006F2590]` × `[0x006F2994]`, that is
  variable 19 × variable 203 (`GamblingDelayBeforeBecomingFearlessSeconds`, 120). DK2 reads
  203, the *delay*, not 204 (`GamblingFearlessDurationSeconds`, `0x006F2998`); both ship as
  120, so the difference is unobservable on shipped data. Then, unconditionally, the
  release and the Crypt tail of the shared leave `0x0047B920`. Without the flag nothing is
  written: the earlier note's unconditional stamp was wrong.
- **The countdown**, in the update `0x0047AD70` after its own `+0xB0` reload and before
  the purse test: a nonzero `CasinoGamblingDuration` (`+0x4F4`, word) is decremented; on
  reaching 0 bit 13 is set (`OR CH,0x20`) and the word reloaded from variable 203. The
  constructor seeds it from variable 203 (`0x0048790C`), and no enter or leave hook
  touches it, so it counts the update's acts, one every fifth turn (Q424), across visits:
  after 120 of them, 150 game seconds in all, the next leave makes the creature fearless
  for 120 game seconds (variable 19 × variable 203 turns).
- **Who reads what.** A scan of every instruction in `.text` and `cseg` (742,900 in all) for
  `+0x3D7`–`+0x3DA` finds the leave's store `0x0047AF64` (the positive control), the
  constructor's store `0x004874EB`, the dumper, and four readers, all `now < FearlessUntil`
  (`CMP`, `SBB`, unsigned): the fear counter `0x0045EC80` (`0x0045ECD8`) and the withdraw
  test `0x0045EDA0` (`0x0045EE28`), both treating the creature as fearless
  (`combat-fear.md` §3.2–3.3), and two in `Creature_Update` (`0x00489B0D`, `0x00489E4F`),
  each skipping the `ThreatThing` fear response, which is not built. The scan for bit 13 by
  the instructions after each load of `+0x3EA`/`+0x3EB` finds only the setter
  `0x0047ADB2` and the leave, and the dumper's shift.
- **Built:** the countdown, the flag, the stamp and both fear-rule readers.

## 2. Sleeping leave `0x0047AB90`: believed presentation only

- **The body.** If creature `+0x3B7` (`LairId`) names a live thing of class 2 (`+0x12`),
  that thing's `+0xF3` bit 0 is **set** — `(x AND 0xFE) XOR 1` leaves it 1 whatever it was,
  so the earlier "toggles" was wrong — and then the creature's own. It returns nothing.
- **The bit.** `+0xF3` is `+0xAF` of the `RenderInfo` object at thing `+0x44`
  (`0x004B3560`: `LEA ECX,[ESI+0x44]`, then its vtable dumper `0x004B47C0`, which prints
  its fields up to `+0xAE` and not this byte, so DK2 gives it no name). Its initialiser
  sets it (`0x004B36E6`, `OR AL,1`). Its readers are draw tests: the per-viewer visibility
  predicates `0x0048E830`, `0x004A6E10` and `0x004AA050` (each after a `+0x6C` render
  pointer, then the local player, `world+0x264`), the render-record copy `0x0056B10D`,
  and the 3D code at `0x0059B102`, `0x0059B919`, `0x0059BBC2`, `0x0059D288`. So bit 0 is
  the thing's draw flag (STRONGLY INFERRED: every reader found draws, and the three
  predicates are reached only through vtable slots whose callers are unread). State 83
  `0x0046FA60` clears the lair's (`0x0046FC2E`, unless definition `+0x771` is 6) and swaps
  the flag between the creature and a second render object as its animations end; a hero
  stepping out of a gate has its own cleared (`hero-parties-and-effect-generators.md`).
- **Not built:** the simulation keeps no draw state. The slot stays NotBuilt with that
  reason; a renderer that hides a sleeper or its lair would build it.

## 3. Torture leave `0x0047B4A0`

In order, all on the leaving creature (`ESI` = state object, `+0x8` = creature):

1. `0x004537E0(cell, 0x3D, &out)` (`RET 8`): the first class-2 thing on the creature's own
   map cell whose type `+0x12C` is 61, the Electric Chair; if found, its `+0x92` bit 0 is
   cleared. That bit gates a render-only effect pass (`0x005A3AA0`, the 4000-range mesh
   points); the chair action at `0x004995B4` sets it every 6 × variable 19 turns. A scan of
   `+0x92` in both code blocks finds that pass (`0x005A406D`) as the bit's only reader, so
   these are the chair's sparks, presentation.
2. `0x004F2180(room = rooms[+0x3B9], creature, &out)` (`RET 8`): the room's devices
   (object list `+0xE`, linked by `+0x12A`; types 8 and 61), skipping one whose tile holds
   another creature in 131 or 136; a free one on the creature's own tile is returned at
   once, else the nearest by squared distance. On 1 the device's draw bit (§2) is set:
   the device is shown again. Presentation.
3. Unless `+0x3EA` bit 14 (`TortureVoluntary`) is set (`TEST CH,0x40`), state `+0x6E` = 0:
   `DroppedToDoJobId`, our `DroppedToDoJob` (state object `creature+0x24E`, so `+0x2BC`).
4. Bit 14 cleared (`AND 0xFFFFBFFF`).
5. The release and the Crypt tail.

Bit 14 is set at `0x00483E65`, in 127's strap path `0x00483B90`, only after the walk
`0x00471890(device centre, 130, 0x68)` returns nonzero. Its other readers (`0x0045A747`,
`0x004BDED7`, `0x004BDEFC`, `0x004D99D2`; `call-to-arms.md`, `combat-acquisition.md`)
read nothing of ours yet; the flag now exists for them. **Built:** steps 3–5 and the setter.

## 4. The Workshop's maker count: player `+0x8A`

- **It is on the player.** Enter `0x0047B0A0` and leave `0x0047B120` both take the cell
  under the creature (world `+0x1D4`), then `0x00455990`, which resolves the cell's `+0x8`
  thing id, not the room id at `+0x2` that `0x004559B0` resolves. Its callers treat the
  result as a player: Minimap_TileColour reads its `+0x10FC` (`Number`) and `0x00473130`
  calls the alliance test `0x004BF3D0` on it. Player `+0x8A` is the dumper's
  `NumberOfManufacturingCreatures`; room `+0x8A` would be `BeingDeleted`. The decay
  `0x004C3340` reads the same `player+0x8A`. So the count belongs to the keeper who owns
  the tile the maker stands on, not to the maker's own.
- **Enter:** `INC [player+0x8A]`, then clears state `+0xB9`, then the lookup and claim; a
  refused claim does not take the count back. State `+0xB9` as a byte is written 1 and 2 by
  `0x00470110` and read nowhere in either code block. Its dword uses are other states'
  data: Training's `0x004857F0` keeps a last position at `+0xB5`/`+0xB9`, written every
  call and read only once the state is a turn old; the other dword users are not traced.
  The clear is inert as far as read.
- **Leave:** `DEC [player+0x8A]` with no floor, then the release and the Crypt tail.
- **Decay:** with points (`+0x217`) and `TEST EAX,EAX` on the count zero, on turns
  divisible by variable 19, the points fall by variable 138 to no lower than 0. A negative
  count holds the points as a positive one does.
- **What follows:** entering a walk runs no hook (`state-category-hooks.md` §5), so a
  maker whose walk starts from its work state still counts until it arrives and the saved
  caller's leave hook runs. The needs routine sends a creature to bed that way (§3.5);
  hunger instead sets 91 from the job's state (`creature-needs-routine.md` §3), which
  leaves category 5 at once. Ours derived the count from `UpdatingCategory`, which also
  dropped it the turn a walk to bed began. Measured on a fixture Workshop with two makers
  and a Lair: 5 walking seconds, 5 points less decay (1,467 against 1,472 over 30 minutes);
  hunger trips decay the same on both.
- **Death.** DK2's death bookkeeping sets state 1 through `0x00476C80` (`SetState(1, 0)`,
  at `0x004883D8`) before its release, so a maker's leave hook lowers the count. Ours does
  on a death through the dying state, but not for a creature that dies outright
  (`CreatureHealthSystem.Die`), whose count stays raised. Not built.
- **Not modelled:** a tile with no keeper of ours (owner 0) changes no count; what DK2's
  cell id names there is Q514.
