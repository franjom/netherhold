# Structure refactor plan

Status (checked 2026-10-15 against HEAD `e5048ced`): **Phase 3 is done**
(`a8ffc392`, 2026-10-06). **Phases 1, 2 and 4 are open**, Phase 5 is optional. Written
2026-09-23; the numbers below were re-measured on 2026-10-15.

> Another session may commit to this repo concurrently. The `HashVersion` and the pinned
> hash literal in `DeterminismTests` move often; read the current ones out of the source
> when you start rather than trusting a number quoted here.

## Why this exists

A read of the workspace gave the impression of an unstructured codebase — "no
namespaces, everything static, all thrown in". Measured against the tree, that
impression is wrong in its specifics and right about one thing:

| Claim | Measured |
|---|---|
| No namespaces | every file declares one, file-scoped and hierarchical |
| Everything static | the rules classes are static and hold **no mutable static fields**; state lives in sealed classes, records and structs |
| No organisation | 11 projects under `src/`, acyclic layering, `Core` depends on nothing |
| All thrown in | **True of one place now**: `DK2.Simulation`'s root (Phase 1) |

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
pins a state hash (`HashVersion 203` on 2026-10-08), so read the current value before
starting. A pure move **must** leave that hash untouched. If it changes, the move was not
pure and something was edited by accident.

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
2. **BOM state is mixed** across `src/`, including the `DK2.Simulation` root. Any tool that *rewrites*
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

**Problem.** 174 `.cs` files sit flat in the `DK2.Simulation` root (63 when this plan was
written), while `Systems/` (81), `Combat/` (13), `Steering/`, `Commands/` and `Navigation/`
are already organised. Organisation was started and never finished. Opening this folder is
what produces the "all thrown in" reaction, and it is a fair reaction. The folder is
growing faster than it is being sorted, so the sooner the better.

**Method.** `git mv` only. No file contents change. Namespace stays
`DK2.Simulation` for every moved file. (`Systems/` and `Combat/` do carry their own
namespaces, `DK2.Simulation.Systems` and `DK2.Simulation.Combat`, from before this plan;
the new folders deliberately do not follow that, to keep call sites untouched.)

### 1a. Two files whose home already exists

These are not a matter of taste — each one's production consumers are already in
the target folder, or beside it:

| Move | Evidence |
|---|---|
| `MeleeDamageRules.cs` → `Combat/` | The only production reference is `Combat/FightRules.cs` (tests aside) |
| `SeparationRules.cs` → `Steering/` | Referenced by `Steering/SeparationBehaviour.cs`, `Steering/SteeringBody.cs` and `Systems/CreatureMovementSystem.cs` |

Do these first as a standalone commit. They are two moves, they are unarguable,
and they confirm the `git mv` + determinism-gate loop works before it is applied
to sixty files.

### 1b. New folders

The lists below are the seed from 2026-09-23; every file named is still in the root.
**Re-derive the lists from the current root before moving**: about 110 files arrived
since (the Casino, Door, Hero, Imp, Keeper and Shot families, among others) and each
belongs in one of these folders or stays in the root.

- **`Triggers/`**: `TriggerExecutor`, `TriggerState`, `TriggerCaptureComparison`,
  `TriggerAreaConditions`, `TriggerConditionEvaluator`, `TriggerReach`,
  `TriggerTurnCsvEmitter`.
- **`Rooms/`**: `RoomRules`, `RoomIndex`, `RoomInstance`, `RoomLog`, `LairRules`,
  `HatcheryRules`, `PrisonRules`, `PortalRules`, `AutoFurnitureRules`,
  `TempleSacrificeRules`, `TempleSacrificeState`, `GraveyardRules`, `GraveyardState`,
  `ResearchRules`, `BridgeRules`, `StructureBlocks`.
- **`Creatures/`**: `CreatureState`, `CreatureAnger`, `CreatureAngerRules`,
  `CreatureDeathRules`, `CreatureHealthRules`, `CreatureLevelRules`, `EatingRules`,
  `JobRules`, `TrainingRules`, `WorkStance`, `AttractionBudget`, `CarryRules`.
- **`Economy/`**: `GoldStore`, `FloorGold`, `ManaRules`, `PaydayRules`, `Efficiency`.
- **`Work/`** (imp labour and the tile-work queue): `TileWork`, `ImpSupplyRules`,
  `ImpCrowding`, `ImpTaskKind`, `DisposalRules`.
- **`Objects/`**: `ThingProjection`, `ObjectCarryRules`, `HandRules`, `ObjectState`,
  `ShotState`.

### 1c. What stays in the root

The project's spine, and it should read as a spine, about a dozen files:

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

**Resolved from history: this is cosmetic residue, and no behaviour is
missing.** The field was named `WorkRemainder`, not `HealthRemainder`. It was deleted
deliberately in `20ea8b0` (2026-09-10, HashVersion 54) because direct measurement refuted
a carried remainder: DK2 computes `tabled x Efficiency` in 4096ths and arithmetic-shifts
down 14, carrying nothing (Q62, Q292, Q296). The deletion removed the field and the tail
of its comment, leaving the opening `<summary>` and two prose lines behind.

**The fix is to delete those three orphaned lines.** Nothing else: no ADR, no
`HashVersion` bump, no simulation change.

Two checks were run when this was written, so it is not fixed in one place and left in its twin:

- The sibling device the deleted comment cited, `CreatureState.MoveRemainder`,
  is **also gone**, removed separately and deliberately in `8c27c6f` (ADR-0157
  phase 3, HashVersion 62) when movement moved onto the steering tick. Both
  remainder accumulators are retired; the tree is consistent.
- A sweep of every file in `src/` for an unclosed `<summary>` preceding another
  found **exactly one** — this one. It is isolated residue, not a pattern.

---

## Phase 3 — Decompose `src/DK2.Tools/Program.cs` — DONE

Commit `a8ffc392` (2026-10-06) split it into a command registry and per-family command
files under `src/DK2.Tools/Commands/`; `Program.cs` is now about 185 lines. Nothing further
is planned here.

---

## Phase 4 — Split `WorldSnapshot.Write`

`src/DK2.Simulation/WorldSnapshot.cs:105` — `public static void Write(Stream,
GameSimulation)` is now about 650 lines (355 when this plan was written) and the
longest method in the repo by a wide margin, an outlier rather than a house style.

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

**Problem.** `DK2.Simulation` is heavily commented (28% comment lines on 2026-09-23). Individual
XML doc blocks run to 20+ lines of reverse-engineering findings: evidence,
confidence levels, dated owner confirmations, question numbers.

**This is the least urgent item and the easiest to get wrong.** The content is
the most valuable thing in the repo, the citation convention already exists (most
source files cite `docs/research/*.md`), and "recovered
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

- **The static rules classes.** They hold no mutable state and
  are pure functions over passed-in state (`GoldStore.Capacity(GameDatabase, in
  TileState)`). For a simulation whose headline contract is bit-identical
  determinism, a stateless functional core is the right design. Do not convert
  them to instance services to satisfy a DI reflex.
- **Few interfaces.** Abstractions exist exactly where something is
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

Phases 1, 2 and 4 are roughly a day together and carry a machine-checkable proof of no
behaviour change. Phase 5 is open-ended and should only be started with a specific file in
hand. Phase 1 alone removes most of the impression that prompted this plan.
