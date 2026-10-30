# Mechanic: the typed cheats, and whether DK2 remembers them

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra (pyghidra-mcp and the headless runner) on
  2026-10-30. Addresses are facts about the binary in our own words; no decompiler output
  enters this repository (ADR-0046).

Original version: DK2 v1.70 (GOG)

Questions: opens Q688. Builds on `game-actions.md` §3.1 (the entry path, first read 2026-10-05).

## 1. The answer

**DK2 keeps no record that a cheat was used.** Nothing in the cheat path writes a flag, a
counter or a statistic, and the level-end, statistics, Total Evil Rating, hi-score and
profile code read none. A cheated level ends, scores and unlocks exactly as an honest one; in
the campaign it also enters the hi-score table the same way (skirmish writes no score and no
profile at all, `004BE630`). STRONGLY INFERRED: the entry functions and the eight handlers
were read whole and every field they touch has its writers and readers listed below; of the
save code only the loader's header and call structure were read, and the save writer was not.
The owner has seen a cheat-won campaign level score like an honest win (§7). That confirms the
scoring effect, not the absence of a hidden byte in `HiScores.dat` or the registry, so the
headline stays STRONGLY INFERRED (Q688).

## 2. Entering a cheat

| Step | What happens | Where | Confidence |
|---|---|---|---|
| Open | **Ctrl+Alt+C** in a level: DirectInput key `0x2E` pressed, modifier mask bits 2 (Ctrl) and 4 (Alt) both set, Shift ignored, entry not already open, game mode `[0x0075AD4B]` not 3, and only when the key handler reaches it (§5). Sets interface `+0x3E37` = 1 and the character index `+0x3E3B` = 1, arms all eight phrase bytes at `0x006CE908`, plays sound `0x134` | [0040A600](functions/0040A600.md), [005B2A50](functions/005B2A50.md) | CONFIRMED (binary) |
| Type | Characters arrive as window message `0x102` (`WM_CHAR`). While `+0x3E37` is set the interface's message callback sends each one to the matcher instead of any text field | [00409640](functions/00409640.md) | STRONGLY INFERRED (read by disassembly; `WM_CHAR` from the message number) |
| Match | Each character, case-folded by `0x006357D0`, is compared at the running index with every still-armed phrase; a mismatch disarms that phrase. A completed phrase plays `0x133`, fires and closes entry; no armed phrase left plays `0x135` and closes entry. Spaces are part of the phrases | [0040A680](functions/0040A680.md) | CONFIRMED (binary) |
| Fire | One game action per phrase, carrying the local player's number, into the ordinary session queue (`0x004346E0`), so it travels in the packet stream like any player command | [0040A760](functions/0040A760.md) | CONFIRMED (binary) |

No on-screen text accompanies entry: the only readers of `+0x3E37`/`+0x3E3B` are the three
functions above and the HUD reset `0x004033F0` (all-instruction search, 2026-10-30).

Typed letters keep their hotkeys. `Cheat_BeginEntry` returns 0 on every path, so
`Input_HandleGameKey` goes on to the key bindings (`0x00405829`); the `WM_CHAR` copy goes to
the matcher separately. CONFIRMED (binary), and seen by the owner (§7). The remake's entry UI
departs from this: see ADR-0589.

## 3. The eight phrases

Pointer table `0x0068F550`, eight entries; the effect is the handler of the queued action
(table in `game-actions.md` §4).

| # | Phrase | Action | Handler | Effect | Confidence |
|---|---|---|---|---|---|
| 0 | show me the money | `0x43`, amount 100000 | [00505A10](functions/00505A10.md) | Pours 100000 gold into the player's Treasuries (room type 1) and Dungeon Heart (type 5) that have room, through `Room_DepositGold` ([004F4090](functions/004F4090.md)). The handler skips a ruined Heart (room `+0x9A` bit 0); the deposit itself refuses any ruined room and, in the Heart, fills only tiles on its border ring; what does not fit is lost | CONFIRMED (binary) |
| 1 | now the rain has gone | `0x4B` | [00505D70](functions/00505D70.md) | Reveals the whole map for the local player (`fog-reveal-sources.md` §3.3) | CONFIRMED (binary) |
| 2 | feel the power | `0x4E` | [00505E20](functions/00505E20.md) | Calls `Creature_LevelUp` on each of the player's creatures until it reports level 10. `Creature_LevelUp` does nothing for a creature whose health `+0x40` is at or below 0 (`Thing_IsDead`, creature vtable `+0x28`), so such a creature below level 10 on the list would make this loop spin for ever; whether one can be there when the action runs is UNKNOWN (Q688) | CONFIRMED (binary) |
| 3 | this is my church | `0x45` | [00505AD0](functions/00505AD0.md) | Every room type the world lists (world table at `+0x15B4`, count `+0x15BC`, rows of 0x486 bytes, id at `+0x44D`; world vtable `0x0066E3EC` slots `+0x9C`, `+0x90`): availability 3 in the player's `RoomInfo` | CONFIRMED (binary) |
| 4 | fit the best | `0x46` then `0x47` | [00505B30](functions/00505B30.md), [00505BB0](functions/00505BB0.md) | Every trap type (world table `+0x15E4`, count `+0x15EC`, 0xD1-byte rows; slots `+0x164`, `+0x158`, lookup `+0x160`), then every door type (`+0x15D4`, `+0x15DC`, 0xB5-byte rows; slots `+0x13C`, `+0x130`, `+0x138`): availability 3, skipping traps with descriptor `+0x88` bit `0x8000` and doors with `+0x8B` bit `0x80` (meanings unread). Which table is traps and which doors follows from the player table each result is written to | CONFIRMED (binary); skip bits UNKNOWN |
| 5 | i believe its magic | `0x48` | [00505C30](functions/00505C30.md) | Every keeper spell (world table `+0x1604`, count `+0x160C`, 0x7A-byte rows; slots `+0x114`, `+0x108`): availability 3 | CONFIRMED (binary) |
| 6 | do not fear the reaper | `0x54` | [005060E0](functions/005060E0.md) | Wins the level: `Player_EndLevelStatistics(2)`. Not queued in game mode 3 | CONFIRMED (binary) |
| 7 | ha ha thisaway ha ha thataway | `0x44`, amount 100000 | [00505AA0](functions/00505AA0.md) | `Player_AddMana(100000)`, capped at the player's maximum mana `+0x10EA` | CONFIRMED (binary) |

Availability 3 is AVAILABLE ([trigger-dispatch-tables.md](trigger-dispatch-tables.md#action-type-6-writes-availability); table layout in `player-object.md`). The four
setters ([004BAAD0](functions/004BAAD0.md), [004BAC80](functions/004BAC80.md),
[004BADE0](functions/004BADE0.md), [004BAF40](functions/004BAF40.md)) only change a row the
level already has: a type absent from the level's tables stays absent.

## 4. Nothing persistent is written

What the cheat path writes, completely: interface `+0x3E37`, `+0x3E3B` and the eight bytes at
`0x006CE908` (all transient entry state), the queued action, and then whatever the handler
changes (gold in rooms, mana, availability rows, creature levels, fog, Status). Each handler
was read whole; none writes anything else.

The consumers that would carry a mark do not test for one:

- **Level end** [004BE630](functions/004BE630.md): the cheat win goes through it unchanged. It
  writes Status, the statistics block `+0x142..+0x185`, campaign map statuses, the keeper rank
  and the Total Evil Rating with no cheat test. Mana stored (`+0x15E`, from `+0x82`) is weighted
  2 in the rating (`level-statistics.md` §3), so the mana phrase raises the rating unmarked.
- **Hi-score** [0053E610](functions/0053E610.md): inserts by rating alone and writes
  `Data\Settings\HiScores.dat` (ten 0x84-byte rows); no flag field is read or written.
- **Profile** (registry key `Player`, [0055F6D0](functions/0055F6D0.md)): its value names hold
  no cheat entry (`campaign-map-and-progression.md` §1).
- **Save** [0050ADE0](functions/0050ADE0.md): the loader's header and call structure were
  read; it reads no field named above, and none of the entry state lies in the world blocks it
  restores. Its subsystem loaders and the save writer were not read.

**`-CHEAT` is a dead switch.** The command-line parser sets `0x0075AE8F` = 1 for it
([005A63B0](functions/005A63B0.md), `0x005A6C03`), and the address occurs nowhere else in any
initialised block of the binary (byte scan, 2026-10-30). Cheat entry does not test it.
CONFIRMED (no other reference); that no indirect access exists is STRONGLY INFERRED.

`Text.str` 354 `Cheats` sits among the skirmish setup labels (`fog-of-war.md`). No widget
record carries 354 as its label dword and no instruction uses it as an immediate: like its
fog neighbours it looks unwired. INFERRED.

## 5. Modes

| Mode `[0x0075AD4B]` | Cheats |
|---|---|
| 1 campaign | allowed |
| 2 skirmish | allowed |
| 3 multiplayer | entry refused at the key (`0x0040A600`); phrase 6 also refuses itself |
| 4 My Pet Dungeon | allowed |

No lobby setting, command-line switch (`-CHEAT` included) or player flag is consulted; the
mode numbering is from `front-end-menus.md` §5. CONFIRMED (binary).

Beyond the mode, Ctrl+Alt+C is handled like any other key: `Input_HandleGameKey`
([00405010](functions/00405010.md)) reaches the cheat test at `0x00405822` only on its ordinary
path. Entry is therefore unavailable:

- after the level's result (player `Status` `+0x19A` non-zero takes the post-result arm);
- while the chat edit control is open (interface `+0x10E2`, `hud-visibility.md`), or a text
  field at `+0x10C6` is active (`0x0052C520`);
- while a key is being rebound in the options (`+0x111`, `options-menu.md`), or interface
  `+0xFD` is set (meaning unread; it also stops the message callback `0x00409640`);
- in view mode 7, the full-screen map (presentation slot `+0xCC` = 7 at `0x00405453` sends
  the key to the map-mode arm, binding `0x29`, which never reaches the cheat call);
- while the camera refuses ordinary input. `Camera_AllowsOrdinaryInput` (`0x0044D590`)
  allows it only when the letterbox `+0xE48` is 0 or the camera mode is 2, 10 or 11, the timed
  camera move `+0xDD8` is 0, and the mode is neither 11 nor 18 (18 is a scripted path).

CONFIRMED (binary), from the branches at `0x00405087`, `0x00405453`, `0x004054FC`,
`0x0040576A`, `0x004057AC`, `0x004057B8` and `0x004057D1`.

## 6. Integrity checks elsewhere

None marks a game. What exists:

- **Saves** (`0x0050ADE0`): an 8-byte header whose version dword must be 1, then a 0x40-byte
  string compared with the executable's build stamp (from `"May 12 2000"` and `"12:28:22"`,
  pointers `0x006BC438`/`0x006BC43C`); on a mismatch the loader returns failure without
  restoring the world. No checksum over the contents was seen at the top level. STRONGLY
  INFERRED (header and that branch read, not run; subsystem loaders unread).
- **Executable checksum, for multiplayer**: at start-up `App_Main`
  ([005A5DA0](functions/005A5DA0.md)) reads the file named by `argv[0]` (presumably the
  executable) in 0x2000-byte chunks and folds every dword into a rotate-left-1-then-XOR sum,
  seeded `0x5041554C`, stored at `0x007962A0` (the seed if the file cannot be opened, 0 on a
  read error). The host publishes
  it in the session description ([00543840](functions/00543840.md)); the session list
  ([0054D370](functions/0054D370.md)) marks a session joinable only when its value equals
  ours, the version words `0x006BC430`/`0x006BC434` match and it has room. Players with
  different executables cannot join each other. CONFIRMED (binary) for the arithmetic and the
  comparison; which file is read is STRONGLY INFERRED.
- **Multiplayer sync**: per-turn world checksums and seed compared between machines,
  `Gone Out Of Sync (Checksum)` (`navigation-captures.md`); `-NOCHECKSUM` (`0x0075B308`)
  turns the comparison off.
- **Packet recordings** (`-PSAVE`): the header writer [0055EAB0](functions/0055EAB0.md) stores
  the level name and either the dword at `0x0079629C` or 0, chosen by `0x0075AE57`. That flag
  lies in uninitialised memory and has three references: `-NOFILECHECKSUM` sets it to 0
  (`0x005A698F`), the playback loader `0x0055E7D0` sets it to 1 (`0x0055E99B`, after writing
  `0x0079629C` at `0x0055E995`), and the header writer reads it. No writer on an ordinary run
  was found (pointer and block writes not scanned), so an ordinary `-PSAVE` header most likely
  holds 0; unresolved. The dword is not a content checksum: `App_Main` sets it at `0x005A5E8C`
  to the sum of two fields of the same file's information record (`0x005B3CE0`), metadata such
  as size and date. That a recording holds the cheat actions like any others is
  STRONGLY INFERRED (they go through the ordinary queue; the per-turn record body was not read).
- `HiScores.dat` is written as the raw 0x528-byte table, with no checksum.

## 7. Owner observations (2026-10-30)

Recollection and play experience, not binary evidence or a controlled test:

- **Feel the power sometimes freezes the game** ("I think it does"). This corroborates the
  possible non-ending loop in §3 row 2; the binary finding keeps its own confidence.
- **Typed cheat letters also fire their hotkeys** (seen). Matches §2.
- **A cheat-won campaign level scores like an honest win**: rank, Total Evil Rating and
  hi-score entry (seen). Supports §4 for the scoring; it cannot show whether a hidden byte is
  stored.
- **Whether fit the best leaves some traps or doors locked**: not known. The skip bits in §3
  row 4 stay UNKNOWN.
