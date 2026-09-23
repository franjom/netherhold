# Handoff notes

Working knowledge that would otherwise live only in chat history (`AGENTS.md`,
"Session handoff"). It is a reference, not a log: state the current fact; git has
the history. Budget 500 lines; edit the section a fact belongs to, do not append
a sitting.

---

## Current state

### Counts — never hand-maintained; run these

| Figure | Command |
|---|---|
| Tests (and how many skip without an install) | `dotnet test DK2.slnx -c Release`; `DK2_DIR=/nonexistent dotnet test DK2.slnx` is what CI sees |
| ADRs | `grep -c '^## ADR-' docs/DECISIONS.md` — not in numeric order; 0093–0099 stay reserved (ADR-0100) |
| Questions | `grep -oE '^\| \*{0,2}Q[0-9]+' docs/research/open-questions.md \| grep -oE '[0-9]+' \| sort -n \| tail -1` — rows are neither ordered nor uniformly formatted |
| `HashVersion` / `FormatVersion` | `grep -n 'HashVersion = ' src/DK2.Simulation/GameSimulation.cs; grep -n 'FormatVersion = ' src/DK2.Simulation/WorldSnapshot.cs` (kept equal) |
| Pinned state hash | `grep -n 'Assert.Equal(0x' tests/DK2.Simulation.Tests/DeterminismTests.cs` |
| Scenarios, research notes | `find scenarios -name '*.yaml' \| wc -l`; `ls docs/research/*.md \| wc -l` |
| KWD and TRIGGERS coverage | `dk2tools kwd corpus`, `dk2tools kwd trigger-types` (need an install) |
| What is built vs research only | `dk2tools coverage [--all] [--check]` |
| The plan's status paragraph | `dk2tools status` regenerates it (and the activity counts in `ARCHITECTURE.md`, `GOAL_ALIGNMENT.md`, `creature-state-table.md`); run it **after** the tests, from a Release build |

### Milestones

The plan's §8 table is the record (revision 2, 2026-09-21: M6, then combat and the
keeper AI ahead of breadth; M12 keeper AI, M13 skirmish, M14 multiplayer added).
M0–M4 closed. **M5** has a first clean comparison: two levels' idle trigger events
match a live capture exactly once the probe's one-tick lag is applied (ADR-0117,
ADR-0119, `docs/experiments/`). **M6** is partial: every THINGS record reaches the
world and eight trigger action types execute, no level is winnable. **M7** is
partial: combat is built (ADR-0163–0170), heroes, traps and door behaviour are not.
**M12** is researched (`computer-keeper-*.md`) and unbuilt.

### What exists (present tense; the ADR is the specification)

- **Tiles and work.** Dig, mine, claim, reinforce, repair, attack, convert-room from terrain flags (`TileWorkRules`, ADR-0009); work lands once a tick, `delta × efficiency ÷ 4` (ADR-0089); upward jobs cost the deficit; a dug tile never closes and fill-in is not a DK2 action (Q219); hostile expansion is direct claiming (ADR-0033); a tile nobody can own has no owner (ADR-0045). Stance and crowding: ADR-0026/0028/0030.
- **Rooms.** Derived instances (`RoomInstance`, ADR-0027); placement from placeable-on flags; sell at 50 % (ADR-0029); bridges (ADR-0031/0032); claimed whole (ADR-0044). Behaviour for Portal, Lair, Hatchery, Prison, Treasury and Heart (gold), Training Room, Library (accrual only), Temple prayer and sacrifice, Graveyard (ADRs 0042–0090, 0147–0151). Auto-placed furniture and the per-type attraction budget (ADR-0147).
- **Gold and mana.** Gold lives in tiles and hands, no player counter (ADR-0015/0019); floor gold is an object (ADR-0055/0056). Mana ceiling 200000 flat, income cap 500/s (ADR-0017). Free imps as a deadline (ADR-0057); Create Imp price and 7 mana/s upkeep (ADR-0021/0022).
- **Imps.** Two-tier job choice (ADR-0058); carrying bodies, captives and objects (ADR-0063–0065); aimed bonus (ADR-0069).
- **Creatures.** Health and hunger, own bed, eating, levels and experience through the loader divide (ADR-0083), anger as six banded counters saturating at 65533 (ADR-0081/0087), desertion, the 60 s dying window (ADR-0051), payday as a journey, three mood choosers on a five-second gate (ADR-0082), Hand of Evil (ADR-0062/0075/0077, disposal ADR-0148), knock-out at a sixth (ADR-0071), hazard damage. `Dk2StateId`, `StateBeforeUtilityState`, `UtilityNewState` are hashed fields written by nothing (ADR-0135 step one).
- **Combat** (ADR-0163–0170; `combat-*.md`, `shots.md`, `creature-status-effects.md`, `spell-friendly-arm.md`). `SelectAVictim` from `ProcessFight`: the dispatch scan for all five fight styles, the heart (scan E, its ring tile standing in for the Arch Pillar, Q363) and the ally scan F; the fight state with its two clocks and `MeleeDamageRules`; the struck creature's answer; fear, back-off, flight and withdrawal; the worker's arm and the alarm; attack spells as action 16 firing shot things; the friendly arm; status effects as countdown words; state 8's approach; a fixed 8-tick victory pause (Q376) and the corpse walk. Scans A, B (thing-targeting spells), C and D are not built (2026-09-23).
- **Win and defeat** (ADR-0162). `Status` written once, heart health, ruin and killer, actions 10/11, last keeper standing; a ruined heart is claimable and stores no gold. Alliances are symmetric state (`AllianceSystem`, ADR-0159).
- **Navigation** (ADR-0155/0157/0158). DK2's mesh router, funnel and four steering forces drive every creature; doors are in the navigation byte and `DoorSystem` opens, closes, locks and picks them (ADR-0159).
- **Players.** `PlayerIds` 0 unowned, 1 heroes, 2 neutral, 3–7 keepers (ADR-0043); only a keeper is staffed (ADR-0052). `DK2.AI.KeeperAI` is an input source emitting `PlayerCommand`s (`KEEPER_AI.md` §12.7 step 1).
- **World.** Fog of war as simulation state, one bit per keeper in ours (ADR-0076); levels on their own VARIABLES (ADR-0040); FNV-1a state hash with one pin and `ChaosSystem` (ADR-0006).
- **A level's THINGS and triggers.** `ThingProjection.Place` keeps every record in file order (ADR-0101/0103/0106/0108/0139); `TriggerExecutor` walks the five root kinds once a turn, performs actions 1, 6, 7, 8, 10, 11, 14, 22, 44, 45 and marks the rest `Partial` or `Unhandled` (ADR-0107/0152/0160–0162); conditions cover the world arms, creature 36/37/49, player counters and the area arms (`trigger-*.md`).
- **Data and presentation.** WAD, KWD/KLD (18 record types), STR, SDT, KMF, textures, `.SAV` tile grid; `DK2.Convert` writes glTF/GLB, PNG and WAV. `WorldView` is the one translation to drawables; Unity 6 LTS `6000.6.0f1` renders campaign level 1 as boxes (ADR-0034–0036/0104).

### What does not exist

**The creature state machine is researched and not wired** (2026-09-23): 281 states,
32 categories, the transition rule and all 36 category hook bodies are read
(`creature-state-table.md`, `state-categories.md`, `state-category-hooks.md`); ours
runs on 30 activities assigned directly at 35 sites, with no transition gate and no
category update hook. Also absent: hero-party spawning and effect generators
(Q254–Q259), every keeper spell but Create Imp, trap behaviour and door blocking,
the creature trigger event filter, most trigger action types and 31 of 46 condition
types (`dk2tools trigger-run` prints each stall; ADR-0125), defeat's consequences
(Q358), the research tree (accrual only), Workshop, Casino, Torture, Guard Room and
Combat Pit, DK2's own computer keeper, mentor speech and notifications, the campaign
progress registry, Temple spell intake `0x87`, wandering (Q41), vampire
reincarnation (read, Q329), `Obstacle` furniture, the shuffle speed mode.
Unity: no branch loads the converter's output (Unity ships no runtime glTF
importer); boxes rather than DK2's walls (Q71).

**A claim that something has NOT happened carries a date** (2026-09-15):
`dk2tools docs --check` fails any *never / not yet / awaits / still no* line here
without a `20xx-xx-xx`. Prefer a pointer to a restatement.

### Standing caveats

- **One deliberate departure from shipped data:** `DigRockHealth` −5000 → −500, the owner having measured two swings (ADR-0023, Q20). `ObservedCorrections` is the only place such a number may live.
- **The portal tally is never zeroed** (ADR-0086; DK2's reset is vtable-only and unread as of 2026-09-08). A worn portal lost and re-claimed is the missing observation.
- **The pin's world holds two creatures but registers only `ChaosSystem`** (Q206): `HashInto` is guarded, no creature system is.
- **A trigger timer's unit depends on where you read it**: the `.kld` holds seconds, the loader multiplies by variable 19 at load, the comparison consumes ticks (ADR-0102 amendments). Ours converts in `HasElapsed`; a `.kld` trigger loader must convert exactly once.
- **Install-gated tests skip, they do not pass** (ADR-0100): `[InstallFact]`/`[InstallTheory]`; `DK2_REQUIRE_INSTALL=1` turns a missing install into a failure; CI sets `DK2_DIR: /nonexistent`.
- **Simulation speed has never been measured** (still true 2026-09-23; goal 9).
- **Timing is DK2's own**: `TicksPerSecond = 4`, observed at 3.94 ticks/s live (ADR-0089/0102). `TicksPerSecond` (ours) and `OriginalTicksPerGameSecond` (a fact) are equal and kept separate.
- **Reference install:** `E:\GogGames\Dungeon Keeper 2` (GOG; `DKII.EXE` MD5 `2f3bbc21eae8c3aa468136baa303036d`, v1.70 STRONGLY INFERRED, Q10). `DK2_DIR` is authoritative when set; an invalid one means no install.

---

## Where the code lives

`docs/ARCHITECTURE.md` has the layering, the system order and the per-project
type lists; the tree is the truth for names. In one line: `DK2.Core` ←
`DK2.Data.Model` ← `DK2.Data` / `DK2.Simulation` ← `DK2.Pathfinding`, `DK2.Assets`,
`DK2.Presentation`, `DK2.AI`, `DK2.Convert` ← `DK2.Harness`, `DK2.Tools`, Unity.
Eight projects import `src/Shared/UnityBound.props` and build netstandard2.1
(ADR-0002, enforced by `ArchitectureTests`).

| Project | Holds |
|---|---|
| `src/DK2.Core` | primitives: `Fp32`, `GameTick`, `TilePos`, `Hash64`, `SimRandom`, `Confidence` |
| `src/DK2.Data.Model` | immutable definitions, no IO; `GameDatabase` (`this[VariableTypeId]` **throws** on a missing variable) |
| `src/DK2.Data` | readers for the shipped files (`Kwd/`, `Wad/`, `Save/`, `Text/`), `Dk2Install` |
| `src/DK2.Simulation` | all rules and mutable state; `Systems/` one class each, order declared once in `SimulationSystems.Default()`; `Combat/`, `Steering/`, `Navigation/`, `Commands/` |
| `src/DK2.Pathfinding` | the original's router (`NavMesh`, `MeshRouter`, `Funnel`); `GridPathfinder` survives as the work chooser's `TryFindNearest` |
| `src/DK2.Assets`, `src/DK2.Presentation` | logical asset keys and the provider chain; `WorldView`, `PlayerInputTranslator`, `IRenderTarget` (the Unity seam) |
| `src/DK2.AI` | the computer keeper as an input source |
| `src/DK2.Convert`, `src/DK2.Harness`, `src/DK2.Tools` | original art → modern formats; headless scenario runner; the `dk2tools` CLI (one file) |
| `unity/DK2.Unity`, `unity/DK2.Unity.Compile` | the adapter, and a project that compiles it against Unity's module assemblies (outside `DK2.slnx`) |

Tests: `tests/DK2.Simulation.Tests` one file per mechanic, `DeterminismTests` holds
the pin, `Dk2TerrainFixture` carries DK2's real numbers so rules test in CI;
`tests/DK2.Behavior.Tests` runs the same rules against the install (`*OnRealDataTests`
drift guards, every scenario, `CampaignLoadTests`); `tests/DK2.Data.Tests` pins every
record stride; `tests/DK2.Core.Tests` holds the licence and Flame-identifier guards;
`tests/DK2.TestSupport` the install gate.

Reference material: `docs/DECISIONS.md`, `docs/research/*.md` (one note per mechanic;
runtime layouts indexed by `debug-dumpers.md`), `open-questions.md`,
`observation-checklist.md` (what the owner can settle by playing),
`docs/RESEARCH-QUEUE.md` (binary reads and the Ghidra command), `RESEARCH_STATUS.md`,
`EXPERIMENTS.md`, `KEEPER_AI.md`, `GOAL_ALIGNMENT.md`, `CODEX_TASKS.md`,
`reverse-engineering/README.md` (the only tracked file there), `original-reference/README.md`,
`unity/README.md`, `THIRD-PARTY-NOTICES.md`.

---

## Running things

```
dotnet build DK2.slnx -c Release                 # one solution; `dotnet build` takes one project, not several
dotnet test  DK2.slnx -c Release --no-build
DK2_DIR=/nonexistent dotnet test DK2.slnx        # what CI sees
dotnet run --project src/DK2.Harness -- run scenarios/smoke/determinism_flat_map.yaml [--out <dir>]
```

Harness output lands in `build/scenario-runs/<id>/` (gitignored). Scenario format:
`scenarios/README.md`; `setup.placed_things: true` places a level's THINGS (ADR-0101).

**`dk2tools`** = `src/DK2.Tools/bin/Release/net10.0/DK2.Tools.exe` (or `dotnet run
--project src/DK2.Tools --`). Run it with no arguments for the command list. The
usage text omits the table dumps: `kwd terrain|terrainflags|terrainmana|rooms|roomflags|creatures|creature <name>|objects|research|map|mapbytes|tagged|creaturebits|fieldsweep|varsweep`.
`kwd creature <name>` prints a `raw:` line per `Fp32` — transcribe that, never the decimal.

**Unity** (nothing under `unity/` is in `DK2.slnx`; CI has no Unity):

```
powershell -File unity/sync-assemblies.ps1                    # after ANY change under src/
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj  # -p:UnityManagedDir=... if the editor is elsewhere
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics -projectPath unity\DK2.Unity -logFile build\unity-smoke.log -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest
```

The smoke test loads campaign level 1 through Unity's Mono runtime and fails unless
there is one GameObject per tile; it exits 0 with no install. Interactive use and
controls: `unity/README.md`.

**Ghidra** (headless, 11.0.1 at `D:/ghidra_11.0.1_PUBLIC`, JDK 26): the command is
in `docs/RESEARCH-QUEUE.md`. Two projects exist and must not be shared between
concurrent runs: `D:/ghidra projects/DK2_RE` (Codex) and `D:/ghidra projects claude/DK2_RE`
(this session). Runs on one project are sequential. Scripts (`DisasmMulti.py`,
`InsGrep.py`, `ScanAll.py`, `CallScanAll.py`, `ReadsAt.py`, …) and exports live in
`reverse-engineering/` on this machine only; `git status --short reverse-engineering/`
must be empty before every commit.

**Live memory reader:** `python reverse-engineering/dk2watch.py --seconds 300 --csv D:\run.csv`
attaches to `DKII-dx.EXE` or `DKII.EXE` with `PROCESS_VM_READ` (the two differ in no
code or data byte). Load the level first; pass `--delay 0` when the probe waits at
the front end. `--tags <out.csv>` diffs the player task pool into a command log
(`player-task-pool.md`); `dk2move.py` logs creature positions and switches on DK2's
own `nav.log`; the unattended recipe is `navigation-captures.md`, and DK2's
`-PSAVE`/`-PLOAD` replays a played session. **A capture's turn is one tick late**
(ADR-0119); a stored timer stamp needs no correction. The campaign registry key that
unlocks levels is `campaign-progress-registry.md` (160 bytes, one dword per map).
Level 20 is quiet for only about seven minutes (Q275).

**Reading a condition arm:** `DispatchMap.py` prints `index + 1`; the true type is
printed + base − 1 (`trigger-dispatch-tables.md`). Get the walker's context first
(`dk2tools trigger-run`), then check the shipped parameter distribution
(`kwd trigger-params`) before implementing.

**Plan line:** `dk2tools status` output through Bash redirection loses non-ASCII;
capture it in PowerShell with `Out-File -Encoding utf8`.

---

## Next steps

Order as of 2026-09-23, from the plan's §8 Run column: M6 and M7 first, the keeper
AI (M12) ahead of breadth.

1. **The heart and defeat.** Q363: how a blow on an Arch Pillar (object 94) reaches the heart object's health (`0x004B2CE0`'s object arm, `0x0049B5E0`'s type-13 arm); nothing places pillars yet (2026-09-23). Q358: what a defeat does (`0x004DAA10`, `0x004DAE40`). Q359: alliance propagation.
2. **Heroes.** Hero parties and action type 14 spawning (Q254–Q259), the hero gate; heroes can now fight.
3. **Combat's leftovers.** Q376 (the celebration animation's length, KMF frame counts), Q401 (does Haste sometimes miss?), Q395's unread shot handlers and the Guided Bolt, the thing-targeting spells (scan B), scans A/C/D, Q337's possession scalar, Q335 resistance.
4. **M6 triggers.** `kwd unreached` ranks remaining action effects; types 17 and 24 rank first and are parked (Q309, Q227). Door and trap behaviour (Q249–Q253); effect generators (Q254–Q259). Q220/Q221 are the two open record layouts.
5. **Rooms without behaviour:** Workshop (category 5 hook), Casino, Torture and conversion (ADR-0085), Guard Room, Combat Pit (its loser is knocked out, not killed — Q310 — so it needs no death path), Temple spell intake `0x87`. Vampire reincarnation is read (Q111) and waits on Q329.
6. **Rebel** is under construction in the other session on 2026-09-23 (`rebellion.md`, `RebellionRules.cs`, uncommitted); until it lands the three `Rebel*Percentage` variables are read by nothing.
7. **Computer keeper (M12).** DK2's own is read: entry, `Task` dispatcher, all seven handlers, the room planner, `RespondToAttack` (`computer-keeper-tasks.md`, `computer-keeper-handlers.md`). Next: compare `DK2.AI.KeeperAI` against it (`KEEPER_AI.md` §12.7 steps 2–5); §12.8's four deviations each want an ADR if they survive.
8. **The creature state machine.** Wire what is read: a transition gate over the 35 direct `Activity` assignments, the category update hook (prison drain, training, research, manufacture), and the travelling activities collapsing onto the utility-state pair (ADR-0135). Q402 asked whether hook divergences should be corrected; ADR-0171 corrected two.
9. **M5.** A scenario that cannot assert says why in its file (ADR-0150); runs are under `docs/experiments/` and the gitignored `reverse-engineering/dk2runs/` (tag captures 9–14 were not kept; `EXPERIMENTS.md` is their record).
10. **Unity:** load the converter's output; DK2's wall pieces (Q71/Q72, `wall-rendering.md`).

**Known "ours, not DK2's", each flagged at its site**

- Treasury deposit order is tile-index order (`GoldStore`; Q35's withdrawal half unread).
- Which lair tile a bedless creature walks to (Q157).
- A nearer imp does not take a task from a farther one (Q155; DK2 does).
- `ManaSystem` runs for every player, not only keepers (ADR-0052).
- `RoomRules` does not model a level's per-room `Availability`; only `PortalRules` reads availability (ADR-0040).
- Combat: the heart's ring tile stands in for the Arch Pillar (Q363); the victory pause is 8 ticks (Q376); the Flanker offset uses the target's velocity (Q400); haste rescales the speed cap each tick where DK2 pins once.

**Housekeeping**

- No `README.md` at the repository root (2026-09-23).
- Scenario tick budgets were written against 20 ticks/s; a budget that is too long is cosmetic, an experiment that no longer runs is not (Q316, ADR-0150).
- Re-run the zero-value audit (ADR-0085) whenever a record type gains a reader; done for the balance table, creatures and objects (Q213), not terrain or rooms.

---

## Decisions already made — do not relitigate

`docs/DECISIONS.md` is the index and the argument; where a later ADR reverses an
earlier one, the earlier entry says so. Standing owner rulings that are not ADRs and
are CONFIRMED from the original: a keeper has at most one dungeon heart and losing it
is defeat (Q40); the mana ceiling is 200000 from the first second (Q37); imps gain
speed with level (Q118c/Q119); the room the editor labels "Hero Portal" is the
Mercenary Portal (Q211); a Combat Pit loser is knocked out, never killed (Q310); a
captive is not taken through a portal (2026-09-13).

---

## Conventions that are easy to violate by accident

- **A balance constant hardcoded in simulation code is a bug** — read it from `GameDatabase`. The indexer **throws** on a missing variable, so a new reader breaks every synthetic database until the variable is added to `TestWorld.Variables()`, `Dk2TerrainFixture.Variables()` and `ScenarioTests.Variables()`. Add the entry; never default the lookup. The one exception is `ObservedCorrections` (ADR-0023).
- **A number that does not match how the original plays is not licence to change it.** Reproduce the data, record the discrepancy as a question, let the output show it.
- **Changing simulation state breaks the pinned hash — that is the design.** Bump `HashVersion`, keep `FormatVersion` equal, update the literal in `DeterminismTests` in the same commit, add a row to `hash-versions.md`. Never loosen the assertion. A field left out of `HashInto` is unguarded, population or not.
- **New state must be reachable from `WorldState`** — `HashInto` *and* `WorldSnapshot` in the same commit, or it is silently non-deterministic and lost on load.
- **Nothing in the simulation may read an event back.** `SimulationEvents` is write-only.
- **Ticks relate to seconds only in `SimulationTiming`.** Rates from a creature record go through `LoaderRatePerTick` (integer divide by 4, floored at 1).
- **Ask flags, never ids.** A rule keyed on a terrain, room or creature id is a rule about the reference install; the `*OnRealDataTests` guards check each partition against the shipped tables. Room → job from the room table (`JobRules.RoomFor`, ADR-0088).
- **A decimal printed from an `Fp32` is lossy.** Transcribe the raw value.
- **A fixture creature inherits nothing.** Every field a mechanic reads must be transcribed for every fixture creature and covered by a drift guard.
- **Only a keeper is staffed** (ADR-0043/0052); player 2 is neutral; test worlds must not call the hero side "Keeper".
- **Order in the work queue is behaviour** (ADR-0010): never sort or deduplicate it.
- **Confidence tags are mandatory** — CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN — and `[Dk2Behavior(Confidence.X, "docs/research/…")]` on the rule type. `Confirmed` is reserved for the running game; a binary read is STRONGLY INFERRED.
- **Record a shipped variable as "no reader found", never "no reader"**, and say what was scanned; a zero-valued variable with a live reader is *disabled*, not *cut* (ADR-0085).
- **Every researched mechanic gets `docs/research/<name>.md`**; DK2 facts read from the binary go there in our own words with the address as provenance. Decompiler output is never committed (ADR-0046).
- **Licence boundaries:** SPDX notice on every source file (`LicenseHeaderTests`); no Flame identifier in `src/`, `tests/` or `unity/`, comments included (ADR-0080); OpenKeeper facts carry provenance; wiki content in our own words with a citation.
- **Never `git add -A`.** Stage named paths. Nothing from `original-reference/`, no `.kwd`/`.wad`/`.kmf`/`.sav`, nothing from `reverse-engineering/` but its README, no captures or game-data tables.
- **Put the doubt in the test name**, not only in a doc comment — a test that pins a guess hides it.
- **A status line is a claim with a date.** Do not hand-edit counts; run the commands in *Current state*.
- **Question and ADR numbers are taken from committed files** — check the other worktree before taking one (`git worktree list`), and prefer appending to an existing row.

---

## Environment gotchas (each of these already cost time)

- **`cat > file` with no stdin hangs the Bash tool**, and so does anything that pipes into `python`. **Long heredocs die** (~150 lines) and heredocs of any length collapse backslash pairs, silently no-opping a patch script. Write scripts with the `Write` tool; a patch script should assert its match count before writing and is not idempotent.
- **Rewriting a file here drops its BOM** unless you read `utf-8-sig` and write the BOM back; documents are UTF-8 with a BOM, keep it. A merge can leave CRLF working copies — look at the bytes before matching line endings.
- **`dotnet build` takes one project, not several** — pass `DK2.slnx`. `dk2tools status` lists tests with `-c Release --no-build`, so a Debug-only build makes it report a stale count. A hung `dk2tools` with no CPU is a full stderr pipe.
- **Another session edits this tree concurrently.** Check `git status --short` and `git log` before any tree-wide edit; leave its uncommitted files alone. Worktrees: `D:/DungeonKeeperRemake` (master) and `D:/DungeonKeeperRemake-Codex` (Codex's branches). Codex's own numbers start where the last committed row ends; tell it the next free question number in its prompt.
- **The commit hook runs `dk2tools docs --check`** over everything changed since HEAD, staged or not; question rows ≤ 800 chars, doc comments ≤ 12 lines, research docs ≤ 400 lines, this file ≤ 500, ADRs ≤ 50, commit bodies ≤ 15.
- **Ghidra scans:** the image has **two** executable blocks — `.text` (`0x401000`–`0x652c00`) and `cseg` (`0x653000`–`0x66bfff`) — so scan all thirteen initialised blocks with a positive control (variable 19's global returns **226** hits); an instruction-text match on `+0x758` also hits `[ESP + 0x758]`; the decompiler drops empty `if` bodies and hides `this` — read the narrowest caller's `MOV ECX`; a `JMP` into a shared `CALL` is dropped by a window read; count the callee's `RET n` before reading a push window.
- **Flame/OpenKeeper builds** (`original-reference/README.md`): Flame's install step replaces the game's `PATCH.dll` — never pass `-DDK2_DIR` casually, back up first.
- **`winget search` without `--source winget` prompts** and fails.
- **Unity:** Hub is an MSIX package; the editor is at `C:\Program Files\Unity 6000.6.0f1`; exit code 198 with `Found 0 entitlement groups` means not activated; never reference `UnityEngine.dll` alongside the module assemblies (CS0433); the `.sln`/`.csproj` under `unity/DK2.Unity` are Unity's and gitignored; re-run `sync-assemblies.ps1` after any change under `src/`.
- **`pwsh` is not installed.** Use the `PowerShell` tool or `powershell.exe -NoProfile`.

---

## Research method that has been working

Cheap to expensive; stop when the question is answered.

0. **Ask the owner, or hand them `observation-checklist.md`.** They have played DK2; anything a player would *see* is settled in a sentence. Ask for quantities by name and for values that differ. Record "no" as CONFIRMED too. An owner's "I'd have to play to verify" can often be mined instead (Q398: three writes with no zero test).
1. **The shipped data.** `dk2tools kwd …`, the dump notes. Check our own dump before recording a number as absent.
2. **The binary, via Ghidra** — before filing UNKNOWN (ADR-0046). Find a record loader by `CopyPairs.py` (source = file offset + `0x3F0` for creatures, `0x80` for objects); DK2's ten debug dumpers name ~800 fields (`debug-dumpers.md`) — find the dumper before scanning offsets; when a counter's writer will not be found, find a timer set in units of it; a mechanic with no state handler may live in its state **category**'s update hook (`state-category-hooks.md`).
2a. **Read the running game's memory** once the addresses are known. What discriminates is **predicting the specific value a mechanism writes**, not looking for activity near a tick. An idle capture beat two played ones; predictions go on record before the run.
3. **`external-resources.md`**: dk2-tools templates (format facts), Flame's `sgmap` (`dk2tools sgmap`; structure, not behaviour), OpenKeeper as documentation (its mechanics come from the editor manual — cite it for formats, not rates), the wiki in our own words.

**Validate structurally, never on trust.** Record size = `contentSize ÷ itemCount` exactly; sum a template's field widths to the stride; semantic invariants (happy-job chances sum to 100); cross-parser references resolving; known values (Vampire 666 damage).

**Traps with names, each of which has cost a committed claim:**

- *A claim nothing checks may already be false.* Ask what would fail if the rule stopped holding.
- *A named variable is not evidence the engine consults it.* Count readers first (ADR-0085).
- *A sweep of keys is not a sweep of values.* Sweep the field across all 66 level files (`kwd varsweep`).
- *Two correct numbers may never meet.* Check the engine puts them in one place (ADR-0081/0083).
- *A measured amount is not a cadence* (ADR-0089). *Two wrong constants can cancel*; the residual's sign discriminates.
- *A negative is a claim about the scope searched.* Say what was swept; a `.text`-only scan misses `cseg`.
- *A value can pass through a transform, so a single unit is an assumption.* Ask where it is written as well as read (Q233).
- *An id that appears as no constant may be stored encoded*; discriminate a decode with a set of independently fixed size (Q282).
- *A near-constant byte makes every record look alike.* Check the corpus distribution first (`kwd trigger-params`).
- *Do not name a mechanism from the shape of its handling* (Q229, Q232).
- *A type number is not an arm*: four dispatchers, the walker that reached a record decides which code runs.
- *An inference across an unread span is a guess.* Dump the gap first.
- *Read the prologue and epilogue first*; diff sibling arms against one you have read.
- *A differential experiment needs its null.* *Sample the state, not the duration.* *Rank by the quantity, not its proxy.*
- *An observation names an outcome, not a mechanism* (Q28). *A field unread is not information lost.* *A correct rule can be inert.*
- *Ask before building.* Where a rule needs a field the simulation lacks, say so.

---

## Live research questions

`open-questions.md` is the record; `RESEARCH-QUEUE.md` the working list. The ones
that gate work today, by how they are answered:

**Owner, one look at the running game** (`observation-checklist.md`)

| Q | What to watch |
|---|---|
| Q376 | how long a creature dances after a kill (ours: 8 ticks) |
| Q401 | does a Haste cast on a standing ally ever fail to land? |
| Q202 | slap a creature to Unhappy with a bed available; does sleeping bring it down? |
| Q177 | does the aimed (drop) bonus carry from one marked tile to the next? The binary says yes |
| Q118 | how much does water slow a creature (a Vampire in a lake: 25 s to die = per-second hazard, 1.25 s = per-tick) |
| Q103 | the 240 s / 9 s sleep cycle — corroborated by stopwatch, not settled |

**Binary reads**

| Q | Read |
|---|---|
| Q363, Q358, Q359 | the pillar's damage path; the defeat routines `0x004DAA10`/`0x004DAE40`; the alliance loops' conditions |
| Q395, Q393, Q397, Q400 | unread shot handlers and the Guided Bolt; `0x00495080(0x1F)` possession modifier; `0x004B32A0`; the approach loop's approximations |
| Q201 | the dizzy-drop driver: candidate is the per-creature `stunDuration` at record `0x0ED0` |
| Q199 | the pet-dungeon mana cap (+1400 against variable 235's 500) — find its reader |
| Q197 | portals visible through fog on the minimap — a second mechanism |
| Q185 | state-record flag byte `+0x18`: bit 0 on 147 of 281 states, untested by its seven readers |
| Q210 leftovers | creature flag bit 0's attack; bit 1 has no reader found; room table integers `+0x19/+0x1d/+0x21` |
| `creature+0x3EA` | bits 4 (doubles prayer mana) and 7 (Q69) gate real behaviour and are UNKNOWN |
| Q84 | id 66 as the level's player count; `Level17` reads 2 against a pool reaching player 5 |
| Q35, Q188 | which treasury tile a withdrawal drains; when a prisoner's step roll halves |
| Q220, Q221 | a standalone GLOBALS(0) chunk; the 16-byte DeadBody THINGS payload |
| Q216 | slapping a researcher (needs the research tree to be watchable) |

**Triggers**

| Q | Open |
|---|---|
| Q229 | flag id **128** is not a flag — a dword at `+0x72A2` through virtual slot `+0x348`; carried opaque, do not name it first |
| Q232 | timer id **16** is not a timer — read through slot `+0x34C`; all 86 action type 23 records target it; `TriggerState` refuses the id |
| Q226, Q227 | selectors and fields read without a revealed role, exposed as `Unknown_*`; action type 24 alone has 827 records |
| Q225 | trigger types the shipped corpus never uses but the executable reads; needs a map made in the original editor |

**Parked UNKNOWNs:** Q57 (a keeper who cannot pay imps; ours floors mana at zero), Q58 (Possession upkeep), Q70 (the last 72 bytes of a CREATURES record), Q75 (a starting gold stockpile), Q76, Q78, Q83 (`GameTicks` = 340), Q87 (`CreaturePool`: limit or quota), Q102, Q164, Q169, Q194, Q208 (the Imp's `ExpPerSecond` is 40× its training rate).

---

## Corrections carried forward

Each is a thing a reader would plausibly re-assume; the cited file has the evidence.

- "Five work applications a second" was never DK2's cadence — the fifth is the size of one application; work lands once a tick (ADR-0089). An upward job costs the **deficit** (ADR-0089 reversing ADR-0024).
- "Filling in" a floor tile back to rock is not a DK2 action (Q219); `FillInHealth` and Claimed Path → Rock were a shape in the data only.
- The editor's "Hero Portal" is the **Mercenary Portal** and yields mercenaries, never a Dwarf (Q211); portal capacity is `(n − 1) × 5 + 15` over every portal room (Q98); the interval is `(n² + base)` with `n` the portal's own tally, not OpenKeeper's formula (ADR-0086).
- Anger is six counters banded on the **largest**, not a sum (ADR-0081); `AngerNoPay` has no treasury clause; only the *happy* chooser charges `AngerNoWork` (ADR-0079/0082); Jack never uses its anger values (ADR-0087).
- `ExpPerSecond` is **combat** experience; "the record's rates are per minute" was a coincidence of 20 × 60 (ADR-0083). Imp work experience is per tick, so an imp reaches level 2 in about 37 s.
- DK2 stores **one** fog bit per machine (`fog-of-war.md` §10). The save's tile grid is not at a fixed offset; locate it by correlation.
- The creature state table has **281** records (ADR-0072). The flower above a creature is **health**, six petals (Q108); the Hand cannot pick up a collapsed creature (Q161).
- `MaxFreeRangeChickensPerPlayer` does not cap a hatchery, one chicken per tile does (Q135); "limit"-named variables are not limits (ADR-0055); "wrongly named" variables are live; variable 112's zero **doubles** converted-creature prayer mana (Q212).
- In the MAP record player **2** is the unowned marker (`PlayerIds`). Only an imp may be dropped on unclaimed Dirt Path (Q198).
- A THINGS record's `X`/`Y` are **tile indices**, `InitialHealth` a **percentage**, `RoomThing.RoomType` a **terrain** id (ADR-0101) — all three external transcriptions were wrong.
- A trigger timer threshold in a `.kld` is in **seconds**, compared in **ticks** (ADR-0102). The banks are **level** state, 128 flags and **16** timers; the byte the external list calls `Flag` is an operation and addressing mode (Q229, Q232).
- `DispatchMap.py` prints `index + 1`; the true type is `index + base` — 19 for player conditions, 15 for creature actions, 10 for player actions, 1 for world actions.
- Condition types 29 and 47 return a bare boolean with no `Comparison`; type 29 tests availability strictly `== 3` (ADR-0120). Type 19's `CreatureId` **0** is the owned total minus every worker (ADR-0113). The player object's `+0x5A/+0x62/+0x6A/+0x72` are four availability tables (rooms, doors, traps, spells), nine bytes an entry.
- **The player task pool is a work pipeline, not a set of marks**: a tile sits in several kind lists and migrates, so entering and leaving are the events; a finished dig and a cancelled one both leave it and the claim that follows separates them (ADR-0131). **A capture records what the game did, never who caused it** — ask for labelled play before attributing anything.
- The player record base is `playerArray + (id−1) × 4389`; a room's `FirstSlab`/`LastSlab` are the ends of a list, not a bounding box. The runtime map is `world + 0x9DA` (`runtime-map.md`).
- DK2 claims a floor tile in about twelve ticks, ours in four; reinforcing ≥61 against our 50 (Q292) — a difference of mechanism, not arithmetic.
- A fixture can build a tile DK2 cannot (Dirt Path with an owner, Claimed Path without one); a test standing on one measures nothing.
- An owner's "it does not" can be right for a reason the code shows: the Firebomb's explosion inherits a zero radius and hurts nobody (Q398).
