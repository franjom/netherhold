# Where a dropped creature lands: states 76, 255, 257, 250 and 77

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25; 257 and 250 re-read by disassembly on 2026-10-03.
- **Built:** ADR-0226, `DropLandingRules`; 257's hold and 250's stamp ADR-0345.
- Corrects:
  - ADR-0193 and `state-category-hooks.md` §3.2: a hand drop enters 76, not 77.
  - Q201's "the only site pushing a stunned state is the flee routine".
  - ADR-0215, where a job room's drop acted at once.

## Result in one sentence

The keeper's drop enters **76**. An own non-worker that no room takes over is **stunned** (255) for its definition's `StunDuration`. It gets up in **257**, and 257 asks the room underfoot: the room's drop state, or with none, **250**, where it stands until 50 turns past the state word `+0x7E`. **77** is the slap's reaction.

## 1. The drop enters 76 — CONFIRMED

- **The keeper's drop.** `0x004BC710` clears the in-hand bit (`+0x3E6` bit 0), stamps `+0x4BF` with now and calls `SetState(76)` (`0x004BC7CD`–`0x004BC7D7`).
- **Level-placed creatures (STRONGLY INFERRED, binary 2026-10-10).** After rebuilding rooms,
  `0x0050B5B0` kicks THINGS flag 0x40 creatures to 76; the loader copies that flag through
  `0x0048E000`. `LevelPlacedCreatureRules` runs their room take-over after furniture exists:
  Prison 114→115 with the room owner as jailer, retaining starting health; the independent
  non-worker/hero/neutral fallback also admits neutral creatures on neutral-owned cells.
  Torture 130→131,
  with own creatures kept distinct from captives. Level 8 has two neutral Dark Elves in cells
  and three neutral Mistresses on devices, including the wall wheel at (12,10).
  Deviation: initialization finishes on the first simulation tick, rather than before it.
- **77 is the slap's.** Its only way in is the slap handler `0x0048B8A0` (`0x0048BA06`), which deals `SlapDamage`, fires creature event 5 and player event `0x3C`.
- **What 77 does** (`0x0046F1F0`). It plays action 19, then sends a creature to the state of the room it is attached to (`+0x3B9`): Workshop 63, Prison 115, Torture Chamber 130. A Horned Reaper goes to 238, a worker to 10, anyone else to 1.

## 2. State 76, `0x0046EAB0` — CONFIRMED (the arms used here)

**Vertical gate and sound (binary reread 2026-10-29; Q680).** At
`0x0046EE1A..4A`, virtual `+0x88` is the creature flying test
(`0x0048ED40`): a flying creature has its Z lowered by `0x800` on this
state update. A non-flyer instead uses the shared gravity pass
(`0x004B2030`), already described in imp-create-position.md. The gate
`0x004B1FA0` predicts Z plus the signed, toward-zero quotient of
(persistent vertical velocity + temporary vertical velocity) / 16 and
compares it with world floor height (virtual `+0x204`). Above it, 76
returns success without the room/stun transition. On contact it requests
sound event `0xF6` from creature-definition sound category `+0x6E3`, except
on a Temple floor accepted by `0x00454110`. These operations are CONFIRMED;
there is no fixed descent duration in this handler. The human input's
initial drop Z and complete trajectory still require a matched native
capture. The requested position is supplied unchanged to `0x004BC710`.

The evil worker's landing art is already correct in our view: its `idle`
key resolves to catalogue index 19 (native slot 20). Other creatures use
slot 24 deferred; see animation-transitions.md section 1. No presentation
correction follows from the key's name.

After its landing work (the pit scatter, the fall, and the sound gate above), it:
1. **Sets 1**, and a dying creature stops there (`0x00459560`).
2. **Writes `WasLastDroppedOnARoom`** (bit 26) from the tile.
3. **An enemy room.** On a tile whose owner is neither the creature's nor the neutral player's, a room that takes the creature over (`0x004E8DA0(1)` = 0) gives its enemy state `+0x1D`, through 1. `+0x3CB` takes the room's owner. This is a captive into a cell (114) or onto a device (130).
4. **A non-worker, or a creature of player 1 or 2.**
   - **Taken over:** on a room that takes it over, `0x004E8DA0(0)`.
   - **Otherwise:** `0x00491830(255)` — `SetState(255)`, action 52, creature event 15. **This is the drop's stun.**
5. **A keeper's worker** has its own arms (`0x00481400`, the Treasury, Portal, Heart, Workshop and Temple cases), and falls back to 11.

**`0x004E8DA0(1)`, "does this tile take the creature over".** It returns 0 (yes) for:
- a Portal or Hero Portal tile whose cell byte `+0xC` is 4,
- a Prison or Temple tile the cell test `0x00454110` refuses,
- a free torture device under the creature (`0x004F2180`),
- a Combat Pit tile the cell test refuses (its floor).

Anything else, a Library or a Lair included, returns 1.

## 3. The stun, 255 → 257 — CONFIRMED

255 (`0x004810E0`) stands while `now − state+0x18 < StunDuration`, a count of turns (the loader `0x00493B57` stores file seconds × ticks a second >> 12; `creature-status-effects.md` §2). Its first call, the turn after the stun is set (Age 1), creates effect 101 (Creature Dazed Effect) only when the duration hold arm is active; zero duration skips it. Its art has "dazed" stars and a sparkle on a circular path, tied to the creature's `0x1388` point, which `0x00434E80` follows (ADR-0480). `CreatureStunnedTimeSeconds` (2) is never read. Then 257 (`0x00481200`):
1. It returns 1 while an action is current (`0x00496D00`): the get-up, action 53, which 255's end started. Action 53 plays animation block 27, Getup (`0x006E47AD` = 27), so the hold is that animation's frames at its rate: `ceil(frames × 4 / rate) + 1` ticks, the +1 being the tick the handler sees the action end, as for the Entrance animation (`portal-creature-action.md`). That `+20` selects the block is STRONGLY INFERRED (`creature-action-table.md` §3), and the formula is fitted (STRONGLY INFERRED): it reproduces every 257 run the Level 1–3 captures measured (the longest of a type that ends in a room drop, the same in every run): Warlock (5) 51 frames, 8 ticks; Goblin (7) 69, 11; Troll (10) 29, 5; Firefly (12) 84, 13; Dwarf (14) 16, 4; Lord of the Land (29) 22, 4. A fight can cut it short (257 → 8 or 199): 257's flag byte is 2, without bit 6, so `ProcessFight` runs while it holds (`combat-acquisition.md` §2). Measured in the Level 4 seed 1 capture: DK2's dropped creatures go 257 → 199 or 8 one to eight ticks after getting up, each on the hero registered on it (ADR-0488).
2. It steps a little along its facing: the definition's vector `0x00494650(def, 2)` (`def+0x6EC+0x18`) turned by the heading `+0xF4`, taken if the terrain there is not solid (terrain table byte `+0x17B` bit 0 clear) and the cell test `0x00454110` gives the same answer for the tile there as for its own.
3. It runs `0x00481400` (`SelectAVictim`, else a neighbouring enemy door as the forced target; `door-blocking.md` §8) and returns 1 if that found something; otherwise **`0x004E8DA0(0)`**. So a shot's stun ends the same way as a drop's.

`0x004E8DA0` has a fourth caller at `0x00481351`, inside 257. The function-based caller list missed it, because Ghidra had no function there.

**Shipped `StunDuration`, all 46 records.** Most creatures lie 2–3 s, which matches the owner (Q201). Goblin 1, Imp 0, Bile Demon 5, Mistress 3, Knight 4, Stone Knight 5; the Dwarf, Firefly and Fairy 0.

## 4. `0x004E8DA0(0)`, the room drop, and 250 — CONFIRMED

It runs only while `CasinoBigWinnerGameTickRemaining` (`+0x3F2`) is 0.

**On a room with a drop state** (`+0x19`, `portals-room-handler-table.md`):
- A keeper's creature is refused if it hates the room's job (`0x00494480(+0x25)`).
- Otherwise it goes to 1 and then the state, and `DroppedToDoJobId` takes the job.

**With none**, for a keeper's creature (not the hero player or the neutral one):
- `+0x2CC` takes now. **This is the state word `+0x7E`:** the state record lies at `creature+0x24E`, and `0x24E + 0x7E = 0x2CC`.
- Unless the owner's `CallToArmsOn` (player `+0x76` bit 2) is set, `FightProcessCounter` (flag dword bits 22–25) becomes 10 and the creature enters **250**.

**250** (`0x00481CF0`, "guarding") returns 1 while an action is current; else, while `+0x7E + 50 > now` (unsigned), it starts action 43 if the creature has it (`0x004966C0`, `0x00496700`) and returns 1; otherwise 0, which is idle.
- So a dropped creature stands for **50 turns from getting up**, or until a fight takes it. Level 1's six Goblins at (15,14): 76 × 1, 255 × 4, 257 × 11, then 250 until the Lord comes into sight at 1728–1730.
- (Until 2026-10-03 this section said `SetState` does not write `+0x7E` and the stand could reach 62 turns; the stamp above was missed, and ours inherited the gap: ADR-0345.)

## Differences in ours

- **Kept arms.** The Combat Pit floor, the torture devices and the Prison arms stay the direct arms they were. A captive now goes through 1, where it went through 77.
- **Lair drops** run state 79; see lair-acquisition.md §9 and ADR-0450. Other
  unsupported room drop states still idle. The Combat Pit's 137 is built
  (ADR-0227); the Heart's 71 is built under ADR-0400.
- **Workers.** A worker's getting up idles; `0x00481400` (read, `door-blocking.md` §8) is not built, in 257 or in 76.
- **The stun's count and stars** (ADR-0480): 255 and 256 count from the turn after they are set; the first counted turn asks for effect 101 once only while the duration hold remains active (zero duration skips it), at the creature's position on the floor, tied to the creature's mesh point 5000. Point 5000 is a KMF CTRL point, the head: slot `+0x10C` is the engine interface's `0x0059D380` (that the callers' object is that interface is LIKELY), which searches the drawn mesh's tagged points (`0x0059D260`); an animated entry is (primitive, vertex) evaluated at the drawn frame (`0x005835E0`). The view keeps each drawn creature's head and moves the effect and its stars with it each update, as `0x00434E80` does; a creature drawn with a static mesh is not resolved, so its stars stay where they were asked for. The drop goes straight to 255, so ours spends no turn in 76 and gets up one turn before DK2.
- **Not modelled:** the landing's sound, fall and pit scatter; 257's step along the facing; the casino-winner gate. The Call To Arms branch is built (ADR-0279).
- **257's hold** is `ActionUntilTick`, set at the stun's end to `DropLandingRules.GetUpTicks`, the Getup animation's length (ADR-0348, Q527). Classic presentation plays 255's knockdown once and holds its final pose, then plays 257's Getup once from its state-entry stamp. Action 52's art selector is 24 (`0x00495F36`), action 53's is 27 (`0x00495F6E`); independently checked 2026-10-13.
- **One word for `+0x7E`:** `StateDeadlineTick`, stamped with now by `AfterGettingUp` on the way into 250, as `0x004E8DA0` stamps `+0x2CC`.
