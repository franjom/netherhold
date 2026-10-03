# Where a dropped creature lands: states 76, 255, 257, 250 and 77

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25; 257 and 250 re-read by disassembly on 2026-10-03.
- **Built:** ADR-0226, `DropLandingRules`; 257's hold and 250's stamp ADR-0344.
- Corrects:
  - ADR-0193 and `state-category-hooks.md` §3.2: a hand drop enters 76, not 77.
  - Q201's "the only site pushing a stunned state is the flee routine".
  - ADR-0215, where a job room's drop acted at once.

## Result in one sentence

The keeper's drop enters **76**. An own non-worker that no room takes over is **stunned** (255) for its definition's `StunDuration`. It gets up in **257**, and 257 asks the room underfoot: the room's drop state, or with none, **250**, where it stands until 50 turns past the state word `+0x7E`. **77** is the slap's reaction.

## 1. The drop enters 76 — CONFIRMED

- **The keeper's drop.** `0x004BC710` clears the in-hand bit (`+0x3E6` bit 0), stamps `+0x4BF` with now and calls `SetState(76)` (`0x004BC7CD`–`0x004BC7D7`).
- **Other ways into 76.** Its other caller is the level initialiser (`0x0050BDBE`).
- **77 is the slap's.** Its only way in is the slap handler `0x0048B8A0` (`0x0048BA06`), which deals `SlapDamage`, fires creature event 5 and player event `0x3C`.
- **What 77 does** (`0x0046F1F0`). It plays action 19, then sends a creature to the state of the room it is attached to (`+0x3B9`): Workshop 63, Prison 115, Torture Chamber 130. A Horned Reaper goes to 238, a worker to 10, anyone else to 1.

## 2. State 76, `0x0046EAB0` — CONFIRMED (the arms used here)

After its landing work (the pit scatter, the fall, the landing sound unless it is a Temple tile the cell test refuses), it:
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

255 stands for `StunDuration` (`creature-status-effects.md` §2), then 257 (`0x00481200`):
1. It returns 1 while an action is current (`0x00496D00`): the get-up, action 53, which 255's end started. Its length is the creature's animation, not a definition field. In the Level 1–3 captures the longest 257 run of a type that ends in a room drop is the same in every run: Warlock (5) 8 ticks, Goblin (7) 11, Troll (10) 5, Firefly (12) 13, Lord of the Land (29) 4, type 14 4 (the Dwarf, by Level 2's party and its zero `StunDuration`); a fight can cut it short (257 → 8 or 199).
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
- (Until 2026-10-03 this section said `SetState` does not write `+0x7E` and the stand could reach 62 turns; the stamp above was missed, and ours inherited the gap: ADR-0344.)

## Differences in ours

- **Kept arms.** The Combat Pit floor, the torture devices and the Prison arms stay the direct arms they were. A captive now goes through 1, where it went through 77.
- **Unbuilt drop states.** On getting up, the rooms whose drop state is not built (Treasury 188, Lair 79, Hatchery 92, Portal 197, Heart 71, Prison 114, Crypt 274) idle. The Combat Pit's 137 is built (ADR-0227).
- **Workers.** A worker's getting up idles; `0x00481400` (read, `door-blocking.md` §8) is not built, in 257 or in 76.
- **Not modelled:** the landing's sound, fall and pit scatter; 257's step along the facing; the casino-winner gate. The Call To Arms branch is built (ADR-0279).
- **257's hold** is `ActionUntilTick`, set at the stun's end to the per-type table `DropLandingRules.GetUpTicks` read off the captures; a type never captured gets a guess of 8 (Q527). DK2 plays the animation.
- **One word for `+0x7E`:** `StateDeadlineTick`, stamped with now by `AfterGettingUp` on the way into 250, as `0x004E8DA0` stamps `+0x2CC`.
