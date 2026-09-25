# Hero-party walking after action 14

## Result in one sentence

A spawned hero leader repeats its standing `GoodJob`, ordinary
followers re-route to staggered points behind it through state 222,
and states 223/224 only coordinate a follower Dwarf tunnelling for a
leader whose route failed.

Source: `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on
2026-09-25. This note records binary facts in our own words (ADR-0046).
**CONFIRMED** means the cited instruction decides the claim;
**STRONGLY INFERRED** names a role from several such instructions;
**UNKNOWN** marks the remaining observable gap. No running-game
observation was made.

Calling conventions used below are **CONFIRMED** from each prologue,
epilogue and stack load. A creature-state controller is in `ECX`; its
body is at controller `+0x08`; state/job handlers have no stack
argument. `0x00478800` instead has `RET 4`: `ECX` is the controller and
its one stack argument is the job id. `0x00471890` has `RET 0x0C`:
point, resume state and options. `0x00471A70` has the same three stack
arguments with a room first.

## 1. The job dispatcher and the leader's standing job

`0x00478800` rejects category 22 and, during Call To Arms, every job
except 10 and 16. Otherwise it switches over job ids 0..28. A
successful arm writes the id to controller `+0x64`,
`CurrentlyDoingJobId`; failure leaves it alone.
**CONFIRMED** by `0x00478803`-`0x004789A2`.

The five shipped party jobs take these arms:

- **13 `StealGold` -> `0x00481FA0` -> state 163.** The player
  word `+0x40D` selects a player by tag. Zero calls `0x0050EA20`;
  that walks every player and keeps the one with the greatest sum of
  creature-definition byte `+0x787` (`0x004BCB20`). The target must
  differ from the creature's owner. It finds that player's nearest
  room type 1, proves a route with options 3,
  and enters 163 `stealing booty`. The value word is unread by this arm.
  **CONFIRMED** by `0x00462660` and `0x00481FA0`.

- **18 `KillCreatures` -> `0x00462310`.** The player word selects
  a player;
  zero uses `0x0050EA20` and writes the chosen tag back with
  `0x0048BE20`. The handler scans that player's creature list, retains a
  reachable member other than self, and routes to it with resume state
  177 `attacking Dungeon Heart`, options `0x41`. The value word is
  preserved if
  the player is filled in, but is otherwise unused. **CONFIRMED** by
  `0x0046231B`-`0x00462474`.

- **19 `KillPlayer` -> `0x00462490`.** Player selection and the
  zero-player
  rewrite are the same. `0x004C5F50` returns the selected player's
  `+0xB43` heart/dungeon-area record; when its tile is valid the handler
  routes there with resume state 178 `attacking Dungeon Heart`,
  options 1. The value word is unused. The record's exact name is
  **STRONGLY INFERRED** from its player-relative address and
  heart-state consumers; the accesses
  and state are **CONFIRMED** at `0x00462490`-`0x00462544`.

- **22 `Wait` -> `0x00486CC0` -> state 176.** It enters state 176
  and puts `now + 1` in controller `+0x44`. State 176 plays action 43
  while parked; after its deadline it picks a point in the current
  tile, reloads a random 50..84-tick deadline, and walks back to 176.
  Both job words are ignored.
  **CONFIRMED** by `0x00486CC0` and `0x00486CF0`.

- **23 `SendToActionPoint` -> `0x00471E80`.** A zero value enters
  state 227, whose handler `0x004B4C20` returns success without work.
  A non-zero value enters 173. State 173 resolves the low byte as an
  action-point id, samples its rectangle up to four times and routes
  to state 174. State 174 replaces the value with action point
  `+0x33`, its next waypoint, sets a deadline from `+0x30`, and enters
  175. State 175 wanders by adjacent tiles until that
  deadline and then returns to 173. The player word is ignored.
  **CONFIRMED** by `0x00471E80`, `0x00471EB0`, `0x00472150` and
  `0x004721C0`; action-point field names come from `action-points.md`.

The standing job is not a one-shot command. State 1 sends hero-owned
creatures to state 90 (`0x004773D0`). State 90's handler
`0x00477850` runs the body `GoodJob` through `0x00478800` on each
visit if leader/follower bit 2 is clear. On failure it sets a 12-tick
deadline and state 4; state 4 returns zero at the deadline, so
`0x00476FC0` resets to state 1 and the cycle retries. The job triple
is not cleared. A terminal job state that returns zero likewise goes
through state 1 and state 90, so a surviving leader repeats its job.
**CONFIRMED** by `0x004773D0`, `0x00477850`, `0x00477680` and
`0x00476FC0`.

A follower in state 90 does not dispatch its own spawn job: it enters
state 222. State 226 (`0x004787F0`) is the other direct per-turn
`GoodJob` caller; the stealing states use it after assigning temporary
jobs to party followers.
The dispatcher is not a category hook. Its other callers are the happy,
unhappy and angry choosers `0x004780E0`, `0x004782C0`,
`0x004784C0`, idle chooser `0x004778E0`, and its helper
`0x00478680`. **CONFIRMED** by the seven call references to
`0x00478800` and the state table built at `0x0047BBE0`.

## 2. States 222, 223 and 224

State 222 is the normal follower trail, although the question began
with 223/224. `0x00461F00` reads `MyLeadersId`, chooses a tile behind
that thing's heading, adds one of nine sub-tile offsets, and calls
`0x00471890(point, 222, 0x80)`. When the leader is itself travelling
in state 6, the reference point is its stored movement target rather
than its current position. **CONFIRMED** by
`0x00461F00`-`0x004620D4`.

Spacing is `max(PositionInParty >> 1, 1)` accepted tile steps behind
the leader. Positions 1, 2 and 3 therefore aim one tile back; 4 and 5
aim two; 6 and 7 aim three, and so on. Obstruction checks may rotate
the search and stop at a nearer tile. `PositionInParty` is therefore
spacing, not only a
count; the leader's copy remains the follower count. **CONFIRMED** by
`0x00461F36`-`0x0046204F`.

States 223/224 are entered only by option-bit-0 route failure:

- `0x00471890` and `0x00471A70` require a party leader (`+0x46F & 2`), a
  non-`0xFF` party and non-zero `PositionInParty`. They scan same-party
  followers (`+0x46F & 4`) for an `IsWorker` definition. On a successful
  `0x00485B80` or `0x00485AB0` tunnel request with resume 224, they
  put that
  worker's tag in the leader's `MyLeadersId` and set the leader to 223.
  These two helper branches are the only direct assignments of 223.
  **CONFIRMED** at `0x004719C8`-`0x00471A56` and
  `0x00471C59`-`0x00471CEA`.

- State 223 (`0x00462130`) treats `MyLeadersId` as the tunneller's
  tag. It requires the tag-table active byte and thing kind zero;
  otherwise it clears the word and returns zero. While valid it
  chooses an adjacent point behind the worker and calls
  `0x00471890(point, 223, 0x80)`. Thus 223 repeatedly travels through
  state 6 and resumes itself; `0x004622FB` is the only other
  site carrying 223, as a resume value. **CONFIRMED** by
  `0x0046213C`-`0x0046230C`.

- The worker tunnels in state 186 with 224 in its tunnelling resume
  slot `+0xD3`; state 186 is the only producer of 224. State 224
  (`0x004620F0`) reads the worker's ordinary `MyLeadersId`, forces
  that leader to state 1, then puts the worker in normal follower
  state 222. It has no liveness test. **CONFIRMED** by the two
  fallback calls, state 186's resume
  at `0x00485C10`, and `0x004620F0`-`0x00462129`.

## 3. Every `MyLeadersId` and `PositionInParty` reader

`InsGrep.py` scanned all disassembled instructions, including `.text`
`0x00401000..0x00652BFF` and `cseg`
`0x00653000..0x0066BFFF`. The complete creature-field reader set is:

- `0x00461F12`, state 222: select the leader thing;
  `0x00461F36`: calculate trailing depth. **CONFIRMED.**
- `0x004620FC`, state 224: select the leader to release.
  **CONFIRMED.**
- `0x0046213C`, state 223: select and validate the tunneller.
  **CONFIRMED.**
- `0x004716BD`, point feasibility `0x00471630`, and `0x00471807`,
  room feasibility `0x00471740`: require a non-zero follower count
  before asking a
  worker to tunnel. **CONFIRMED.**
- `0x004719C8`, point route helper `0x00471890`, and `0x00471C59`,
  room route
  helper `0x00471A70`: the same fallback eligibility test.
  **CONFIRMED.**
- `0x004904DA` and `0x004904F6`, creature diagnostic dumper
  `0x00490240`: print the two fields and decide no gameplay.
  **CONFIRMED.**

The other hits are not readers: constructor `0x004872A0`, health
handler `0x004B2CE0` and spawn `0x00519F90` write the fields;
`0x005604C0` writes a dword at the same numeric offset in an unrelated
`cseg` object; three `0x3E19999A` constants merely contain the search
text. **CONFIRMED** by the
base registers and opcodes at all 30 hits.

## 4. The party pace is a one-time write

The complete call-reference list for `0x004887D0` has 22 sites. The
spawn's `0x0051A6FC` is the only call with mode 0 and a non-zero
explicit speed taken from the party scan. No later call recomputes a
minimum or reads another party member. **CONFIRMED** by all call
windows and `0x0051A6D9`-`0x0051A6FC`.

`0x004887D0(mode, explicit)` records the mode at creature `+0x3BB`; a
zero explicit value selects the creature definition's walk/run/shuffle
field, while a non-zero one is passed straight to `0x0048D010`. Fight
entry uses mode 1 and zero, so it replaces the party cap with that
creature's run speed. Later mode-0 calls restore that creature's own
walk speed, not the party minimum.
The shared party pace is therefore never restored. **CONFIRMED** by
`0x004887D0`-`0x0048882D`, fight call `0x0045EBC9`, and the call sweep.

The level-stat arm at `0x00494BA4` divides its argument by variable 19
and writes only definition `+0x5B4` at `0x00494BB7`. It neither calls
the speed setter nor writes creature `+0x11C` or the steering body's
`max_speed`. A level-up does not reset the current cap; a later mode-0
speed call will use the updated definition speed. **CONFIRMED** by
`0x00494BA4`-`0x00494BBE`.

## 5. A leader lost to capture or death

The knock-out branch of health handler `0x004B2CE0` reorganises hero
parties whose `PartyId` is non-zero. A fallen follower is detached:
party id and `MyLeadersId` become zero and both role flags clear. For a
fallen leader, the first same-party follower in the hero list is
promoted, receives the old leader's job triple and trigger root, and
enters state 1. A second scan points and renumbers the remaining
followers behind it, then writes their count to
the new leader. **CONFIRMED** by `0x004B2FDF`-`0x004B30F3`.

Read again while building (ADR-0218). The branch runs only for the hero player's creatures
(`+0x28` against `[0x006F254C]`), right after the dying countdown starts (`0x004B2FB5`). The detach
clears bit 1 and bit 2 through `0x0048DF40(0)` and `0x0048DF70(0)`. The heir takes bit 1, loses
bit 2 and keeps its own `MyLeadersId`. The fallen leader keeps its party and its bit. The count
written to the heir covers the followers only.

This re-lead path notably excludes `PartyId == 0`, even though `0xFF`
is the
usual no-party sentinel. **CONFIRMED** by the byte test at `0x004B2FDF`.

There is no equivalent party-field access on the immediate-death path,
and the whole-offset scan found no other cleanup. Ordinary state 222
performs no active-tag or thing-kind test before dereferencing
`MyLeadersId`; only state 223 validates its temporary tunneller
reference. What followers observably do after a leader dies without
the knock-out/capture branch is **UNKNOWN** (Q428).

## Open question

- **Q428:** the observable fate of followers after immediate leader
  death. The binary's absence of another direct party-field cleanup is
  strong negative evidence, but does not establish how long the dead
  thing remains tag-addressable or what a later tag reuse looks like to
  state 222.

## Built (2026-09-25, ADR-0216)

`HeroSystem` and `HeroJobRules` follow sections 1, 2 and 4: state 1 to 90, the dispatcher's
arms for jobs 18, 19, 22 and 23 with states 173–178 and 227, the follower's 222 and the one-time
pace. Checked against the binary while building: state 90's failure deadline is `now + 12`
at `0x004778BB`; state 176 compares `+0x7E`, not the `+0x44` its entry writes; state 173 draws
the tile's y before its x and excludes the rectangle's upper bounds; its tile test rejects a
room or impenetrable tile of the hero's own keeper and any impenetrable tile a creature cannot
wade; state 178 is `XOR EAX,EAX; RET`. Definition byte `+0x787` is file byte `0x0F4C`
(loader `0x0049348A`): Imp 10, Dwarf 15, King Reginald 125, Dark Angel 232.
Not built: 223/224. The section 5 re-lead is built (ADR-0218), and StealGold (ADR-0219, `steal-gold.md`).

Review, the same day. State 1 tests the hero owner before the worker flag, so a hero Dwarf is run
as a hero, and a resting 10 idles into 90. An arrival's resumed state runs from the next turn.
Job 18 stops at the first reachable creature and fails outright if its route fails.

## 6. The point in the heart (read 2026-09-25, ADR-0221)

**Job 19.** It resolves `+0x40D` (or the strongest player, written back), then reads
`DungeonHeartPosition` (`0x004C5F50`, `player+0xB43`, a tile). The cell there must name a room
(`+0x2 & 0xFFF`; none fails). `0x004559B0` gives that room, and `0x004E4D90(room, &point, 0, 0)` a
point in it. Then `0x00471890(point, 178, 1)`. **CONFIRMED** at `0x004624EC`–`0x00462544`.

**`0x004E4D90(room, out, a, b)`.** It starts from `room+0x2D/+0x2E` and follows the cell links
`random(room+0xA7)` times (`0x005558D0`). Unless `a != 0 && b == 0`, it then steps on round the
ring while `0x00454110(cell)` is non-zero, or while it is zero when `b` is set. It writes the tile's
centre. **CONFIRMED.**

**`0x00454110(cell)`, non-zero meaning refused.** It refuses only for a room cell whose `+0xC` is
0xFF:
- **Types 3, 5, 8, 17, 22:** always accepted.
- **Types 9, 10, 15:** refused on one colour of a checkerboard that `room+0x80` shifts.
- **Type 14:** refused unless the cell holds an object of type 75.
- **Any other type:** refused.

So every heart tile is accepted, and job 19's point is a random heart tile. **CONFIRMED.**

**State 177's fallback** (`0x004625F7`–`0x00462642`). The centre of the `DungeonHeartPosition`
tile itself, `0x00471890(centre, 178, 0)`, with no room asked. **CONFIRMED.**
