# play-record.ps1: record the owner's play, decode it, capture its state

`tools/oracle/play-record.ps1` records a level the owner plays in the original game with DK2's own packet recorder,
keeps the recording as add-only evidence, decodes it into tables, and replays it afterwards to capture game state.
Read this before running it or changing it; the "Traps" section lists every way an earlier session went wrong.

## Modes

All modes except `-Decode` need an **elevated** PowerShell (DK2 runs elevated; the owner clicks the UAC prompts) and
no DK2 process running. Run from a checkout: `powershell -ExecutionPolicy Bypass -File tools\oracle\play-record.ps1 ...`

| Mode | What it does | Output |
|---|---|---|
| (none): play | Starts `DKII-DX.exe -LEVEL <Level> -PSAVE <Name>` at full speed, nothing attached; waits for the game to close; copies the recording into a new study and decodes it | `F:\DK2Evidence\studies\<Name>--<date>--001\`: `<Name>.sav`, `commands.tsv`, `camera.tsv`, `MESSAGE.LOG`, `screenshots\`, `manifest.json`, `sha256.txt` (all read-only) |
| `-Decode <sav> -Out <new dir>` | Decodes any packet recording; no game, no elevation | `commands.tsv`, `camera.tsv` |
| `-Replay -Study <study> -Map WxH` | Starts `reverse-engineering\dk2live.py record` (polls memory from outside, never stops the game), then `DKII-DX.exe -LEVEL <level> -PLOAD <name> 0`; records to the recording's last record (playback takes one per turn); closes the game it started and any DK2 left; checks the log for desync. The capture is the **legacy** format (selected regions only, object slots 1150-1719; ticks can be missed or torn, which `replay-summary.json` reports from the recorder's closing JSON object) | `<study>--replay-<stamp>\`: `level.dk2cap`, `commands.tsv`, `camera.tsv`, `recorder.*.txt`, `MESSAGE.replay.LOG`, `replay-summary.json` |
| `-Verify -Study <study> [-Minutes 45]` | The control: `DKII-DX.exe -LEVEL <level> -PLOAD <name> 0` with nothing attached; reads only `MESSAGE.LOG` (shared, every 5 s); closes the game at the first complete `Gone Out Of Sync` line, when the game closes, or after `-Minutes` (set it above the recording's length). It cannot tell how far the replay got: only a run that reaches the time limit with the game still running and no desync line counts as clean; an early close is reported as not evidence. While it runs, any DK2 process is treated as the replay and killed at the end | `<study>--verify-<stamp>\`: `MESSAGE.verify.LOG`, `verify-summary.json` (`stopped`, `gameExitedItself`, `engineTimeBlock`, `desyncTurn`, `desyncKind`, `elapsedSeconds`) |
| `-Capture -Study <study> [-Turns ...]` | `record_native.py` full-memory captures at chosen record ordinals, one replay per turn, after a 3-turn smoke check | `<study>--capture-t<turn>\` each, `<study>--captures-<stamp>.json`. **Desynced on Level 6a** (Traps) |

Play options: `-Level` (default `Level6a`), `-Name` (default `l<level>-owner-<yyyyMMdd-HHmm>`, `l6a-...` for Level6a;
letters, digits, `-`, `_`, at most 40), `-GameDir` (`E:\GogGames\Dungeon Keeper 2`), `-EvidenceRoot` (`F:\DK2Evidence\studies`). Replay and capture
also take `-Python` (`python`); replay takes `-Recorder` (`D:\DungeonKeeperRemake\reverse-engineering\dk2live.py`, run as `dk2live.py record`) and `-Map WxH` (required: the
level's size, 70x70 for Level6a; with `--map 0` the recorder could take the front end's own small world for the level).
`dk2live.py record`'s own `--level` is its zlib level, not the game level; the script does not pass it.

Status, 2026-11-16: play and `-Decode` used for real (Level 6a, 7243 records, 1084 commands; the decoder agrees with
`record_native.packet_info` on `l5live.sav`, 6415 and 1079, and on Level 6a; `test_play_record.py`). The kept Level 6a
study (`l6a-owner-20261115-1821--2026-11-15--001`) was decoded by an earlier version with raw columns only
(`turn seed action start_x start_y end_x end_y arg word`); `-Decode <sav> -Out <new dir>` gives today's columns. `-Capture` ran and
desynced (Traps). `-Replay` ran once (Level 6a, 2026-11-16, `...--replay-20261116-1816`): 7238 frames to tick 7240,
none torn, 2 missed (1527, 1529), 31 min; the game logged `Gone Out Of Sync (Checksum) GT 631` (Traps). Ticks 1-630
match the owner's game by the game's own check; after 631 it is not known whether the game diverged (Traps). Its summary
showed `recorderExit: null`, a script defect now fixed. `-Verify` is new and not yet run.

## commands.tsv

One row per command: `ordinal` (the record's position in the file, from 1), `turn` (the record's stored stamp),
`seed` (that record's RNG seed, hex), `action`, `name`, the decoded fields `x y x2 y2 type thing wx wy` (empty where
the action has none), and the raw `d1 d2 d3 word`.

**Stamp, ordinal and playback turn differ.** The stored stamp leads DK2's playback clock by one
(`docs/research/packet-playback-clock.md`), and it repeats while the game window is unfocused: in the Level 6a
recording stamp 964 fills ordinals 963-966 around two window-focus (92) commands, so after it ordinal = stamp + 2.
`record_native.py --first/--last` count ordinals; join on `ordinal`, never on `turn`.

| action | name | decoded fields |
|---|---|---|
| 40, 41, 49 | tag, untag, sell | rectangle `x y` to `x2 y2` |
| 47 | build-room | `type` room id, rectangle `x y` to `x2 y2` |
| 50, 52 | place-door, place-trap | tile `x y`, `type` door or trap id |
| 54 | cast-at-place | `type` spell id, world point `wx wy` (tiles) |
| 55 | cast-on-creature | `type` spell id, `thing` |
| 57, 59, 62 | pick-up-creature, pick-up-gold, slap | `thing` (a slap's `d2` is kept raw: 1120 in both Level 6a slaps, unread) |
| 93 | door-lock-toggle | `thing`, the door's slot (`game-actions.md` section 2.1) |
| 61 | drop | world point `wx wy` |
| 114 | panel-click | `type` = category/id (1 rooms, 3 doors, 4 traps, 5 spells, 0 HUD) |
| 21, 25, 26 | leave-possession, possessed-move, possessed-act | raw only |
| 1, 92 | end, window-focus | raw only |

The table is not exhaustive: 63, 75, 88 (activate special), 134, 135 and others decode as `action-<id>` with raw fields.
World points (`wx wy`) are in tiles from 1/4096-tile units: inferred, not documented (all 271 Level 6a drops lie within
5 tiles of the camera, all 13 casts within 2.4).

`thing` is the original game's thing slot, not a type, and slots are reused: read it only against state at that
record (a `-Replay` capture, joined by ordinal).
`camera.tsv` is the camera centre per turn in tiles: where the player was looking.

Ids seen (owner-confirmed where marked): rooms by `RoomTypeId` (1 Treasury, 8 Wooden Bridge, 11 Prison, ...); spells 2
Possession, 4 Create Imp, 6 Lightning Strike (owner; Thunderbolt in the repo), 10 Heal, 14 Horny talisman (owner); trap 3 Sentry, 8 Fear (by the
`traps.md` section 12 table order; the owner placed Sentry and Fear); door 6 Barricade (owner).

## The packet

A 64-byte level name (its tail is not zeroed: buffer residue can follow the terminator), a dword (0x82D586F6 in every
recording on F: and E:, Levels 1-17; `cheats.md` section 6 reads it as the sum of two fields of the file's information
record, or 0), then one record per game turn: count (1 byte), stamp (4), seed (4), checksum
(4), header (4), count x 18-byte commands (d1, d2, d3, action id, a word), a 12-byte camera (x, y in 1/4096 tile, then 4
bytes). DK2 compares each turn's stored seed and checksum on playback and logs `Gone Out Of Sync (Seed|Checksum) GT n`
in `MESSAGE.LOG` on a mismatch. Layout: `docs/research/packet-playback-clock.md` (reader 0x0055EA10) and
`record_native.py` `packet_info`; `navigation-captures.md`'s older Q356 table (a 4-byte counter, a 13-byte camera) is
superseded. A truncated last record (the game closed mid-write) is dropped with a warning here; `packet_info` refuses
the file.

## Traps

- **`-Capture` desynced a long replay; the cause is not known.** On Level 6a a `record_native.py` replay logged
  `Gone Out Of Sync (Checksum) GT 76` (2026-11-16, `...--capture-t140`), 63 turns before the owner's first command
  (stamp 140). A plain `-PLOAD` replay of the same file ran 427 turns with no such line
  (`...--plain-pload-0606\MESSAGE.LOG`). The seed is not it (`seed.json`: old = written = 1001387456, the packet's
  first seed). HYPOTHESIS: what `record_native.py` adds. Its only stops before GT 76 were 6.0 s at tick 1 and 2.6 s at
  tick 2; it also sets a hardware breakpoint hit every turn, `__COMPAT_LAYER`, and a hidden window. To settle it: a
  run with the tick-1/2 stops only, and one with breakpoints but no pause. Until then prefer `-Replay` for long replays.
- **`-Replay` logged a checksum mismatch too, later: GT 631.** The 2026-11-16 Level 6a `-Replay` (`dk2live.py record`
  reads memory from outside and never stops the game) logged `Gone Out Of Sync (Checksum) GT 631 [482DC5CE:482D9AA2]`,
  then `Out Of Sync(on GT 631) GT n` for every n from 632 to 7243. The nearest commands: a drop at ordinal 624 (stamp
  625) and a creature pick-up at ordinal 636 (stamp 637), none in ordinals 625-635. The plain replay that ran in sync
  stopped at 427, so it does not say whether a replay with nothing attached also logs 631.
  - What the mismatch means is not settled. A checksum-only warning (seed matching) at GT 6 on the Level 2 recordings
    did not stop the replay following the session to the same win (`docs/VERIFICATION_SWEEP_PLAN.md`); this run did not
    report the seed. HYPOTHESIS: the ticks after 631 are not the owner's game. Confirm before reading the owner's
    decisions from them (for example, does a creature the owner picked up after 631 exist at that spot in the capture).
  - HYPOTHESIS for the cause: the window lost focus during the replay (the owner saw it). A focus change is game action
    92, `GameAction_SetSessionPaused` (0x005137E0, STRONGLY INFERRED), which pauses or resumes the game clock; the owner's
    own recording carries two (ordinals 962, 965). The owner's live right click ended a possession during this replay,
    so the game takes live input while it replays. Not checked: whether a live focus change in playback reaches the
    simulation, and when this run lost focus.
  - Next: `-Verify` on the same study without touching the game. One run is one sample: if it passes 631 in sync, the
    focus change or the outside reads remain the suspects; if it also logs 631, the game or the recording is the cause.
- **Do not touch the game during any replay.** No clicks, no keys, no alt-tab, nothing on top of its window: the game
  takes live input while it replays (above), and the replay is evidence only if the recording alone drove it.
- **`ExitCode` from `Start-Process -PassThru` is empty in Windows PowerShell 5.1** unless the script reads the
  process's `Handle` while it runs. The first `-Replay` summary shows `recorderExit: null` for a recorder that ended
  cleanly; the script now reads `Handle` at start.
- **The hand is last-in, first-out** (`hand-of-evil.md`). Pair each `drop` with the most recent unmatched pick-up,
  creature (57) **or gold (59)**, not the oldest; the owner held up to nine at once. Tolerate a drop with an empty hand:
  in Level 6a, ordinals 2887 and 2888 each carry the same drop twice, and one of those drops finds the hand empty.
- **Each action puts its arguments in different dwords** (a room's type is d1, a trap's type d2, a drop's position is
  d1/d2 in 1/4096 tile). Use the decoded columns; the raw `d1 d2 d3` exist for actions not yet named.
- **The owner could not quit a replay cleanly**; `-Replay` stops the game it started (the 2026-11-16 plain replay's
  log still has its exit statistics, so a forced close keeps the log). `MESSAGE.LOG` is rewritten
  at each launch: copy it before the next start (the script does).
- **`-LEVEL` on both `-PSAVE` and `-PLOAD`**, or the front end eats the seed and the replay desyncs on turn 1. Launch
  `DKII-DX.exe`, not `DKII.EXE` (its `-LEVEL` exits before the level loads). Both from `docs/TRAPS.md`.
- **Never `python -I` for `dk2live.py`**: isolation drops the script's directory from `sys.path`, so its
  `import dk2capture` fails. `dk2live.py` and `dk2capture.py` live in the gitignored `reverse-engineering\` folder of
  the main checkout, not in worktrees.
- **Numbers in PowerShell follow the Windows locale** (a comma decimal on this machine); the decoder formats with the
  invariant culture. Keep it that way in any new column.
- **Python strings that build Windows paths** turn `\r`, `\t` into control characters; write paths in raw strings.
- **Evidence is add-only**: every output directory must be new; the script refuses to overwrite a recording or study.
- **Turn rate**: do not convert turns to minutes from a guess; DK2's turn rate is not established here.

## Tests

`python -m unittest discover -s tools/oracle -p test_play_record.py -v`: a synthetic packet with one command of each
decoded layout, a truncated last record (dropped, not misread), the desync-line reader on a log modelled on Level 6a's
(and on a line still being written), and
`-Verify` refusing a missing study. Needs Windows PowerShell.
