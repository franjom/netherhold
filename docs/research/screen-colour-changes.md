---
systems: [camera-lighting, creatures, presentation-unity]
---
# The whole-screen colour changes around possession (Q745)

DKII.EXE v1.70, read 2026-11-15 (pyghidra-mcp and the headless runner; disassembly of
`0x00597400`-`0x00598040` whole). Question: DK2 recolours the whole picture at some camera
events around possession. What is the mechanism, what does each change do, how is it timed,
and which creature gets which? Context: [possession-client.md](possession-client.md) §6.
Everything below is STRONGLY INFERRED (read, not run) unless it says otherwise.

## 1. Verdict

The effect is the display's **gamma ramp**, not a draw. A small object at presentation
`+0x13D3` holds three 256-entry 16-bit tables, one per channel. Each frame, while a change is
running, it blends them and hands them to `IDirectDrawGammaControl::SetGammaRamp(0, ramp)`.
So a change is a **per-channel lookup**: output = `table[c][input] / 65535` for an 8-bit input
channel c. HUD coverage is HYPOTHESIS: the API scope suggests the complete primary image,
including the HUD; that coverage was not established by a renderer read.
The identity state is kind 0 with the user's gamma. "Kind" is a word
0-15 that picks one of 16 table builders.

CONFIRMED owner play, 2026-11-18: possessed Vampire and Skeleton views have no
screen tint and taking a hit produces no red flash, both live and in the native files.
The contrary 2026-11-15 answer was mistaken. Registered native evidence:
`F:\DK2Evidence\captures\visual\possession-vampire--2026-11-18--002` (10 files) and
`F:\DK2Evidence\captures\visual\possession-skeleton--2026-11-18--002` (6 files).
The corresponding `--001` datasets mix creatures and must not be used for comparison.
The read application guard is consistent with these observations: output is off by
default. The ramps are retained behind a developer-only switch, with no player option.

## 2. How the tables reach the screen

- **Object** (presentation `+0x13D3`; constructors `0x00597400` from `0x0043A740`'s
  construction and `0x00597450` from `0x0043AD70`). Fields: kind word `+0`, start ms `+2`,
  gamma float `+6`, running flag `+0xA`, length ms `+0xE`, current tables `+0x12`, start
  tables `+0x612`, target tables `+0xC12` (red, green, blue, 0x200 bytes each, in that
  order), queue read index `+0x1212`, count `+0x1216`, eight 6-byte entries `+0x121A`
  (word kind, dword length).
- **Apply** `0x00574200(tables)`: copies the 0x600 bytes to the global ramp `0x00764BE8`
  and, when the device exists (`0x007651FC`) and flag bit 1 of `0x00764BB8` is set, calls
  slot `+0x10` of the interface at `0x00760B0C`. That interface is queried with IID
  `69C11C3E-B46B-11D1-AD7A-00C04FC29B4E` (IDirectDrawGammaControl) from the surface at
  `0x00764B98` (`0x00573ED0`, `0x005740C4`-`0x005740F9`; which surface it is, probably the
  primary, is HYPOTHESIS). Slot `+0x10` of that interface is SetGammaRamp (from the API's
  layout); the call's arguments are (0, `0x00764BE8`). No call is made while the flag is off.
- **Flag bit 1** is `0x00759AC0` being nonzero (`0x0059D92D`); `-DISABLEGAMMA` clears it
  (`0x005A69C9`-`0x005A69DB`). No store of a non-zero value was found: the cell is `+0x48` of
  the graphics settings object `0x00759A78`, in the part of `.data` that loads as zero, its
  address occurs only at that read and that clear, and neither the settings load
  (`0x00564B00`) nor the reset (`0x00563FB0`) writes `+0x48` (reviewer read). The owner's
  current live observation (§1) does not establish an enable path. The nonzero
  writer remains **UNREAD**; no setter was found in the bounded search.
  The QueryInterface result and SetGammaRamp's return are not tested; no hardware check exists.
- **Gamma float** `+6` = the options Gamma slider / 30 (slider 15..60, `0x00429CD0`) = stored
  level / 4096 clamped to 0.5..2.0 at load (`0x0043AE0D`-`0x0043AE15`); the shipped default
  level is `0x1000` (gamma 1.0, `0x00564B00`). It is set through presentation slot `+0x78`
  (thunk `0x004413E0` to `0x005974A0`), which rebuilds kind 0 and applies it at once when the gamma changes,
  abandoning a running change. An unchanged gamma only re-applies the stored ramp. `Renderer_Initialize` (`0x00574310`) first loads a plain
  ramp `i * 256` before any of this.

## 3. The 16 table builders (`0x00597760`, jump table `0x00598034`)

Let `e = 1 / gamma` and for index i = 0..255 `g = (i / 256) ^ e` (the CRT `pow`,
`0x00636520`; the constant at `0x00670454` is `0x3B800000`, exactly 1/256, so g never reaches
1: at gamma 1 entry 255 is 65279, and kind 0 is `trunc(i * 65535 / 256)`, a count under
`Renderer_Initialize`'s `i * 256`). Every entry is `trunc(65535 * v)` stored in 16 bits
(`_ftol`), to within rounding: a third is the float `0.33333334` (`0x00670458`), and in kind 1
green truncates the extended product while blue passes through a float store, so the two can
differ by one. `Renderer_Initialize` clears the x87 precision bits (`0x00574430`), which an
oracle run has to match. Sines use the float pi `3.14159274`. Columns
are the tables at `+0xC12`, `+0xE12`, `+0x1012`; naming them red, green, blue is STRONGLY
INFERRED (the DDGAMMARAMP order; kind 8, the damage flash, sets only the first).

| kind | red | green | blue | looks like |
|---|---|---|---|---|
| 0 | g | g | g | the user's gamma; identity at gamma 1 |
| 1 | g | g/3 | g/3 | red tint |
| 2 | g | g/3 | g | magenta tint |
| 3 | g/2 | g/2 | min(1.5g, 1) | cold blue-grey |
| 4, 11 | min(2g, 1) | g | g | pink-red lift (11 is the same builder) |
| 5 | g | g | min(4g, 1) | strong blue lift |
| 6 | 1 | 1 | 1 | all white (65535) |
| 7 | 0 | 0 | 0 | all black |
| 8 | 1 | 0 | 0 | all red |
| 9 | g/2 | 0 | 0 | dim red |
| 10 | see below | | | three-band false colour |
| 12 | see below | | | four-band false colour |
| 13 | as 12, red and green swapped | | | four-band false colour |
| 14 | 0 | 0 | min(1.5g, 1) | blue |
| 15 | g | min(g + 0.2, 1) | 0 | yellow-green |

False-colour kinds use `x = g` as the phase (so they posterise by brightness):
- Kind 10: x < 1/3: (0, 0, sin(3 pi x)); 1/3 <= x < 2/3: (0, sin(3 pi (x - 1/3)), 0);
  else all three sin(1.5 pi (x - 2/3)).
- Kind 12: x < 1/4: (0, 0, sin(4 pi x)); < 1/2: (sin(4 pi (x - 1/4)), 0, 0); < 3/4:
  (0, sin(4 pi (x - 1/2)), 0); else all three sin(2 pi (x - 3/4)).
- Kind 13: the same bands with the second and third bands' red and green exchanged
  (second band green, third band red).
Kinds above 15 build nothing (the table keeps its last contents). Names (a red wash, a
darkening) are inferred from the numbers only; no string names a kind.

The band builders store the gamma-shaped input as float32 before their sine arguments.
Kind 10 stores the first and last band's sine as float32; kinds 12/13 store bands 1, 2
and 4 as float32, leaving their third band in the FPU register. Kind 12 at index 96,
gamma 1, therefore reaches 65535 after its float store at `0x00597CBC`, not 65534.
STRONGLY INFERRED from the focused disassembly; complete x87 equivalence remains unverified.

## 4. Timing (`0x005976D0`, `0x00597540`, `0x00597690`)

- **Start** `Start(kind, startMs, lengthMs)`: current tables are copied to the start tables,
  kind and start time stored, running = 1, the queue emptied (a pending queue is lost),
  the target built for `kind`. Length 0 makes target = start = current and applies it at
  once.
- **Each frame** (`0x00597540`, called unconditionally at the top of `Presentation_Update`
  `0x0043B220` with the real-time clock `0x005B5E90`; nothing runs while the flag is 0):
  `w = ((now - start) << 16) / length`, signed 32-bit division. If `w < 0x10000`, every
  entry becomes `(w * target + (0x10000 - w) * start) >> 16` (low 16 bits) and the ramp is
  applied. So the blend is linear per entry, in real time (not game turns, not speed-scaled).
- **At the end** (`w >= 0x10000`): current = target exactly, then the next queue entry if any
  (kind and length from the entry, start time = this frame's `now`, start tables = the old
  target, target rebuilt), else the flag clears and the ramp stays as it is. No further
  apply happens while idle. A queued entry of length 0 would divide by zero (unguarded,
  `0x00597560`; command 0x0F queues its record's own length). The shift at `0x0059755C` wraps
  once `now - start` reaches 32768 ms, and `now < start` gives a negative w, which blends.
- **Queue** `0x00597690(kind, length)`: appends to eight entries; the ninth is dropped.
  Drained only by the end-of-change step above.
- A new Start during a change blends from the half-way table, not from the old target.

## 5. Which events start which change

All are for the local player only (handlers compare the record's player word with the local
player). Start times are the real clock at the call.

| event | change | source |
|---|---|---|
| any camera mode request but for mode 2 or 11 (`Camera_RequestMode` `0x0044A75B`-`0x0044A786`), again when it leaves a possession mode for an ordinary one (`0x0044A676`), and at the end of mode 11 (`Camera_Update` `0x0044C75D`) | kind 0 over 256 ms | possession-client §6 |
| flying into first person: once the move's end is within 256 ms, with camera flag `+0xE3C` bit 7 and a live creature in the thing slot (`0x0044C7EB`-`0x0044C84B`, `Camera_Update` `0x0044BED0`) | kind `def+0x86F` of the creature over 256 ms | §6 below |
| death view, entering mode 11 (`Camera_SetMode` `0x0044BA7F`-`0x0044BA98`) | kind 9 over 1500 ms, then queued kind 7 over 1500 ms (half red, then black; the move takes 3000 ms) | possession-client §6 |
| a possessed creature takes damage (`Thing_ModifyHealth` `0x004B2E5F`: creature byte `+0x3E6` bit 7, delta < 0; `0x0048F2E0` posts command 0x12) | kind 8 (red) over 125 ms, queued `def+0x86F` over 250 ms; not rate-limited | handler `0x00448A60` |
| a frozen possessed creature thaws (`Creature_UpdateTimedEffects` `0x0048E2A9`-`0x0048E2ED`: `+0x447` reaches 0, bit 7 set; command 0x16) | kind `def+0x86F` over 500 ms, nothing queued | handler `0x00448B00` |
| command 0x0F (`0x004486A0`; record: kind, length, player) | kind rec over rec length, then kind 0 over the same; kind 8 refused within 250 ms of the last (stamp view `+0x261D`) | no poster found |
| command 0x15 (`0x00448AC0`) | kind 14 (blue) over 125 ms | no poster found |
| options Gamma slider, level load (`0x00429CD0`, `0x0043AE1E`) | rebuilds kind 0, applies at once | not possession |

The commands arrive through `World_PostBridgeCommand` (`0x00517480`), the only caller of the
bridge enqueue, from 62 sites; none of them posts command 0x0F or 0x15 (checked by listing
each site's record, 2026-11-15), so those two handlers have no poster in the exe's own
code (UNKNOWN whether a network or replay record can reach them). Possession's changes are
every row above except the last three. Kinds 6, 10, 11, 12, 13 have no caller found.
Bridge command 0x15 here (table `0x0066D0D8`, dispatched by `0x0043B1E0`) is not game action
21 of the view table `0x0066CE30`, which ends a possession (possession-client.md §6).

## 6. The creature's kind (`def+0x86F`)

`CreatureDef_BuildFromRecord` (`0x00492320`, `0x00493CAB`-`0x00493CBD`): when the presence
dword at staging `+0x24C` is nonzero it copies the byte at staging `+0x112C` to the
definition word `+0x86F`; the default is 0 (`CreatureDef_Init` `0x00492202`). Staging is
file + 0x3F0, so the source is **`Creatures.kwd` record offset `0x0D3C`** (record i at file
offset 56 + 5449 i), the byte after the unique-archetype byte `0x0D3B`. Read from the install
(`Data/editor/Creatures.kwd`, all 46 records, 2026-11-15, STRONGLY INFERRED: the mapping is
read, the values are data):

| kind | creatures |
|---|---|
| 1 | Vampire, Unique Vampire |
| 2 | Dark Elf, Unique Dark Elf |
| 3 | Skeleton, Unique Skeleton |
| 4 | Salamander, Unique Salamander |
| 5 | Dark Angel, Unique Dark Angel |
| 15 | Maiden Of The Nest |
| 0 | every other record (Imp, Warlock, Mistress, Bile Demon, Troll, Horny, heroes, other uniques) |

Kind 0 is the plain gamma ramp, so those creatures show no tint. This agrees with the
OpenKeeper lead (a Skeleton halves red and green and lifts blue is kind 3). Per-level
creature records were not checked. Two other readers of `def+0x86F` are the posts in §5.

## 7. Implementation

The engine-free tables and transition queue, transient simulation notifications, Godot
full-screen pass and verification boundaries are recorded in
[possession colour rendering](possession-colour-rendering.md) (ADR-0818).

## 8. Unread, and leads

- What sets the gamma-enable flag `0x00759AC0` (§2). The current native Vampire observation
  is untinted (§1); close the enable condition before claiming any configuration applies
  the researched tables. Per-level creature overrides remain unchecked.
- Posters of commands 0x0F and 0x15, and any caller of kinds 6, 10, 11, 12, 13.
- `0x00597760` reads only its argument, the object's float at `+6` and image constants, so
  it can run in `tests/DK2.Oracle.Tests` to turn §3 into a value-by-value check.
- Windowed mode and non-gamma hardware: nothing read.
