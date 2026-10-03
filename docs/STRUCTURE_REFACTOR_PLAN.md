# Structure refactor plan

Status: proposed, not started. Written 2026-09-23 from a structural review of the
tree at `f0cc96e`.

> Another session commits to this repo concurrently — HEAD moved twice during the
> review that produced this plan. Every structural finding below was re-verified
> at `f0cc96e`. The `HashVersion` and pinned-hash literals move often; read the
> current ones out of the source when you start rather than trusting a number
> quoted here.

## Why this exists

A read of the workspace gave the impression of an unstructured codebase — "no
namespaces, everything static, all thrown in". Measured against the tree, that
impression is wrong in its specifics and right about one thing:

| Claim | Measured |
|---|---|
| No namespaces | 381 of 383 files declare one, file-scoped and hierarchical |
| Everything static | 114 static classes vs 138 sealed + 100 records + 53 structs; **zero** static mutable fields |
| No organisation | 11 projects, acyclic layering, `Core` depends on nothing |
| All thrown in | **True of two places**, listed below |

The architecture is sound and this plan does not touch it. What it fixes is
*where files sit*, which is what produced the impression — plus one real defect
found on the way.

The work is ordered so that the largest visible improvement (Phase 1) is also
the safest: it is a pure file move with a machine-checkable proof that nothing
changed.

## The safety property

`DK2.Simulation` sets no `IDE0130` / `dotnet_style_namespace_match_folder` rule,
and `.editorconfig` is deliberately narrow. Folder layout is therefore
**independent of namespace** in this repo. Every `.csproj` uses SDK globbing with
no explicit `<Compile>` items, so moving a file changes no project file.

Consequence: Phase 1 can be done with `git mv` alone — no edit to any file's
bytes, no namespace change, no `using` change at any call site.

The gate that proves it: `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines`
pins a state hash — `0xFD44E939D714F3F1UL` at `HashVersion 76` as of `f0cc96e`,
but read the current value before starting. A pure move **must** leave that hash
untouched. If it changes, the move was not pure and something was edited by
accident.

Because another session commits here, re-read the pinned hash immediately before
the move and compare against that, not against a number copied from this
document. A hash that differs from this file's literal may mean the tree simply
advanced, which is not a failure; a hash that differs from what you read *five
minutes ago on your own branch* is.

---

## Phase 0 — Preconditions

These are repo-specific and have each caused a real mess before. Do not skip.

1. **Another session may be editing this tree.** Check `git log` and file mtimes
   first. Stage named paths only — never `git add -A`.
2. **BOM state is mixed**: 74 of 231 files under `src/` carry a UTF-8 BOM, and in
   the `DK2.Simulation` root it is 29 with, 34 without. Any tool that *rewrites*
   files (an IDE "move to folder" refactor, `dotnet format`, a script that reads
   `utf-8-sig` and writes `utf-8`) will normalise BOMs and bury the real change
   in hundreds of spurious hunks. **`git mv` moves bytes and is the only safe
   mechanism for Phase 1.**
3. **Line endings are LF.** Confirm `git config core.autocrlf` is not rewriting
   the working copy before starting; a merge in this repo has left CRLF copies
   before.
4. Start from a green `dotnet test DK2.slnx` so a later failure is attributable.

---

## Phase 1 — Give `DK2.Simulation` its folders

**Problem.** 63 of the project's 125 files sit flat in the project root, 13,320
lines of them, while `Systems/` (34), `Steering/` (10), `Combat/` (10),
`Commands/` (6) and `Navigation/` (2) are already organised. Organisation was
started and never finished. Opening this folder is what produces the "all thrown
in" reaction, and it is a fair reaction.

**Method.** `git mv` only. No file contents change. Namespace stays
`DK2.Simulation` for every moved file.

### 1a. Two files whose home already exists

These are not a matter of taste — each one's only consumers are already inside
the target folder:

| Move | Evidence |
|---|---|
| `MeleeDamageRules.cs` → `Combat/` | Referenced only by `Combat/FightRules.cs` |
| `SeparationRules.cs` → `Steering/` | Referenced only by `Steering/SeparationBehaviour.cs` and `Steering/SteeringBody.cs` |

Do these first as a standalone commit. They are two moves, they are unarguable,
and they confirm the `git mv` + determinism-gate loop works before it is applied
to sixty files.

### 1b. New folders

**`Triggers/` — 7 files, ~2,246 lines.** The single most cohesive cluster in the
root, and 17% of its lines.

`TriggerExecutor` · `TriggerState` · `TriggerCaptureComparison` ·
`TriggerAreaConditions` · `TriggerConditionEvaluator` · `TriggerReach` ·
`TriggerTurnCsvEmitter`

**`Rooms/` — 16 files, ~3,032 lines.**

`RoomRules` · `RoomIndex` · `RoomInstance` · `RoomLog` · `LairRules` ·
`HatcheryRules` · `PrisonRules` · `PortalRules` · `AutoFurnitureRules` ·
`TempleSacrificeRules` · `TempleSacrificeState` · `GraveyardRules` ·
`GraveyardState` · `ResearchRules` · `BridgeRules` · `StructureBlocks`

**`Creatures/` — 12 files, ~2,427 lines.**

`CreatureState` · `CreatureAnger` · `CreatureAngerRules` · `CreatureDeathRules` ·
`CreatureHealthRules` · `CreatureLevelRules` · `EatingRules` · `JobRules` ·
`TrainingRules` · `WorkStance` · `AttractionBudget` · `CarryRules`

**`Economy/` — 5 files, ~741 lines.**

`GoldStore` · `FloorGold` · `ManaRules` · `PaydayRules` · `Efficiency`

**`Work/` — 5 files, ~861 lines.** Imp labour and the tile-work queue.

`TileWork` · `ImpSupplyRules` · `ImpCrowding` · `ImpTaskKind` · `DisposalRules`

**`Objects/` — 5 files, ~754 lines.**

`ThingProjection` · `ObjectCarryRules` · `HandRules` · `ObjectState` · `ShotState`

### 1c. What stays in the root

The project's spine, and it should read as a spine — roughly 12 files:

`GameSimulation` · `WorldState` · `WorldSnapshot` · `ISimulationSystem` ·
`SimulationEvents` · `SimulationTiming` · `InputFrame` · `InputLog` ·
`PlayerStatus` · `ObservedCorrections` · `WorldPathGrid`

### Verification for Phase 1

```
git status          # every line must read "renamed:", never "modified:"
dotnet build DK2.slnx
dotnet test DK2.slnx
```

`git status` showing `modified:` for a moved file means its bytes changed —
almost certainly a BOM or line-ending normalisation. Reset and redo that file
with `git mv`.

The pinned state hash must be unchanged and `HashVersion` must not move. No ADR
is needed: nothing about the simulation's definition changed.

Commit per folder, not per file. Seven commits, each trivially reviewable as a
rename list.

---

## Phase 2 — Fix the orphaned doc block in `WorldState.cs`

A real defect, currently committed, at `src/DK2.Simulation/WorldState.cs:96-100`:

```csharp
/// <summary>
/// Health owed to this tile that was too small to apply, carried between
/// applications; always smaller in magnitude than
/// <summary>
/// Ticks left before this tile burns away, or 0 when nothing is burning.
```

An unterminated `<summary>` swallows the entire doc comment belonging to
`BurnTicksRemaining`, which follows it.

**Resolved from history — this is cosmetic residue, and no behaviour is
missing.** The field was named `WorkRemainder`, not `HealthRemainder`, which is
why searching the tree for the latter finds nothing. It was deleted deliberately
in `20ea8b0` (2026-09-10, *"One application is a shift, not a division, and
nothing is carried"*, HashVersion 54), which also removed
`EfficiencyRules.Scale` and the field's `hash.Add` line.

The carried remainder was not lost — it was **refuted by direct measurement**.
Run 15 sampled the running game's tile health 20 times a second and recorded 54
consecutive reinforce steps of *exactly* 9 against a tabled 9.6. DK2 carries
nothing; it computes `tabled × Efficiency` in 4096ths and arithmetic-shifts down
14, and a signed shift floors. That one mechanism corrected four work rates at
once and closed Q62, Q292 and Q296.

The orphan is a three-line-short deletion: the diff removed the field and the
tail of its comment from the `<see cref="SimulationTiming.BaseApplicationDivisor"/>`
line onward, leaving the opening `<summary>` and two prose lines behind.

**The fix is to delete those three orphaned lines.** Nothing else. No ADR, no
`HashVersion` bump, no simulation change — deleting them restores
`BurnTicksRemaining`'s documentation and touches no code.

Two checks were run so this is not fixed in one place and left in its twin:

- The sibling device the deleted comment cited, `CreatureState.MoveRemainder`,
  is **also gone**, removed separately and deliberately in `8c27c6f` (ADR-0157
  phase 3, HashVersion 62) when movement moved onto the steering tick. Both
  remainder accumulators are retired; the tree is consistent.
- A sweep of every file in `src/` for an unclosed `<summary>` preceding another
  found **exactly one** — this one. It is isolated residue, not a pattern.

---

## Phase 3 — Decompose `src/DK2.Tools/Program.cs`

**Problem.** 3,733 lines, 180 KB, in top-level statements. One `switch` with 49
command arms, most bodies inlined at 20–160 lines, followed by ~30 trailing local
static functions. It is four times the next largest source file in the repo.

**The pattern already exists.** The `sgmap` arm is one line:

```csharp
case "sgmap":
    return SgmapCommand.Run(args[1..], FindRepoRoot());
```

`Sgmap.cs` is a `public static class SgmapCommand` with a `Run(string[], string?,
TextWriter?, TextWriter?)` entry point, injectable output and error writers, and
a private `Usage`. That is the target shape. This phase is not a new design — it
is applying the repo's own existing one to the other 48 arms.

Note the injected `TextWriter`s: they are why `SgmapCommand` is testable and the
inlined arms are not. Extraction should preserve that signature rather than
writing to `Console` directly.

### Ordering

`kwd` is 33 of the 49 arms and spans lines 721–2586 — roughly 1,865 lines, half
the file. Take it in slices rather than in one commit.

**3a. The self-contained non-`kwd` arms first** — they are independent and prove
the loop:

| Command | Lines | Target |
|---|---|---|
| `docs` | ~214 | `Commands/DocsCommand.cs` |
| `status` | ~176 | `Commands/StatusCommand.cs` |
| `coverage` | ~142 | `Commands/CoverageCommand.cs` |
| `sdt` | ~56 | `Commands/SdtCommand.cs` |
| `convert` | ~32 | `Commands/ConvertCommand.cs` |
| `str` | ~26 | `Commands/StrCommand.cs` |
| `wad` | ~20 | `Commands/WadCommand.cs` |

`status` and `docs` deserve care: `status --check` gates the plan's committed
figures, and `docs --check` covers unstaged files. Both are load-bearing for
other workflows. Extract them without changing behaviour, and run each with
`--check` before and after to confirm identical output.

**3b. `kwd` into a sub-dispatcher.** `Commands/Kwd/KwdCommand.cs` takes
`args[1..]` and dispatches the 33 subcommands, each in its own file under
`Commands/Kwd/`. The largest justify their own file on size alone:

`unreached` (161) · `portals` (118) · `alterterrain` (112) · `fieldsweep` (105) ·
`varsweep` (102) · `vars` (97) · `things` (84) · `objects` (83) · `creatures` (79) ·
`creaturebits` (74) · `creaturescan` (69)

The remaining 22 are 12–58 lines each and can be grouped by subject —
`KwdTerrainCommands`, `KwdTriggerCommands`, `KwdCreatureCommands`,
`KwdRecordCommands` — rather than scattered into 22 near-empty files.

**3c. The shared helpers.** The ~30 trailing local functions (`FindRepoRoot`,
`RunGit`, `ChangedFiles`, `StagedFiles`, `GitLines`, `AddedLineNumbers`,
`OptionValue`, `ResolveData`, `ResolveText`, …) become an internal static
support class — `Commands/ToolContext.cs` or similar. Several are pure git
plumbing and could reasonably be their own `GitQueries` type.

**End state.** `Program.cs` is a dispatch table of one-line arms plus `Usage()`,
on the order of 150 lines.

### Verification for Phase 3

Unlike Phase 1, this phase **does** change file contents, so the determinism hash
is not the gate — `DK2.Tools` is not in the simulation. The gate is behavioural:

- `dotnet test DK2.slnx` green throughout (`DK2.Tools.Tests` exists but is one
  file, 132 lines — thin cover for 3,733 lines, so tests alone are not enough).
- For each extracted command, capture its output before and after on the same
  inputs and diff. This is the only real check on most arms.
- `dk2tools status --check` and `dk2tools docs --check` must still agree with the
  committed files.

Extraction is mechanical, but 49 arms is 49 chances to drop a `when` clause on an
argument count. Move one command per commit; the diff should be a pure cut-paste
plus a `using`.

---

## Phase 4 — Split `WorldSnapshot.Write`

`src/DK2.Simulation/WorldSnapshot.cs:97` — `public static void Write(Stream,
GameSimulation)` is 355 lines, twice the next longest method in the repo
(`KwdCreatures.ParseRecord`, 170). Only 7 of 762 measured methods exceed 100
lines, so this is an outlier rather than a house style.

It is a linear serialiser, so it is low-risk and low-urgency — the length is
inherent to the number of fields, not to complexity. Split per record type
(tiles, creatures, objects, rooms, triggers, players) into private static
`WriteX(BinaryWriter, …)` methods, matching however `Read` is already structured.

This touches the simulation. The write order must not change; if it does, the
snapshot format changes and `WorldSnapshot.FormatVersion` must bump. Assert the
byte-for-byte output is identical before and after — the existing `SnapshotTests`
round-trip is necessary but not sufficient, because a round-trip stays green if
`Read` and `Write` change symmetrically.

Do this after Phase 1, so the file is already in its final location.

---

## Phase 5 — Move research provenance out of XML docs (optional)

**Problem.** `DK2.Simulation` is 28% comment lines — 6,956 of 25,241. Individual
XML doc blocks run to 20+ lines of reverse-engineering findings: evidence,
confidence levels, dated owner confirmations, question numbers.

**This is the least urgent item and the easiest to get wrong.** The content is
the most valuable thing in the repo, the citation convention already exists (149
files cite `docs/research/*.md`, across 102 research documents), and "recovered
but not written down is lost" is a lesson this project has already paid for.

The defensible version is narrow: where an XML doc restates at length what its
cited `docs/research/` file already says, shorten the comment to the finding plus
the citation and let the document carry the argument. Where there is **no** cited
document, write the document first — never delete the only copy.

Do not treat a comment-line count as the target. A lower percentage achieved by
deleting provenance is a loss, not a win.

---

## Explicitly not changing

A future pass — human or agent — may be tempted by these. Each was examined and
is correct as it stands:

- **The static rules classes.** 114 of them, but they hold no mutable state and
  are pure functions over passed-in state (`GoldStore.Capacity(GameDatabase, in
  TileState)`). For a simulation whose headline contract is bit-identical
  determinism, a stateless functional core is the right design. Do not convert
  them to instance services to satisfy a DI reflex.
- **Only 10 interfaces.** Abstractions exist exactly where something is
  genuinely substituted — `ISimulationSystem`, `IPathfinder`, `IAssetProvider`,
  `IRenderTarget`, `ISimulationEventSink`. Interfaces with one implementation and
  no substitution point would be noise.
- **Namespaces.** Already correct and hierarchical. Phase 1 deliberately does
  *not* align them to the new folders, because that would turn a zero-risk move
  into a large call-site diff for no benefit.
- **The `Directory.Build.props` and `.editorconfig` comments.** They record why
  an obvious approach was rejected and what it cost. They are long on purpose.
- **`TreatWarningsAsErrors`, `Nullable`, `Deterministic`, `InvariantGlobalization`.**

## Effort

Phases 1, 2 and 4 are roughly a day together and carry a machine-checkable proof
of no behaviour change. Phase 3 is the larger piece — mechanical but wide, best
spread over several sessions at one command per commit. Phase 5 is open-ended and
should only be started with a specific file in hand.

Phase 1 alone removes most of the impression that prompted this plan.
