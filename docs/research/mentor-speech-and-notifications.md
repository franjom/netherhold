# The Mentor's speech, the notification table, and six lines DK2 never says

**Scope:** how DK2 v1.70 turns a game event into a spoken Mentor line with a
subtitle — the notification type table, the encoding it stores speech references
in, the queue that plays them — and the idle-taunt mechanism in particular.

**Sources:** `DKII.EXE` read 2026-09-10 via Ghidra (ADR-0046 governs the
boundary; facts in our own words, nothing here is decompiler output), the shipped
`Speech.str` and `speech_mentorHD.SDT`, and one owner recollection that corrected
the binary read. The `.str` container format is **not** re-documented here — its
one home is `src/DK2.Data/README.md` §STR.

## Confidence

| Claim | Confidence | Basis |
|---|---|---|
| `Speech.str` id *n* and mentor-bank sample *n* are the same line | **Known** | Ids 0–17 are the countdown texts and samples `count001`–`count018`; the code passes 1–18 |
| The queue argument is the id **plus one** | **Known** | The subtitle path re-subtracts it; the sound-bank event ids run 1..1505 for 1505 samples |
| Notification types live in a 100-entry table at `.data 0x006A0B00`, 10 bytes each | **Strongly inferred** | The resolver's own indexing; all 100 rows decode coherently |
| A speech reference is stored as **`-(10000 + id)`** | **Strongly inferred**, corroborated by observation | Eight independent coherence checks below, plus the owner hearing two lines the rival reading excludes |
| Type 89 is the Mentor taunt pool, 27 variants | **Strongly inferred** | Its variant list is 27 lines, all inside the authored `silly` block |
| The idle taunt is type 89 variant 3 = *"The very rock yawns…"* | **CONFIRMED** that the line occurs in play (owner recollection, 2026-09-10); the **trigger** is strongly inferred | Owner has heard it; the mechanism is read from the binary |
| Idleness is **cursor movement**, measured in **real** milliseconds | **Strongly inferred** | The handler compares a stored cursor position and stamps the `timeGetTime` wrapper |
| The taunt fires at **160 s**, the camera wander at **180 s** | **Strongly inferred, not yet timed** | Two immediate comparisons; the stopwatch test is in `observation-checklist.md` |
| Six `silly` lines have no code path in v1.70 | **Strongly inferred** | Scope of the negative is stated below |
| All six nevertheless ship complete audio | **Known** | 33/33 distinct payloads, well-formed, durations tracking their own subtitles |
| Cursor kind 12 = Library | **HYPOTHESIS** | The build-hint table maps index 12 to the Library name string; same id space is assumed, not shown |

---

## 1. One line is one id in three places

A Mentor line is a single zero-based index that addresses three shipped things at
once:

| | |
|---|---|
| subtitle | `Data/Text/Default/Speech.str` (file id 1090, **1542** entries) |
| audio | `Data/Sound/Sfx/speech_mentor/speech_mentorHD.SDT` (**1505** entries) |
| sound event | `speech_mentorSFX.map`, 1505 records whose event ids run **1..1505** |

Sample *n* is the audio for subtitle *n*. The countdown block proves it without
any inference: `Speech.str` 0–17 read *"You have 30 minutes remaining"* through
*"One!"*, and samples 0–17 are named `count001.mp2`–`count018.mp2`. The `silly`
block — the Mentor's jokes and taunts — is `silly001`–`silly033` at ids
**1166–1198**.

Because sound events are 1-based and subtitles 0-based, **everything that reaches
the speech queue passes `id + 1`**, and the queue subtracts one again for the
subtitle. The countdown code passes 1..18 for ids 0..17. Keep this off-by-one in
mind; it is the reason a search for a line's id finds nothing.

## 2. The speech queue

`CSpeechSystem` carries MSVC RTTI, so the class name is the binary's own. Its
constructor at `0x00567F90` loads the bank named `SPEECH_MENTOR`.

| Address | What it does |
|---|---|
| `0x00568390` | **enqueue.** 16 slots of 12 bytes (id, plus two caller values), and a parallel 16-slot subtitle queue whose display time is derived from the resolved string's length |
| `0x005681D0` | **update / dequeue.** Enforces a minimum **1000 ms** gap between lines |
| `0x00568570` | is this line still playing? |
| `0x00568140` | clear the queue |

Most callers never touch this directly. They raise a **notification** instead.

## 3. Notifications are (type, variant), not ids

The resolver is `0x004C4600`, taking a message **type**, a player, a **variant**
selector and an allocate flag. It has 86 callers — this is the game's single
"tell the player something" entry point.

Types index a table of **100 records of 10 bytes** at **`.data 0x006A0B00`**:

| Field | Width | Meaning |
|---|---|---|
| `+0` | u16 | flags; **low 13 bits are the on-screen duration in game seconds**, scaled by variable 19 (Q1, = 4 ticks) |
| `+2` | i32 | either the speech reference, or — when flag `0x8000` is set — a pointer to a variant list |
| `+6` | u32 | copied to the notification record; role unread |

Known flag bits: `0x8000` "field `+2` is a variant list", `0x4000` "skip the
already-queued dedupe scan", `0x2000` copied to the record. A type ≥ 100 falls
back to row 0, which is empty — so 100 is the real ceiling.

A **variant list** is 8-byte records — `{u16 variantId, u16 durationSeconds,
i32 speechRef}` — terminated by a record with variant id 0 whose reference is the
default used when the requested variant is not found.

Rows read so far (2026-09-23): type **1** → speech 345 *"Your Dungeon Heart is under
attack!"*, 120 s, raised by the heart's health change on a negative delta; **39** → 346
*"Your Dungeon Heart is dying!"* and **40** → 344 *"... in a critical state!"*, 50 s each,
raised by the heart room's frame at exactly 50 % and 10 % (`win-and-defeat.md` §3.1).
Type 6 is the "your creature is fighting" notice (`combat-fight-state.md` §1), its row unread.

### The encoding, and why it matters

A speech reference is stored **negated with a 10000 bias**:

```
storedValue = -(10000 + zeroBasedSpeechId)
```

The classifier at `0x004C53B0` is what reveals the shape: it reads the
notification record's reference field, and a value **below −10000** is a speech
reference. (Other bands mean other things: `0 < v < 10000` is one class, and
−128..−1 another.) This is why no plain `1166` or `1171` constant exists anywhere
in the executable, and why a naive search concludes the taunt pool is empty.

**The bias is 10000, not 10001**, and getting it wrong shifts every line in the
game by one. Eight checks agree, and each is incoherent at 10001:

1. Type 23 has 13 variants; ids 39–51 are exactly the 13 *"Your X spell has been
   upgraded"* lines, with *"You have researched every available spell"* (38)
   correctly **not** among them.
2. Type 26 has 9 variants; all nine resolve to distinct *"A X Trap has been
   created in your Workshop"* lines, with no misfit.
3. Type 88 has 8 variants; ids 508–515 are exactly the 8 *"Your creatures are…"*
   lines.
4. Types 90 and 95 become a coherent pair about Horny (availability, then how to
   expel him).
5. `Player.cpp` raises variant 1 when the keeper holds fewer than 5 creatures,
   and variant 1 is *"Keeper, your Dungeon is emptier than your head"*.
6. It raises variant 26 at two hours elapsed, and variant 26 is *"If you were any
   kind of a real Keeper you would have won by now"*.
7. The idle handler's alternate branch requires creatures in a room of kind 12,
   and its line is *"You'll go blind, you know"* — the Library joke.
8. **The owner has heard ids 1171 and 1198 in play** (2026-09-10). Reading the
   bias as 10001 puts both outside the pool; reading it as 10000 puts them in it.

Check 8 is what caught the error: the first pass here used 10001, and reported
the yawn line as unreachable. *The running game outranks the disassembly*
(AGENTS.md), and it did.

## 4. Type 89 — the Mentor taunt pool

27 variants, list at `.data 0x006A09D8`. Ids are zero-based `Speech.str`.

| v | id | line |
|---|---|---|
| 1 | 1167 | Keeper, your Dungeon is emptier than your head |
| 2 | 1168 | Trick or treat, Keeper? Buwa ha ha! |
| 3 | 1171 | **The very rock yawns with anticipation of your next fascinating move** |
| 4 | 1173 | It is the Witching Hour. Curses are half-price |
| 5 | 1174 | Surely, even Dungeon Keepers must retire to a Lair of some description? |
| 6 | 1175 | Hello? Are you still there? The Imps are about to lock up |
| 7 | 1176 | Your nocturnal perseverance has earned you a hidden gaming tip. GO TO BED! |
| 8 | 1177 | You know that low, broad, downy-soft item of furniture in the next room?… |
| 9 | 1178 | You'll go blind, you know |
| 10 | 1179 | You have an excess of Mistresses. There's a word for Keepers like you |
| 11 | 1180 | Hic! Your creatures demand cakesh and fine winesh |
| 12 | 1181 | A lost soul has entered your Dungeon. Oh no, it's wandered out again |
| 13 | 1182 | A neutral creature sends word that he can't make it today |
| 14 | 1183 | Your Dungeon floor is lumpy. Order your minions to jump up and down |
| 15 | 1184 | Hungry Demons cannot reach their toes |
| 16 | 1186 | Your Dungeon is on an incline. Angry creatures cannot play marbles |
| 17 | 1187 | Big monsters! Big prizes! I love it! |
| 18 | 1188 | Your Dungeon is full of yoghurt |
| 19 | 1189 | Keeper, you have something unpleasant under your fingernail |
| 20 | 1190 | Micro-piglets stalk your Dungeon. Beware! |
| 21 | 1191 | Your Dungeon is damp. Install central heating |
| 22 | 1192 | Never eat anything bigger than your own head, Keeper |
| 23 | 1193 | Your Lair has been re-carpeted |
| 24 | 1194 | Your minions demand cable |
| 25 | 1195 | Timing is... ... ...everything |
| 26 | 1196 | If you were any kind of a real Keeper you would have won by now |
| 27 | 1198 | One of your Imps does a great impression of you. He can even do the ears |

Note the variant numbers are dense 1..27 while the ids skip — the six skipped
ids are §6.

### Who raises it

| Where | Condition | Variant |
|---|---|---|
| `DefaultPlayerInterface.cpp`, `0x00407380` | **160 s of cursor stillness** (§5) | **3** (the yawn line) |
| same | same, but cursor over kind 12 with >4 own creatures in state 4 | **9** |
| `Player.cpp`, `0x004B9740` | `tick % (2400 × 4) == 0` — every 2400 game seconds | **12 + Random(13)** → 12..24 |
| same | `tick == 3000 × 4` and fewer than 5 creatures pass a flag test | **1** |
| same | `tick == 7200 × 4` | **26** |
| `ShotProcess.cpp`, `0x004AD9E9` | a shot/effect type equals 20 | **27** |
| `Entrance.cpp`, `0x004DCCC8` | a count equals 10 | **10** |
| `0x0050A900`–`0x0050AA40` | eight sites, conditions unread | 2, 2, 4, 5, 6, … |

The game tick comes from `0x00517490` (the world tick accessor, 196 callers) and
the multiplier is variable 19 at `0x006F2590`, established as 4 ticks a second by
Q1 — so 2400 there is 2400 **game** seconds, and 7200 is two game hours.

Its sibling **type 88** is the eight *"Your creatures are happy / pleased / in
awe…"* lines (ids 508–515), raised from the same `Player.cpp` routine every 1500
game seconds as `1 + Random(8)`.

## 5. The idle mechanism

The routine at **`0x00407380`**, in `DefaultPlayerInterface.cpp` (a vtable method, run
per frame). It is the whole of DK2's "player has wandered off" behaviour.

**Activity is cursor movement and nothing else.** The handler keeps the last
cursor position on the interface and compares it each frame; on any change it
re-stamps a millisecond timestamp and clears the fired latch. Keyboard input,
camera keys and unit orders do not themselves reset it — only the pointer moving
does.

The clock is the `timeGetTime`-based wrapper at `0x005B5E90`, so these are **real
milliseconds**: they do not scale with game speed, and pausing does not stop
them.

| Threshold | Effect |
|---|---|
| **160 000 ms (2 min 40 s)** | Raise type 89 **once** per idle spell — variant 3, the yawn line, or variant 9 if the cursor is resting on kind 12 with more than 4 of the player's creatures in state 4. A latch on the interface prevents repeats until the mouse moves |
| **180 000 ms (3 min)** | Arm, then on subsequent frames call `0x00409990`, which no more than once every **15 000 ms** picks `Random(creatureCount − 1)` of the player's creatures, stores its id on the interface and posts a command at its position — the attract/camera wander |

Both are suppressed while a mode global (`0x0075AD4B`) equals 3 — very likely
possession or a cutscene, unread — and while a field on the player object is
non-zero.

**Not yet timed.** 160 s and 180 s are read from two immediate comparisons and
have never been checked with a stopwatch; the experiment is Q282 and
`observation-checklist.md`.

## 6. Six lines that ship complete and are never selected

Variant coverage of the `silly` block is 27 of 33. These six have **no reference
anywhere in `DKII.EXE`**:

| id | sample | audio | line |
|---|---|---|---|
| 1166 | `silly001.mp2` | 3.92 s | Hmm. Are you sure you haven't been playing Populous? |
| 1169 | `silly004.mp2` | 4.55 s | You're taking your time on this one, Keeper. Scared? |
| 1170 | `silly005.mp2` | 5.54 s | Do you await some ominous sign or portent from the Undergods of Fire and Soil? |
| 1172 | `silly007.mp2` | 2.46 s | Familiarity breeds contempt, Keeper |
| 1185 | `silly020.mp2` | 1.93 s | The Wizard needs food |
| 1197 | `silly032.mp2` | 3.76 s | Other Keepers never forget their minions' birthdays |

**The audio is real, not filler.** All 33 `silly` payloads are well-formed
MPEG-2 Layer II, 48 kbps CBR, mono, 22 050 Hz, and all 33 have distinct
SHA-256 digests — none of the six is a copy of a used sibling. Their speaking
rate (10.9–14.3 characters a second) sits inside the range of the 27 used ones
(8.1–15.1), so each recording tracks the length of *its own* subtitle across six
independent cases, which filler or silence would not. For contrast, the bank's
genuine stubs are named `PlaceFiller.mp2` and are 315 bytes — **0.05 s**; none
falls in this range.

**Scope of the negative** (a negative is a claim about the scope searched):

- all 100 notification types and every variant list they point at, walked
  structurally rather than pattern-matched;
- a raw scan of **every initialised block** of `DKII.EXE` **and** `DKII-DX.exe`
  for the encoded reference as both i32 and i16 — the only hits are conditional
  jump and call displacements plus one fixed-point table;
- `patch.dll`;
- all 396 files under `Data/editor/maps`, with a control value to prove the scan
  worked.

Not searched: earlier retail builds. **Only v1.70 (GOG) was available**, and the
possibility that 1.0/1.3/1.51 wired some of these up is untested — the owner
recalls play from the original release era, so this is a live alternative to
"cut". The gaps are **not contiguous**, which argues against a truncated list and
for six individual removals.

## 7. What this means for the remake

The pool is cheap to implement faithfully: 27 variants, one table, and three
trigger sites that matter (idle, the 2400-second cadence, the 1500-second
`creatures are happy` sibling).

**Restoring the six costs nothing in assets.** Their audio is already in the
player's own install, addressed by the same id as every other line, so the
existing `.str` reader and an SDT reader reach them with no new content and
nothing to redistribute — the six `.mp2` payloads must **not** be committed
(original DK2 material), and access goes through the asset abstraction layer like
everything else.

**But it would be a deliberate deviation from DK2**, and AGENTS.md
requires that a deviation be recorded rather than quietly "fixed". No decision is
taken here. If it is taken, it needs an ADR, and the honest shape is a switch
defaulting to DK2's behaviour, because a faithful mode has to be able to *not*
say them. The natural home for the extra five taunts, if enabled, is the idle
handler's 160-second slot — which today can only ever say one of two lines and
has three cut taunts (1169, 1170, and 1166) sitting unused that read exactly like
idle taunts.

## 8. Leads this turned up, not followed

- **The control roster.** `controls.md` records that the Define Keys screen's
  action list is unread. A table of 182 pairs at `.data 0x006BCE50` — action
  index and a name string id — is read by the options screen code at
  `0x00561050`, and looks like exactly that roster. Unverified: the string ids
  were not resolved against `Text.str`.
- **The build-hint table.** `.rdata 0x0066C788`, 52 records of 16 bytes indexed
  by room/object kind, each holding a zero-based `Speech.str` id — this is what
  says *"A Sentry Trap has been created in your Workshop"* and the room names.
  It is a second, independent speech table and is how kind 12 was read as the
  Library.
- Notification table fields `+6`, and flag bit `0x2000`, have no read role.
