# The Casino's states: 145–153, the jackpot and its winner (152, 155–157)

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-10-06.
- **Built:** ADR-0396, `CasinoStep` and `CasinoJackpotRules`.
- The room, its lever, the enter, update and leave hooks and the settlement:
  `state-category-hooks.md` §3.1 and `leave-hook-bodies.md` §1. Job 9 (Drink): `job-selection.md`.
- State `+0xAC`, `+0xB1`, `+0xB4`/`+0xB8` below are offsets in the creature's state object
  (creature `+0x24E`); `+0xB1` is creature `+0x2FF`. `Age` is state `+0x1C`, 1 on a state's
  first handler call. `v19` is variable 19, four turns a second.

## 1. How a gambler moves — CONFIRMED

States 145–151 and 153 are category 17, so the update hook `0x0047AD70` runs in all of them
and a walk between them (state 6, resuming one of them) keeps it running. 152 and 155–157 are
category 0. Every arm below that "returns 0" drops the creature to state 1.

| state | handler | does | next |
|---|---|---|---|
| 145 | `0x00456160` | chooses: the party, a table, a barrel alcove, or a bare floor tile | walk to 153, 148, 146 or 150 |
| 146 | `0x00456B70` | faces the alcove, maybe gets drunk, drinks | 147 |
| 147 | `0x00456D60` | waits out the drink | 145 |
| 148 | `0x004567D0` | faces the table, plays | 149 |
| 149 | `0x00456980` | waits out the play | 145 |
| 150 | `0x00456450` | action 68 once | 151 |
| 151 | `0x00456490` | waits it out | 145 |
| 153 | `0x00456E20` | dances | 145 |

Entries (`SetStateLits` over `0x00476D30` and `0x0048AD30`, and `PreCall` over the route
helpers `0x00471890` and `0x00471A70`): 145 from job 9's walk (`0x0045613D`), from 147, 149,
151, 153, 154's handler `0x00456FB0` and the room update (`0x004D64C3`, `0x004D64DC`,
`0x004D6658`); 146 only from `0x00456690`'s walk; 147 only from 146; 148 only from
`0x00456500`'s walk; 149 only from 148; 150 only from 145's walk (`0x00456426`); 151 only from
150; 153 from the jackpot (`0x004D6B45`) and 145's walk (`0x004561BF`). The room drop
(`Room_DropCreature`) also enters 145, as the Casino's room-record state.

## 2. State 145, the chooser `0x00456160` — CONFIRMED

1. At `Age` 1 the creature stops (`0x0048ECE0`).
2. Not on a tile of its own keeper's Casino (`0x004539B0(15, owner, 0)`): return 0.
3. The room under it (`0x004559B0`, the tile's room word). With a party on (room `+0x41`):
   a walk to its own position resuming 153 (`0x00471890(pos, 0x99, 0x80)`); taken, return 1.
4. Without a lever (room `+0x53` zero): return 0. Otherwise `+0xAC` = 1 (face on arrival) and
   `+0xB1` = 0.
5. Purse `+0x3FB` at most `abs(variable 200)` (8), or no held room (`+0x3B9` zero): return 0.
6. `random(2)` on the simulation stream (line `0x5A`). Nonzero: the nearest free table
   (§3, `0x004D72D0`) and its walk `0x00456500`; none, the nearest free alcove (`0x004D7460`) and
   its walk `0x00456690`. Zero: the alcove first, then the table. Whichever finds an object
   returns 1, whether or not its walk was taken.
7. Neither: a free floor tile (§3, `0x004E4EA0`) and a walk resuming 150; taken, return 1.
   Otherwise return 0.

Ghidra's decompiler marks the object arms of step 6 unreachable: it loses the finders' out
parameter. The disassembly (`0x00456249`–`0x004563F5`) is the reading.

## 3. The finders and where a gambler stands — CONFIRMED

The Casino's room record lists objects 87, 59, 67 and 68 in its first four slots
(`terrain-room-dump.md`): *Roulette Table*, *Barrel*, *Bar Table* and *Alcove Barrels*. Slots
0–2 are floor furniture and slot 3 wall furniture (`auto-placed-furniture.md`).

- **`0x004D72D0`, a table.** Over the room's objects (`room+0xE`, next `+0x12A`), each whose type
  is one of definition slots 0–2 (`def+0x431`), the nearest by `(dx² >> 12) + (dy² >> 12)` from
  the creature that no occupant (`room+0xA`, next `+0x238`) other than the seeker names in its
  `+0x2FF`. A second pass, run only when the first finds nothing, also counts the seeker; 145
  has just cleared it, so the pass adds nothing.
- **`0x004D7460`, an alcove.** The same over slots 3–5, the second pass always run.
- **`0x00456500`, the walk to a table.** On the found object's tile (`0x004537E0`), the first
  object of type slot 0, 1 or 2; none, `SetState(145)`. `+0xB1` = its id. The stand point is the
  object's position plus `r` at angle `a`, where `r = (objdef Width/2 + creature footprint/2) ×
  110 / 100` (`objdef+0xF8`, `def+0x63A`, signed division) and `a` is `(random(2) + 1) << 10` for
  a Roulette Table (87; `0` or `1024`, either side) and `random(0x800)` otherwise; the offset is
  `(cos·r) >> 16`, `(sin·r) >> 16` from `0x006C1C88` and `0x006C1488`. The walk resumes 148 with
  options `0x80`. It returns 1 either way. 145's second argument (the object's angle `>> 9`) is
  never read.
- **`0x00456690`, the walk to an alcove.** The first object of type 68 on the found object's tile;
  none, `SetState(145)`. `+0xB1` = its id; `r` as above; `a = (random(0x200) + ((sbyte)((angle
  >> 2) & 0xFF) << 9) + 0x100) & 0x7FF`. Room_Rebuild's wall pass gives every wall object the
  angle `((side + 2) & 3) << 9` (`0x004EE9CE`), whose bits 2–3 are clear, so for an alcove
  `a = random(0x200) + 0x100`: 45° to 135°, whichever wall it is on. The walk resumes 146.
- **Wall furniture sits off-centre.** The wall pass moves a type-68 object `0x700` toward its
  wall (jump arm `0x004EEA07`, sides north, east, south, west at `0x004EF170`) through
  `SetPosition` (`0x004B5560`). Floor furniture keeps its tile centre (`0x004EE13F`).
- **`0x004E4EA0`, a floor tile.** From the room's first slab, `random(+0xA7)` (the tile count)
  steps along the slab chain, then on along it, at most count + 1 tiles, to the first that is not
  a floor slot (`0x00454110`), whose nav-cache byte (`0x00455000`) is 0, and that holds no
  object with the Obstacle flag (`0x00453600`, `def+0xF4` bit `0x4000`). Its centre. The nav
  byte is what `Tile_SetTerrain` (`0x0044FC40`) writes: 8 for a Solid terrain, 1 for flag `0x80`,
  3 for `0x100`, else 0; the Casino's terrain (24, flags `0x30000034`) has none of them, so it is
  0 on every Casino tile.

## 4. Drinking: 146 and 147 — CONFIRMED

**146.** With `+0xAC` set: the object in `+0xB1` must still be a live object (`0x006ED540`,
kind 2), else `SetState(145)` and return 1; the creature turns toward it (`0x004B2AE0`) and
returns 1 until it faces it, then clears `+0xAC`. Then `random(100)` (line `0x236`):

- **Below 75.** `random(100)` again (`0x23B`): below 25, and art 34 resolves to a present
  record (`0x00494860`), the drunk timer `+0x4CE` = `v19 × 45` turns, and the room's occupants
  with a nonzero timer are counted, this one included: more than 10 posts message 89 with data
  11, more than 2 message 75. Then, if art 19 resolves present, action 39, count 3.
- **75 or more.** If art 37 resolves present, action 56, count 3.

Either way `SetState(147)` follows the action's start without testing it. Art not present:
return 0. Neither shipped drunk variable is read: `DrunkChancePercentagePerDrink` (163) and
`DrunkenDurationSeconds` (156) have no reader (`balance-variables-in-the-binary.md`); the 25 and
the 45 are literals.

**The drunk timer** is counted down by `Creature_Update` (`0x0048AA22`), set by the constructor
and the state above, and read by the walk animation selector `0x0048BE50` (`0x0048C311`), which
chooses the drunk walk art: presentation. Its one simulation reader is 146's count.

**147.** After `Age` 1, a creature that moved since the last turn (`+0xB4`/`+0xB8` against its
position) ends its action (`0x00496D20`). It stores its position. While the action runs it holds
(every `v19 × 8` turns a sound); after, `SetState(145)`.

## 5. Gambling at a table: 148 and 149 — CONFIRMED

**148.** The same facing step as 146. Then `A` = art 14 present (`0x00494A00`, no fallback),
`B` = art 8 present on the Smiley setting or art 9 on the $ setting (`0x004D6880` on the held
room), `D` = art 20 present. The action (`0x004567D0`–`0x00456920`):

| A | B | D | action |
|---|---|---|---|
| yes | yes | yes | `random(100)` (`0x171`): below 50 → 65; below 75 → 66 (Smiley) or 67 ($); else 68 |
| no | yes | yes | `random(100)` (`0x189`): below 75 → 66 or 67; else 68 |
| any | no | yes | 68 |
| any | yes or no | no | 0 |

Action 0 is "no action": it starts and ends at once. Count 3; started, `SetState(149)`, else
return 0.

**149.** As 147, without the sound; and every `v19 × 4` turns while the action runs, if `+0xB1`
is a live Roulette Table, `random(2)` picks one of the Roulette Wheels (object 30) on its tile to
spin (`0x0049EF60(0x12)`) with a sound: presentation, but the draw is on the simulation stream.

## 6. A bare tile: 150 and 151 — CONFIRMED

**150** (`0x00456450`, not a function in the canonical project): action 68, count 1; started,
`SetState(151)`, else return 0. **151** as 147 without the sound.

## 7. The actions — CONFIRMED (table), LIKELY (duration)

`0x004950C0` fills each record (`0x006E4198 + id × 0x1D`): actions 35, 56, 39, 57 and 65–68 have
no handler, mode 0 (EAX, 0 at their stores) and no fixed duration, so each ends with its
animation. Their art (`+0x14`): 35 → 8 (`0x00495AFF`), 39 → 19 (`0x00495BB3`), 56 → 37
(`0x00496090`), 57 → 28 (`0x004960BC`), 65 → 14, 66 → 8, 67 → 9, 68 → 20 (`0x004962AD`–`0x0049635E`). The resolver `0x00494880` maps a missing slot 8 to 0,
14 to 5, 19 to 9, 20 to 1, 28 to 8, 34 to 1 and 37 to 1. `0x004969D0(action, n)` stores `n + 1`
as the cycle count and, for `n` below 2, clears looping: so count 1 plays once and count `n ≥ 2`
plays `n + 1` times, as the prison's flourish does (ADR-0301). The length of a play is the
resolved art's frames and rate, as `AnimationTimingRules` reads them; that the runner ends a
looped action after exactly its cycles is LIKELY (the cycle flags are the renderer's).

**In the shipped data** (`CasinoOnRealDataTests`): of the 39 creatures whose job tables list Drink,
King Reginald and the Stone Knight have no art 19 or 9 and no 28 or 8, so 146 turns them idle three
times in four and 153 at once; every drinker has 37, 8, 20 and 34; 28 lack art 14, so action 65 is
open to eleven.

## 8. The jackpot `0x004D6AA0` — CONFIRMED

The update hook rolls `random(5000)` on Smiley, `random(20000)` on $ (variables 205, 206), once
per act, after the purse; a 42 calls the jackpot on the keeper with the gambler. It does nothing
if any of the keeper's creatures has a winner's deadline (`+0x3F2`). Otherwise:

1. Every other creature of the keeper whose held room (`+0x3B9`) is the winner's:
   `KickToState(1)`, then `KickToState(153)`. 153's enter hook applies the purse test, as the
   party count is not raised yet.
2. Player `+0xF4A` += 1; room `+0x41` = 1 and `+0x45` = now; the room's tiles are flashed
   (`0x004F0D70`, presentation).
3. The winner's deadline `+0x3F2` = variable 207 × `v19` turns (960), its winnings `+0x3F4` =
   the larger of variable 209 (1000) and `0x004BE290`, the keeper's wage bill (each of its
   creatures with no `+0x3E2`, its level-scaled `Pay`), and `+0x3F6` = 0.
4. Message 73, data the winner's id. The winner: `KickToState(1)`, `KickToState(152)`. A sound,
   an effect and a camera event follow: presentation.

`state-category-hooks.md` §3.1 said the others got "complaints 1 and `0x99`": those are the two
kicks.

## 9. The party — CONFIRMED

**The room update** (`0x004D6270`): with `+0x41` set at the turn's start it never reaches the
settlement (`0x004D676F` lies on the party-off path; the party block exits at `0x004D6862`), so a
party pays and charges nothing at the lever for its 90 seconds, nor on the turn it ends. In it, a party older than
`v19 × 90` turns ends (`+0x41`, `+0x45` = 0, owner `+0xF4A` −= 1), and so does a party in a room
with no occupants; both in one turn decrement the count twice. Then for each occupant in 153 it
sets `+0x3D` and plays the music; if the music handle is null every occupant is kicked to 145
(through 1 when refused). Every `v19 × 12` turns (`turn % (v19 × 12) == 0`, DK2's turn being our tick + 1, ADR-0397) an effect on a random tile: the effect is
presentation, but its `random(+0xA7)` (`0x004D6561`) is on the simulation stream. With
`+0x41` clear and `+0x3D` set, every occupant in 153 ends its action and is kicked to 145, and
`+0x3D` clears.

**153** (`0x00456E20`). At `Age` 1: any action ends; art 28 resolved present, action 57, count 64;
else art 8, action 35, count 64; else return 0, as when the start fails. Later, with no action
running: `SetState(1)`, `SetState(145)`.

**The party lifts the purse.** With the keeper's `+0xF4A` nonzero, job 9's gate (`0x004560F0`)
and the enter hook skip the purse test, and the Casino finder `0x004D75F0` takes its party
branch: over the keeper's rooms, the **last** Casino in list order with a party on, that the
creature can reach (`0x00471740(room, 0x80)`) and that has a place or a lever; no distance
ranking. A gambler at or below the stake can join a party, but the update hook's own purse test,
which the party does not lift, sends it idle on its first act (`state-category-hooks.md` §3.1).

## 10. The winner: 152, 155, 156, 157 — CONFIRMED

**152** (`0x00456EC0`, category 0). At `Age` 1: any action ends; art 28 resolved present, action
57 at its position (`0x00496980`, which writes no count: one play is LIKELY, §7); else art 8,
action 35 with count `random(4) + 3`;
else return 0. Each turn with no action running, action 35 again. At `Age` `v19 × 10`:
`KickToState(1)`, `KickToState(155)`.

**155** (`0x00457010`). Over the keeper's rooms: a Treasury, or a Heart whose `+0x9A` bit 0 is
clear, holding more gold (`+0x49`) than the winnings, whose first slab's centre the creature can
reach (`Route_IsPointReachable`, `0x80`), nearest by squared distance to that centre. None, the
same without the gold test. None at all: return 1, to try again next turn. Then a random tile of
the chosen room (`0x004E4D90(pt, 1, 0)` on it, `0x004572C3`: `random(+0xA7)` steps from the first slab) and on round the
chain to the first holding a treasury pile (object 3, any owner's: the loop at `0x00457348` tests
kind and type only), on a Heart only a tile of
the 5×5 border ring (`0x004DA1B0`); none, that tile. Its corner plus one of eight offsets
`random(8)` from `0x00698C20` (thirds of a tile, `0x2AA`/`0x7FF`/`0xD54`, the centre omitted), a
walk resuming 156 with options `0xE8`. Refused: message 77, `SetState(1)`, `SetState(87)`.

**156** (`0x004574A0`). Not on its keeper's Treasury or Heart: return 1, and it stays. Else it
turns toward `(+0x1A × 0x1000 + 0x800, +0x1E × 0x1000 + 0x800)`, a position scaled twice and
wrapped to 32 bits, and when the turn is done `SetState(1)`, `SetState(157)`.

**157** (`0x00457560`). Keeper's gold (`player+0x7E`) below the winnings: message 77. Not on its
keeper's Treasury or Heart: return 0. Else `min(winnings, room gold)` is taken from the room from
its tile round (`0x004F4510`) and given to its purse (`0x0048AE70`, capped). If the amount asked
equalled the winnings, the deadline clears and it returns 0; otherwise the winnings drop by the
amount asked and `SetState(1)`, `SetState(155)`. Nothing taken: 1, then 155.

**The deadline.** `Creature_Update` counts `+0x3F2` down (`0x0048AA07`), first storing now + 4
in creature `+0x38` and `+0x3C` each turn it is nonzero (`0x0048A9F3`; their readers are unread,
so ours does not keep them); reaching 0 calls
`0x004D6A40`: every creature of the keeper gets variable 208 (100) on Treatment with complaint
9, and the keeper message 91.

## 11. Messages — CONFIRMED

From the 10-byte rows at `0x006A0B00`: 73, 75, 77 and 91 are `0x2032` (50 s, tab, no duplicates);
89 has a variant list at `0x006A09D8` whose variant 11 lasts 20 s.

## 12. Ours

- `CasinoStep` runs 145–151 and 153, `CasinoJackpotRules` the jackpot, the party update and
  152, 155–157. The party flag and its turn sit on the Casino's lowest tile, as other room state
  does here (`TileState.PitFightSeenTick`), and move with the room as DK2's record does
  (ADR-0415): a growing Casino keeps its party (`Room_AddTile`); a merge keeps the joined room's
  and drops the absorbed one's (`Room_Absorb` writes no room field); a lost tile ends it in every
  piece, since `Room_Construct` zeroes `+0x3D`/`+0x41`/`+0x45` and `Room_RemoveTile` restores only
  `+0x53`. Neither case lowers the keeper's count: its only decrements are the room update's two
  (`0x004D6270`), and `Casino_OnFreed` (`0x004D7100`) does not touch it. So the count stays raised and the purse test stays lifted for the rest of the level, as in DK2.
- Treasury gold is `TileState.GoldHeld` (ADR-0015): "a tile holding object 3" is a tile with gold.
- Not modelled, all presentation: sounds, the music and its null-handle kick (ours takes the
  music as started), the tile flash and
  effects, the wheel's spin (its `random(2)` is drawn), the drunk walk, 156's turn result beyond
  waiting for it, and `+0x3F6`.
- 154, the eviction a Casino's sale, loss or neighbour's change starts (`0x004D70D0`, `0x004D7100`,
  `0x004E44E0`), is built (ADR-0402, ADR-0406); 158 is not (Q571).
</content>
</invoke>
