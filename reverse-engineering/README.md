# reverse-engineering/

Working space for decompilation of the original `DKII.EXE`. **Everything in here
except this file is gitignored, deliberately and permanently** (`.gitignore`:
`reverse-engineering/**`, with one negation for this README). Authorised by the
owner on 2026-09-04: *"about ghidra, you are free to use it and decompile it, put
all decompiled resources under reverse-engineering folder."* The rule that governs
what may leave this folder is **ADR-0046**.

## The one rule

**Nothing in this folder is a source of code. It is a source of facts.**

| | |
|---|---|
| May cross into the repository | A number, a formula, a comparison, an order of operations, a description of control flow — **written in our own words**, in `docs/research/`, with provenance saying it came from the binary |
| May **not** cross, ever | Decompiler output itself. Not into `src/`, not into `docs/`, not into a commit message, not into a test fixture. Not reformatted, not renamed, not "cleaned up" |

The project is a clean-room *behavioural* reimplementation and was told not to copy
original source code merely because reverse engineering makes it available.
Decompiler output **is** source derived from the binary. A formula read out of it is
a fact about how the game behaves; the function it was read from is EA's expression
of that fact.

## Rank of evidence

**This folder is the FIRST place to look, not the last.** Owner, 2026-09-04: *"I
want you to make using ghidra and decompiled sources as the primary source and go to
when in doubt."* `DKII.EXE` is the actual DK2; OpenKeeper, Flame, the wiki and this
project's own inferences are all somebody's *reading* of it. An open question owes a
Ghidra pass before it is filed as UNKNOWN — ADR-0046's 2026-09-04 amendment.

**Consulted first is not the same as trusted most.** For behaviour the owner can
watch, the running game still wins: the decompiler is an inference engine that
invents variable names, guesses types, and silently mismodels optimised float and
fixed-point arithmetic. Where the two disagree the running game wins and the
disagreement gets written down; `[Dk2Behavior]` CONFIRMED stays reserved for the
running game.

Where the binary and the *shipped data* disagree, that is interesting rather than
settled — ADR-0023 deliberately disbelieves one shipped value.

**Running beats reading, for a function that can be run.** A function whose answer
depends only on its arguments and the image's own bytes (maths, fixed-point helpers,
tables, the seeded random draw) can be executed instead of re-read: see below. That
settles a port value by value on every input run, which no reading does; between the
swept inputs it is still a sample, and it is binary evidence, not the running game.

## Running the original's functions (ADR-0481)

`tests/DK2.Oracle.Tests` holds our C# to `DKII.EXE` itself: tables byte for byte, and
functions run on the Unicorn emulator by `tools/oracle/dk2oracle.py` over swept inputs.
Setup, once: a Windows x64 `unicorn.dll` 2.1.x outside the repository (the PyPI
`unicorn` win_amd64 wheel has one; the NuGet package has none), and
`DK2_UNICORN_DLL` set to its path, plus a Python 3 (`py -3`, or `DK2_PYTHON`); without
the DLL the function tests skip, while the table tests need only the install. Unicorn is never
committed (owner, 2026-10-15). `tools/oracle/purity.py` lists ported candidates (read each before trusting it);
`tools/oracle/capture_memory.py` turns a capture tick into a memory image for functions that read the world
(it needs this folder's local `dk2capture.py`, or `DK2_CAPTURE_DECODER`).
To add a function: read it in Ghidra for its calling
convention, out-pointer and record layout, check it reads no mutable global, and add a
test after `OriginalFixed16Tests` that sweeps the band edges and counts the arms it
reaches. A divide fault in the original is outside its domain and is counted, not
compared. The traps it has cost are in `docs/TRAPS.md`.

## What is in here

Ghidra project: the one project `D:\Ghidra\DK2_RE`, opened with Ghidra 12.1.4 and
cloned per runner run and per pyghidra-mcp session, the clone deleted at exit (outside
the repo — a Ghidra project of this binary contains the binary). Exports land here as
`<subject>.c` / `<subject>.txt`, one file per question, named after the question they
were opened for. The headless runner is `tools/ghidra/ghidra.py --agent <you>` (command and porting notes in `docs/RESEARCH-QUEUE.md`); the scan
recipes and the traps that have cost committed claims are in the `ghidra-queries`
skill (`.agents/skills/ghidra-queries/SKILL.md`) and `docs/TRAPS.md`.

## The binary carries RTTI

`DKII.EXE` ships MSVC run-time type information, so **any vtable can be traced to
the class it belongs to** — the dword immediately before a vtable is a complete
object locator, giving both the base's offset within the object (0 for the primary
vtable, non-zero for a secondary base under multiple inheritance) and a type
descriptor holding the decorated class name. `tools/ghidra/scripts/Rtti.py` walks it, and accepts either
a vtable base or any code pointer stored inside one, so it can be pointed straight
at a "1 absolute reference, in .rdata" scan result.

**What may leave this folder from it:** the addresses, the base offset, and whether
two vtables share a class. **The decorated names stay here.** They are the
original's own internal identifiers and they buy nothing a scan consumes — every
script here is driven by addresses, offsets and globals, never by a class name. A
whole-binary class map is a useful local asset and a large import of somebody else's
naming: generate it here if it is wanted, and let only addresses cross.

## Full-state captures (`dk2record.py`, `dk2capture.py`)

One `-PLOAD` playback, recorded once, answers later questions offline. The
recorder polls DK2 from outside and stores raw bytes every world tick. Decoders
then read fields out of those bytes afterwards, so a new question needs a new
decoder, not a new playback. Nothing here patches the game.

**Record** (probe first, elevated; then the game, elevated):

```
python -u dk2record.py dk2runs/<run>/<name>.dk2cap --map 40x50 --last-tick 3545
"E:\GogGames\Dungeon Keeper 2\DKII-DX.exe" -LEVEL level2 -PLOAD l2win928 0
```

It waits for the process, then for a level with that map size (`--map 0` takes
any), then stores one frame per new tick until `--last-tick`, the process
exits, or the tick goes backwards. `--dump-ticks 100,1500` also writes the whole
process memory at those ticks, the game suspended (`<out>.<tick>.mem.z`, from
`tools/oracle/process_dump.py`): definitions and heap the frames leave out, for
the emulator oracle. The game stands still while each is written (the summary
gives the seconds), so such a recording is not a timing run; each dump is
tens to hundreds of MB. It ends by printing a summary: pass times,
torn and missed ticks, and unknown classes.

**What a frame holds**, each item in one bulk read:

| region / record | address | size | size from |
|---|---|---|---|
| every non-null slot of the thing table | `[0x006EE540 + slot*4]`, all 4,096 slots | per class, by the vtable at record `+0` | allocator and vector-iterator sizes in the binary (below) |
| the thing table and its per-slot active bytes | `0x006EE540`, `0x006ED540` | 16 KB, 4 KB | register `0x00507850` / unregister `0x00507920`, `0x00507970` |
| the world object | `[0x006E5054]` (= `0x006F2A08`) | `0xA36B` | an upper bound: the next referenced global is `0x006FCD73` |
| the player array | `[world+0x84]` | 7 × `0x1125` | vector construction at `0x004C1E64` |
| each player's task pool | `[player+0x1105]` | `0x469B` | allocation at `0x004B87BD` (players whose `Number` is not 1 or 2) |
| the tile array | `[world+0x9DA+0x14]` | width × height × 16 | `runtime-map.md` |

Record sizes: creature `0x53F` (a pool of `0x1C0` built at `0x004B5A7E`),
object `0x189`, door `0x11E`, trap `0x123`, action point `0x40`, effect generator
`0x45`, shot `0x173`, dead body `0x140`, room `0xA9`. The heap allocator
`0x00554B30` takes the byte count, rounds it up to 4 and adds a 12-byte header.
A pointer is live exactly when it is non-null, because unregister nulls it. A
vtable the table does not know is stored as 64 bytes and counted in the summary.
The seven players are registered in the thing table too. There only their
first 64 bytes are stored, because the player region holds the full records.

First real run (Level 2, `l2win928`, 2026-09-28): 3,536 frames, no torn passes,
no missed ticks, pass p50 3.7 ms / p95 6.2 ms / max 25 ms, 10.8 MB.

**File layout.** The magic `DK2CAP\x01\n`, then chunks, each `u32 length, u8 kind,
payload`:
- `H`: a JSON header (class table, sizes, settings);
- `F`: one zlib-compressed frame per tick;
- `E`: a JSON end summary.

A frame is a head (`seq, tick_before, tick_after, wall, pass_ms, flags`; bit 0
keyframe, bit 1 torn), then its regions and its records. Each region or record
has a mode: full, XOR against the previous frame's bytes, gone, or unreadable.
A keyframe (every 256 frames, and the first) stores everything in full. Other
frames store only what changed. XOR is used only when the slot keeps the same
pointer, class and size; otherwise the bytes are stored in full.

**Decode.** `Capture(path).at(tick)` or `.frames()` give snapshots with
`records()`, `things(kind)`, `record(slot)`, `region(id)`, `tile(x, y)`,
`player(i)`, `timers()` and `flags_bank()`. A record's `f(name)` reads a named
field. Every offset lives in one table, `dk2capture.FIELDS`, and each entry
cites its source. CLI: `python dk2capture.py <cap> info | creature-map |
object-map | tiles | floor-gold <out.csv>`. The first two regenerate the old
`level2_creature_map.py` CSVs exactly. `dk2check_l2.py` runs the Level 2
checks in a run folder's `PREDICTIONS.md`. `test_dk2capture.py` is the offline
dry run: a synthetic game in memory, polled by the real recorder loop, then
checked frame by frame.

**Known limits.**
- **Torn passes.** The tick is read before and after each pass, and a pass
  that saw it change is flagged. A pass that falls inside one world update is
  not detected. The recorder waits 30 ms after the tick changes (`--settle-ms`)
  to make that unlikely.
- **Missed ticks.** If the game runs two ticks between polls, the gap is
  counted and listed in the summary. Nothing is interpolated.
- **The first frame can catch the level still loading.** In the first run,
  tick 1 had all 448 pool creatures registered and zeroed and no objects yet.
  Read from tick 2.
- **Not stored:**
  - anything a record only points at (definitions, path buffers, the
    navigation cache, per-tile thing lists);
  - effects and lights, which live outside the thing table;
  - balance variables and terrain tables, which are static per level and come
    from files;
  - the player interface and camera, which is what condition 72 reads and why
    two playbacks of one recording differ;
  - the packet stream itself.

## Contents

Nothing is committed but this README, by design. `git status --short
reverse-engineering/` must show no other file before any commit.
