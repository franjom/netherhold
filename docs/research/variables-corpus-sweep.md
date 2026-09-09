# The VARIABLES corpus, swept (Q84, Q85, and an amendment to ADR-0085)

**2026-09-07.** **`dk2tools kwd varsweep`** parses all 66 `*Variables.kld`
files and reports the `(unk1, unk2)` distribution, which ids vary by level,
which appear in only some levels, and a test of the id-66 hypothesis below.
Every table here is reproduced by that one command. The three questions this
settles were all of the shape *"true of the level I happened to open"*.

## The finding that matters most: a level override can switch a disabled mechanism back on

ADR-0085 listed ten variables shipped as **0** in the global table with live
readers, and concluded there was *nothing to build* because a zero rate
faithfully reproduced is the absence of the effect. **The global table is not
the only place a value comes from.**

| id | variable | global | per-level override |
|---|---|---|---|
| **218** | `TimeBeforeDungeonHeartConstructionBegins` | **0** (ADR-0085: "disabled") | **`level2` = 15, `level8` = 16** |
| 55 | `PrisonModifyCreatureHealthPerSecond` | −3 | `Level6b` = **−5** |
| **94** | `MinimumImpThreshold` | **4**, ten readers | **0** on `FrontEnd3DLevel`, `Secret1`, `Secret2`, `Secret4`, `Secret5` |

So **id 218's mechanism runs on two campaign levels and nowhere else**, and
`MinimumImpThreshold` — a live mechanism with ten readers — is **switched off on
five levels**. Both directions occur.

**What this changes.** "Shipped as zero" was a statement about
`GlobalVariables.kwd` and was read as a statement about the game. The correct
form is *"zero globally, and check the 66 level files before concluding anything
about a mechanism's presence."* This is the same defect as the key/value trap
below, one level up: a value swept in one file and generalised. ADR-0085's other
nine survive — none is overridden anywhere — but the **conclusion** needed the
sweep it did not have.

## Q85: the trailing fields are a generic payload

The record's payload is **three dwords**, and this project already reads them
three different ways depending on the id: `CreaturePool` uses them as
`(creatureId, value, playerId)`, `CreatureStats` as `(statId, level, value)`,
and a plain balance variable as `(value, unk1, unk2)`. **So `Unk1` and `Unk2`
are not trailing fields on every record — they are slots 2 and 3 of a generic
three-slot payload**, and the structured ids use them for real data.

For a **plain** variable they carry nothing:

- **18 distinct `(unk1, unk2)` pairs** across the corpus. The most common is
  **`(0, 0)` on 204 records**, then `(2, 0)` on 65, then `(13, 1)` on 41.
- **The same id carries different pairs in different files.** Id 94 appears with
  `(28, 5915252)`, `(0, 0)`, `(20, 5915252)` and `(13, 1)`; id 157 with
  `(0, 0)`, `(13, 4)` and `(20, 5915252)`; even id 66 carries `(2, 0)`, `(1, 0)`
  and `(5, 0)`. A field whose value depends on the *file* rather than the
  *variable* is not a property of the variable.
- The pairs cluster **by level family**, not by id: the `Secret*` levels carry
  `(20, 5915252)` and `(0, 0)`, while `level1`/`level2`/`level3` carry `(13, 1)`.
  `5915252` is `0x005A45B4`, the same value in eight records across separately
  authored maps — which is what stale memory in an editor's serialiser looks
  like, not a unit tag.

An earlier reading had `unk1 = 13, unk2 = 1` on almost every entry with
`GameTicks` / `LevelRating` / `AverageTime` at `(0, 0)`, and read that grouping
as a kind or unit tag. Every word of it is true **of one level file** and false
of the corpus — the `a-sweep-of-keys-is-not-a-sweep-of-values` defect with the
sweep being a single file.

**What is left of Q85:** whether the engine reads slots 2–3 for a plain id. The
setter stubs store one value (`MOV [global], AL|AX|EAX`), so the value slot is
certainly consumed and the other two are certainly not consumed *by the stub*;
whether the record walker looks at them before dispatching is unread. Given they
vary by file for the same id, "read and ignored" and "not read" are the only
live options, and neither changes anything we build.

## Q84: id 66 is the level's player count, and id 77 is still unknown

**Id 66 is in 65 of the 66 levels**, which corrects the question's premise — it
was catalogued as an obscurity and is one of the most consistently present
per-level settings in the game. Values run **2 to 7**.

Tested first against the creature pool's player ids, the only *other* per-level
source that names players: **54 exact agreements, 3 disagreements, 8 levels with
no pool to test against** (`kwd varsweep` prints this).

| level | id 66 | highest pool player |
|---|---|---|
| `level1` | 3 | 3 |
| `Frosty's Castle` | 6 | 6 (keepers 3–6) |
| `Level13`, `level8` | 4 | 3 |
| **`Level17`** | **2** | **5** |

**Now tested against the PLAYERS(180) record, which decides it.** PLAYERS parses
as of ADR-0091 and each record carries a `PlayerId` at offset `0xA8`. Over the
65 levels that have both files, id 66 equals the **highest `PlayerId` in the
level's PLAYERS records on 64**, and equally the **PLAYERS record count on 64**
— the two coincide because a level's player ids run 1..N contiguously. It
matches the count of *keeper* ids (3–7) on **none**.

**STRONGLY INFERRED: id 66 is the level's player count, equivalently the highest
player id in use — including the hero and neutral slots, not just keepers.** The
two levels where it reads one higher than the creature pool are explained: the
pool is a lower bound, since a keeper can exist with no portal roster.

**`Level17` is the single genuine counterexample** and is named rather than
smoothed away: id 66 = 2 against five PLAYERS records with ids 1,2,3,4,5. One
level out of 65 disagreeing is what keeps this STRONGLY INFERRED rather than
CONFIRMED.

**Id 77 stays UNKNOWN.** Nine levels — `Level16` = 29, `Level6b` = 3,
`Secret1` = 1, `Secret2` = 18, `Secret3` = 23, `Secret5` = 11 and three more,
with values also including 4 — and seven different `(unk1, unk2)` pairs. Its
concentration in the `Secret` levels is the only pattern, and one pattern over
nine samples is not a reading.

## The key/value list, for anyone who needs it

Twelve ids take more than one value across the levels that carry them. Every one
is a trap for a sentence of the form *"X is N"*:

| id | variable | values | levels |
|---:|---|---:|---:|
| 3 | `EntranceGenerationSpeedSeconds` | 5 (18, 20, 22, 30, 10000) | 5 |
| 66 | id 66 | 6 (2..7) | 65 |
| 77 | id 77 | 7 | 9 |
| 157 | `BoulderInitialHealth` | 2 (10000, 12000) | 3 |
| 158 | `BoulderSpeedTilesPerSecond` | 2 (4096, 5324) | 2 |
| 210 | `FirstPersonRechargeModifyPercentage` | 3 (25, 50, 70) | 3 |
| 215 | `MusicLevelTwoThreatThreshold` | 7 | 20 |
| 216 | `MusicLevelThreeThreatThreshold` | 9 | 11 |
| 217 | `MusicLevelFourThreatThreshold` | 6 | 9 |
| 218 | `TimeBeforeDungeonHeartConstructionBegins` | 2 (15, 16) | 2 |
| 241 | `LevelRating` | 16 | 66 |
| 242 | `AverageTime` | 15 | 66 |

Only **one** id is present in every level file with a single value, so "the
level files agree on this" is almost never true and should never be assumed.

## Implementation

`DK2.Data.Kwd.KwdVariables`, `DK2.Data.Model.GlobalVariables.MergedWith`
(ADR-0040); the sweep is `dk2tools kwd varsweep`.

## Tests

`DK2.Behavior.Tests.LevelVariablesOnRealDataTests` pins the per-level overrides
that rules depend on, including
`FiveLevelsOverrideThePortalInterval_NotOnlyLevelOne`; the global table's own
values are `DK2.Data.Tests.GameDatabaseTests` and the merge is
`GlobalVariablesMergeTests`.
