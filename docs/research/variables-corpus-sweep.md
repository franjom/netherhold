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

## Q84: ids 66 and 77 are structured records, read one per level by mistake

**Corrected 2026-10-09 (binary and a raw scan of the 66 files).** Both ids carry **several
records per level**, and `KwdVariables` and `kwd varsweep` keep one per id, so every reading
above this line saw only the last. The earlier conclusion, id 66 = the level's player count,
was a reading of that last record: its value is the highest keeper's id.

Each VARIABLES record is dispatched by its id through the table `Variables_InitDispatch`
(`0x0051AA90`) builds. Its entries do not follow table position: the entry with kind 66 is at
`0x006A6180`, and the one with kind 77 at `0x006A6220`.

**Id 66 is an alliance** (`Level_MakeAlliance`, `0x0051DC90`): players `value` and `unk1` become
allies. STRONGLY INFERRED from the handler; what the alliance does is `0x004BF390`'s test. It is
in 65 levels. 63 carry exactly (2, 1) and then (k, 2) for every keeper k from 3 up: heroes with
neutral, and each keeper with neutral. The two exceptions:

- `Level17`: (3, 2), (2, 4), (2, 5), (2, 1).
- `level18`: the usual five, then (5, 4), (6, 4) and (6, 5). Its three computer keepers are
  allied with each other.

Nothing in `src/` applies the alliances (Q576); id 77 is applied by `GameDatabase.ForLevel` (ADR-0491). `kwd varsweep` still prints the old
player-count test.

**Id 77 overrides one field of one creature type for the level** (`0x0051DD10`, no Ghidra
function). The world looks the creature type up by `value` (vtable `+0xC0`); when it exists,
`0x00494A70` stores `unk2` into the definition field selected by `unk1`. The selector is
`CreatureStatId`: 1 Health (`+0x692`), 3 Threat (`+0x698`), 4 MeleeDamage (`+0x5C4`), 21 and 22
the speeds (divided by variable 19), 27 PossessionManaCost (`+0x764`) and so on: 29 selectors, 0–31 less 8, 11 and
14. STRONGLY INFERRED. The shipped records (value = creature, then selector = new value):

| Level | Records |
|---|---|
| `level1` | Lord of the Land (29): MeleeDamage 200, Threat 125 |
| `level4` | 29: Threat 150 |
| `Level6b` | 3: Fear 0 |
| `Spiral` | 4: Fear 0 |
| `Level16` | 13–21, 25, 29: DistanceCanHear 0 |
| `Secret1` | 3: Speed 0, RunSpeed 0; 1: Health 50 |
| `Secret2` | 14–17, 19, 25: Health 200; 13: Health 400; 18: Health 50; 5: Health 3000, Fear 5000 |
| `Secret3` | 16: Health 600; 23: Speed 6144, Fear 16000, RunSpeed 8192 |
| `Secret5` | 1: Health 50; 11: Threat 1 |

The scan reads aligned 16-byte records with id 77, a creature id below 64 and a selector
below 64. `GameDatabase.ForLevel` applies them (ADR-0491). Selector 30 stores whole tiles raw
(`0x00494C56`), where the loader rounds the file's 12.12 value to tiles.

## The key/value list, for anyone who needs it

Twelve ids take more than one value across the levels that carry them. Every one
is a trap for a sentence of the form *"X is N"*:

| id | variable | values | levels |
|---:|---|---:|---:|
| 3 | `EntranceGenerationSpeedSeconds` | 5 (18, 20, 22, 30, 10000) | 5 |
| 66 | alliance (several records a level) | 6 (2..7) | 65 |
| 77 | creature field override (several a level) | 7 | 9 |
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
