# The VARIABLES corpus, swept (Q84, Q85, and an amendment to ADR-0085)

**2026-09-07.** New command **`dk2tools kwd varsweep`** parses all 66
`*Variables.kld` files and reports the `(unk1, unk2)` distribution, which ids
vary by level, which appear in only some levels, and a test of the id-66
hypothesis below. The three questions this settles were all of the shape *"true
of the level I happened to open"*.

## The finding that matters most: a level override can switch a disabled mechanism back on

ADR-0085 listed ten variables shipped as **0** in the global table with live
readers, and concluded there was *nothing to build* because a zero rate
faithfully reproduced is the absence of the effect. **The global table is not
the only place a value comes from.**

| id | variable | global | per-level override |
|---|---|---|---|
| **218** | `TimeBeforeDungeonHeartConstructionBegins` | **0** (ADR-0085: "disabled") | **`level2` = 15, `level8` = 16** |
| 55 | `PrisonModifyCreatureHealthPerSecond` | live | `Level6b` = **−5** |
| **94** | `MinimumImpThreshold` | **4**, ten readers | **0** on `FrontEnd3DLevel`, `Secret1`, `Secret2`, `Secret4`, `Secret5` |

So **id 218's mechanism runs on two campaign levels and nowhere else**, and
`MinimumImpThreshold` — a live mechanism with ten readers — is **switched off on
five levels**. Both directions occur.

**What this changes.** "Shipped as zero" was a statement about `GlobalVariables.kwd`
and was read as a statement about the game. The correct form is *"zero globally,
and check the 66 level files before concluding anything about a mechanism's
presence."* This is the same defect as the key/value trap below, one level up:
a value swept in one file and generalised. ADR-0085's other nine survive — none
of them is overridden anywhere — but the **conclusion** needed the sweep it did
not have.

## Q85: the trailing fields are a generic payload, and the (13,1) reading was one file

The record's payload is **three dwords**, and this project already reads them
three different ways depending on the id: `CreaturePool` uses them as
`(creatureId, value, playerId)`, `CreatureStats` as `(statId, level, value)`,
and a plain balance variable as `(value, unk1, unk2)`. **So `Unk1` and `Unk2`
are not trailing fields on every record — they are slots 2 and 3 of a generic
three-slot payload**, and the structured ids use them for real data.

For a **plain** variable they carry nothing:

- **18 distinct `(unk1, unk2)` pairs** across the corpus. The most common is
  **`(0, 0)` on 204 records**, then `(2, 0)` on 65 — every one of them id 66 —
  then `(13, 1)` on 41.
- **The same id carries different pairs in different files.** Id 94 appears with
  `(28, 5915252)`, `(0, 0)`, `(20, 5915252)` and `(13, 1)`; id 157 with
  `(0, 0)`, `(13, 4)` and `(20, 5915252)`. A field whose value depends on the
  *file* rather than the *variable* is not a property of the variable.
- The pairs cluster **by level family**, not by id: the `Secret*` levels carry
  `(20, 5915252)` and `(0, 0)`, while `level1`/`level2`/`level3` carry `(13, 1)`.
  `5915252` is `0x005A45B4`, the same value in eight records across separately
  authored maps — which is what stale memory in an editor's serialiser looks
  like, not a unit tag.

> **The previous reading is withdrawn.** This document used to say: *"In a level
> file almost every entry carries `unk1 = 13, unk2 = 1`, while the three that do
> not — `GameTicks`, `LevelRating`, `AverageTime` — carry `0, 0`. That is a
> **grouping** … so `unk1` may be a kind or unit tag rather than padding."* Every
> word of that is true **of one level file**, and false of the corpus. It is the
> `a-sweep-of-keys-is-not-a-sweep-of-values` defect with the sweep being a single
> file.

**What is left of Q85:** whether the engine reads slots 2–3 for a plain id. The
setter stubs store one value (`MOV [global], AL|AX|EAX`), so the value slot is
certainly consumed and the other two are certainly not consumed *by the stub*;
whether the record walker looks at them before dispatching is unread. Given they
vary by file for the same id, "read and ignored" and "not read" are the only
live options, and neither changes anything we build.

## Q84: id 66 is the level's player count, and id 77 is still unknown

**Id 66 is in 65 of the 66 levels**, which by itself corrects the question's
premise — it was catalogued as an obscurity and it is one of the most
consistently present per-level settings in the game. Values run **2 to 7**, and
it always carries `unk1 = 2`.

**Tested against the one per-level source that names players outright**, the
creature pool's player ids: **54 exact agreements, 3 disagreements, 8 levels with
no pool to test against.**

| level | id 66 | highest pool player |
|---|---|---|
| `level1` | 3 | 3 |
| `Frosty's Castle` | 6 | 6 (keepers 3–6) |
| `Level13`, `level8` | 4 | 3 |
| **`Level17`** | **2** | **5** |

**STRONGLY INFERRED: id 66 is the level's player count, equivalently the highest
player id in use.** The two levels where it reads one higher than the pool are
expected rather than awkward — the pool is a *lower bound* on players, since a
keeper can exist with no portal roster. **`Level17` is a genuine counterexample**
and is named rather than smoothed away: 2 against a pool reaching player 5.

**The decisive test is the PLAYERS record**, which this project does not parse
(one of the nine unread record types). One field from it closes this outright,
and it is worth doing when PLAYERS is read for M6 rather than before.

**Id 77 stays UNKNOWN.** Nine levels — `Level16`, `Level6b`, `Secret1`,
`Secret2`, `Secret3`, `Secret5` and three more — with values 1, 3, 4, 11, 18, 23,
29 and seven different `(unk1, unk2)` pairs. Its concentration in the `Secret`
levels is the only pattern, and one pattern over nine samples is not a reading.

## The key/value list, for anyone who needs it

Twelve ids take more than one value across the levels that carry them. Worth
having in one place, because every one of them is a trap for a sentence of the
form *"X is N"*:

`EntranceGenerationSpeedSeconds` (5 values, 5 levels) · **id 66** (6, 65) ·
**id 77** (7, 9) · `BoulderInitialHealth` (2, 3) ·
`BoulderSpeedTilesPerSecond` (2, 2) · `FirstPersonRechargeModifyPercentage`
(3, 3) · `MusicLevelTwoThreatThreshold` (7, 20) ·
`MusicLevelThreeThreatThreshold` (9, 11) · `MusicLevelFourThreatThreshold`
(6, 9) · `TimeBeforeDungeonHeartConstructionBegins` (2, 2) ·
`LevelRating` (16, 66) · `AverageTime` (15, 66).

Only **one** id is present in every level file with a single value, so "the
level files agree on this" is almost never true and should never be assumed.
