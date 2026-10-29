# Headless simulation throughput: hash, keeper views, overlap, parallel watch

> **For the implementing agent:** work task by task, in the order given. Each task ends in a
> commit and a measurement. Do not start a task before the previous one's gate passes. Where a
> step says **STOP**, stop and ask the owner.

**Status:** proposed 2026-10-28; re-checked against master at hash 328 on 2026-10-29 (line
references, version and ADR numbers, gates). Nothing below is built. Covers headless tick cost only.
Frame pacing in Unity is the [parallelism roadmap](2026-10-17-parallelism-design.md),
whose simulation bullet now points here.

**Goal:** cut the cost of a headless tick by about two thirds on keeper-heavy maps, without
changing any simulation result. Then let `watch` run scenarios on several cores.

**Architecture:** the simulation core stays single-threaded and in its registration order.
The plan has three parts:
- **Serial fixes for the two largest costs:** a slow hash algorithm, and a navigation mesh
  rebuilt for every keeper on every tick.
- **One overlap:** the state hash runs on a pool thread while the keeper AI thinks. Both only
  read the world.
- **Process-level parallelism** in `watch`, behind an owner decision.

**Tech stack:** .NET 10 (`net10.0`); `DK2.Core`, `DK2.Pathfinding`, `DK2.Simulation`,
`DK2.AI`, `DK2.Assets` and the other projects importing `UnityBound.props` also build as
netstandard2.1 for Unity, so new code there (e.g. `KeeperStaging`) must compile for it. Tests use xUnit, with `[InstallFact]` for tests that need
the DK2 install.

---

## Background you need

Read first, briefly: `AGENTS.md` (Critical rule, *One fact, one home*, *Replace, do not
append*, Goal 9); `docs/research/hash-versions.md` (the top section); ADR-0454, ADR-0458,
ADR-0486, ADR-0552 and ADR-0575 (the speed budget) in `docs/DECISIONS.md`.

**Numbers this plan reserves.** As of master `1db766f0`: `HashVersion` is 328 and the top ADR
is ADR-0575. This plan uses `HashVersion` **329** (Task 1) and ADR-**0576** (Task 1),
ADR-**0577** (Task 2), ADR-**0578** (Task 3). Before each task, re-read
`GameSimulation.HashVersion` and the last `## ADR-` heading in `docs/DECISIONS.md`. If master
has moved past them, take the next free number and use it everywhere this plan names the old one.

**Hard constraints.**
- **Determinism.** Same inputs give the same `GameSimulation.StateHash()` at every tick. Only
  Task 1 may change hash *values*, and only through `HashVersion`. Tasks 0 and 2–5 must leave
  every hash and every output file byte-identical.
- **Order is behaviour.**
  - Never reorder systems, creatures or random-number draws.
  - Never parallelise inside `GameSimulation.Tick`.
  - `CreatureUpdateSystem` walks creatures newest-first through the shared `SimRandom` and
    writes state that the next creature reads. It must stay serial.
- **No invented DK2 behaviour.** Nothing here changes what the game does.

**Measured baseline.** Release build, this machine, one run each, **at hash 324**. Hashes
325–328 (ADR-0571 to ADR-0574) changed behaviour since, so Task 0 re-baselines. Expect 20–40%
run-to-run noise. Figures are ms per tick.

| Scenario | median tick | state hash | keeper AI | of which: views | of which: `Think` |
|---|---|---|---|---|---|
| `keepers/level17_harkan_unassisted` (1 keeper) | 8.2 | 5.7 | 0.5 | – | – |
| `keepers/level11a_drako_attack` (4 keepers) | 12.1 | 4.7 | 5.1–6.8 | 5.8–6.0 | 0.8 |
| `keepers/level18_nemesis_allies` | 13.1 | 7.5 | 0.9 | – | – |

**Why these costs are what they are.**
- **State hash.** `Hash64` (`src/DK2.Core/Hash64.cs`) is byte-wise FNV-1a, so one
  `Add(ulong)` is eight dependent multiplies, and `int` is widened to 8 bytes. That is roughly
  400–550k adds per tick, at 8.5–12 ns each. The harness hashes every tick
  (`ScenarioRunner.cs:317`), and so does the Unity journal (`DungeonRunner.InputJournal.cs:153`).
- **Keeper views.** For every own fighter, the `KeeperView` constructor routes from the
  staging point (`src/DK2.AI/KeeperView.cs:96-115`) through a *new* `WorldSteering`. Its first
  `Route` calls `Refresh()`, which runs a full `NavMesh.Build`: about 2.6 ms per keeper per tick.

## Order of work

| Task | Item from the review | Expected saving (drako) | Changes hash values? |
|---|---|---|---|
| 0 | measurement output | none | no |
| 1 | faster state hash | about −4 ms, every map, Unity journaling too | **yes, `HashVersion` 329** |
| 2 | one shared staging mesh | about −4 to −5 ms on keeper maps | no |
| 3 | hash overlapped with keeper AI | about min(hash, keeper AI) left after Tasks 1–2, ≈ −1 ms | no |
| 4 | record what is not done; fix a false comment | none | no |
| 5 | `watch --jobs N` | batch throughput ≈ ×cores | no; **owner approval first** |

Tasks 1 and 2 come before Task 3 because they shrink both sides of the overlap. Re-measure
before Task 3, and skip it if its gate fails.

## Shared procedure: the equivalence check

Every task that must not change results runs this before and after its change, and compares.

```bash
cd <the repo root of your worktree>
export DK2_DIR="E:/GogGames/Dungeon Keeper 2"
dotnet build DK2.slnx -c Release -v q
H=src/DK2.Harness/bin/Release/net10.0/DK2.Harness.dll
LABEL=before-taskN     # then after-taskN
dotnet $H run scenarios/keepers --out build/perf/$LABEL
dotnet $H run scenarios/smoke   --out build/perf/$LABEL-smoke
```

**Gate A: the files.**
`diff -r -x timings.txt -x input.journal build/perf/before-taskN build/perf/after-taskN` must
print **nothing**. Do the same for `-smoke`. This covers `hashes.tsv`, `events.log`,
`keeper-commands.tsv`, `world.txt` and `report.md`.
- `input.journal` is excluded because it is binary. Its header holds the commit, `src.dirty`
  and `started_utc` (`src/DK2.Harness/JournalHeader.cs:24-37`), so it always differs.

**Gate B: the journals replay** (Tasks 0 and 2–5; not Task 1, because `replay` refuses a journal
from another `HashVersion`).
- On the *after* build, replay every *before* journal:
  `dotnet $H replay <the run's scenario yaml> build/perf/before-taskN/<run folder>/input.journal`.
- Each must print `replay matches: … recorded hashes compared`. Any other output is a diff.

A failing scenario expectation (exit 1) is fine if it fails the same way on both sides. Any
diff in either gate means the change altered behaviour: fix it, never re-pin.

**The speed-budget gate (ADR-0575).** Run it alone, in Release, with the install:

```bash
DK2_DIR="E:/GogGames/Dungeon Keeper 2" DK2_SPEED_BUDGET=1 dotnet test tests/DK2.Behavior.Tests -c Release \
  --filter FullyQualifiedName~SpeedBudgetTests --logger "console;verbosity=detailed"
```

- It must pass: campaign Levels 1–4 at 50× real time or better, and no corpus tick slower than
  100 ms after warm-up.
- Record the Level 1–4 totals and the worst tick. At hash 328, Level 4 takes 21.3 s against a
  34 s budget, and the worst tick is 36.5–40.5 ms.
- If it fails on a loaded machine, rerun it alone before calling it a regression. A real
  regression is a **STOP**.

**Measuring.** Read `timings.txt` (added by Task 0) in each run folder: the `tick p50` line
and the marker table. The machine is shared and noisy:
- run each variant 3 times, alternating before/after;
- report the median of the three;
- quote `level11a_drako_attack` and `level17_harkan_unassisted`.

---

### Task 0: Write each run's timings next to its report

The harness already collects per-marker timings (`RunTimings`, `src/DK2.Harness/RunTimings.cs`)
and discards them. Every later task needs them.

**Files:**
- Modify: `src/DK2.Harness/ScenarioReportWriter.cs` (inside `Write`, after the `report.md`
  write at line ~150, **outside** the `if (!string.IsNullOrEmpty(result.Picture))` block, which
  only runs when the run drew a picture)
- Create: `tests/DK2.Behavior.Tests/ScenarioReportWriterTests.cs` (no test checks the report
  files today)

**Step 1: Write the failing test.**
- Run a 1-tick synthetic scenario: the `flat_8x8` YAML at the top of `WatchdogTests` is the
  model. Use `new ScenarioRunner(ScenarioTests.SyntheticDatabase(), null).Run(...)`, as
  `RunMonitorTests.cs:67` does.
- Call `ScenarioRunner.WriteReport(result, tempDir)` (`ScenarioRunner.cs:497`).
  `ScenarioReportWriter` is internal, but `DK2.Harness.csproj` has `InternalsVisibleTo` for the
  tests.
- Assert that `timings.txt` exists and contains `"ticks: tick p50"` and `"worst tick after
  warm-up"`.

**Step 2: Run it; expect FAIL.**
`dotnet test tests/DK2.Behavior.Tests --filter "FullyQualifiedName~<TestName>"`

**Step 3: Implement.**

```csharp
        // Wall-clock, so it differs run to run; kept out of report.md, which compares across runs.
        if (result.Timings is RunTimings timings)
            File.WriteAllText(Path.Combine(outputDir, "timings.txt"), timings.Summarise(top: 30));
```

- Also write one line, `worst tick after warm-up: <ms>`. Compute it from `RunTimings.TickTimes`
  (`RunTimings.cs:60`), skipping the first two ticks as `SpeedBudgetTests` does.
- Write `Allocated` too: ADR-0575 names keeper allocation (2–4.7 MB per tick) as the cost to
  watch.

**Step 4: Run the test; expect PASS.** Then run the whole `DK2.Behavior.Tests`.

**Step 5: Record the baseline.**
- Run the shared procedure with `LABEL=baseline`, 3 times.
- Run the speed-budget gate once.
- Keep the medians and the speed-budget figures for the commit message of Task 1.
- Gate A against a run of the parent commit must show no diff, apart from the new
  `timings.txt` files.

**Step 6: Commit.** `harness: write each run's timings beside its report`

---

### Task 1: One mixing round per value in the state hash (`HashVersion` 329)

**The design.**
- **`Hash64` stays the state hash's type.** It is the `ref Hash64` parameter in about 60
  `HashInto` methods. Only its insides change: one multiply–rotate round per value, finished
  by xxHash64's avalanche when `Value` is read.
- **Byte-wise FNV-1a moves to a new type, `Fnv1a64`.** It keeps the hashes that are *not*
  simulation state and are pinned:
  - `AssetRef.StableHash`, which decides Unity placeholder colours, pinned in `AssetRefTests`;
  - the ~66 navigation-mesh pins in `NavMeshBuildOnRealDataTests`, which use a test-local
    `Fingerprint`.
- **Result:** the only literal that moves is the `DeterminismTests` pin.

**Why this mix is safe as a divergence detector.** For a fixed added value, one round is a
bijection on the state: xor, rotate, multiply by an odd constant and add a constant are each
invertible. So two different states never merge on the same next value. Order matters because
of the rotate–multiply. The added constant means a run of zeros changes the state, so a list
of 2 zeros and a list of 3 zeros hash differently.

**Not used: `System.IO.Hashing.XxHash3`.** `DK2.Core` ships to Unity as netstandard2.1
(`unity/sync-assemblies.ps1`), and that would add a NuGet dependency to the Unity plugins.
netstandard2.1 also has no `BitOperations.RotateLeft`, so the rotate is written out. RyuJIT
and Mono both compile the pattern to `rol`.

**Accepted loss of type separation.** `Add(byte)`, `Add(int)` and `Add(ulong)` of the same
value now feed the same word, where FNV saw 1 or 8 bytes. The hashed stream's layout is
fixed per `HashVersion`, so this cannot hide a divergence within one version.

**Files:**
- Create: `src/DK2.Core/Fnv1a64.cs`
- Modify: `src/DK2.Core/Hash64.cs` (whole file)
- Modify: `src/DK2.Assets/AssetRef.cs:248-254` (`StableHash`)
- Modify: `tests/DK2.Behavior.Tests/NavMeshBuildOnRealDataTests.cs` (`Hash64` → `Fnv1a64`
  at ~125 and in `Fingerprint` ~234)
- Create: `tests/DK2.Core.Tests/Fnv1a64Tests.cs`
- Modify: `tests/DK2.Core.Tests/Hash64Tests.cs`
- Modify: `src/DK2.Simulation/GameSimulation.cs:24` (`HashVersion = 329`)
- Modify: `src/DK2.Simulation/WorldSnapshot.cs:32` (`FormatVersion = 329`)
- Modify: `tests/DK2.Simulation.Tests/DeterminismTests.cs:163-164` (pin `0x2B98D9C202582A74UL`
  and the comment above it)
- Modify: `docs/research/hash-versions.md` (new **329** entry on top)
- Modify: `docs/DECISIONS.md` (ADR-0576)
- Regenerate: the status line in `DK2_REIMPLEMENTATION_PLAN.md` (see Step 11)

**Step 1: Split FNV out, behaviour-neutral.**
- Create `src/DK2.Core/Fnv1a64.cs` as a verbatim copy of today's `Hash64.cs` with the type
  renamed to `Fnv1a64`.
- Change its summary to: "Byte-wise FNV-1a, for the hashes that must not move when the state
  hash's algorithm changes: asset placeholder colours and test fingerprints. The simulation's
  state hash is `Hash64`."
- Switch `AssetRef.StableHash` and the `NavMeshBuildOnRealDataTests` hash and `Fingerprint`
  to `Fnv1a64`.
- Move `KnownVector_IsPinned`, `EmptyHash_IsTheOffsetBasis` and copies of the other four
  tests into `Fnv1a64Tests`, each using `Fnv1a64`.

**Step 2: Run the tests; expect PASS with no pin changed.**

```bash
dotnet test tests/DK2.Core.Tests
dotnet test tests/DK2.Assets.Tests
DK2_DIR="E:/GogGames/Dungeon Keeper 2" dotnet test tests/DK2.Behavior.Tests --filter "FullyQualifiedName~NavMeshBuildOnRealData"
```

The NavMesh tests must run, not skip: set `DK2_REQUIRE_INSTALL=1`.

**Step 3: Commit.** `core: byte-wise FNV-1a becomes Fnv1a64 for the hashes that must not move`

**Step 4: Write the failing tests for the new `Hash64`.** Replace `Hash64Tests` with:

```csharp
public class Hash64Tests
{
    [Fact]
    public void SameInput_ProducesSameHash() =>
        Assert.Equal(HashOf("dungeon", 42), HashOf("dungeon", 42));

    [Fact]
    public void DifferentInput_ProducesDifferentHash()
    {
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("dungeon", 43));
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("keeper", 42));
    }

    [Fact]
    public void OrderMatters()
    {
        Hash64 a = Hash64.Start(); a.Add(1); a.Add(2);
        Hash64 b = Hash64.Start(); b.Add(2); b.Add(1);
        Assert.NotEqual(a.Value, b.Value);
    }

    /// <summary>FNV-1a's state could not reach zero; this mix's can, and a round that left zero at
    /// zero would make a run of zeros invisible. The round's added constant prevents it.</summary>
    [Fact]
    public void TheLengthOfARunOfZeros_Matters()
    {
        Hash64 two = Hash64.Start(); two.Add(0UL); two.Add(0UL);
        Hash64 three = Hash64.Start(); three.Add(0UL); three.Add(0UL); three.Add(0UL);
        Assert.NotEqual(two.Value, three.Value);
    }

    [Fact]
    public void NullString_DiffersFromEmptyString()
    {
        Hash64 a = Hash64.Start(); a.Add((string?)null);
        Hash64 b = Hash64.Start(); b.Add(string.Empty);
        Assert.NotEqual(a.Value, b.Value);
    }

    /// <summary>Pins the algorithm, as Fnv1a64Tests pins FNV: a change here moves every state hash
    /// and needs a HashVersion bump.</summary>
    [Fact]
    public void KnownVector_IsPinned()
    {
        Hash64 h = Hash64.Start();
        h.Add(0x0123456789ABCDEFUL);
        h.Add((byte)'a');
        Assert.Equal(0x0000000000000000UL, h.Value); // replace with the value Step 6 prints
    }

    private static ulong HashOf(string s, int n)
    {
        Hash64 h = Hash64.Start(); h.Add(s); h.Add(n);
        return h.Value;
    }
}
```

**Step 5: Implement.** Replace the body of `src/DK2.Core/Hash64.cs`. Keep the licence header.
Keep the old summary's second and third paragraphs (why `GetHashCode` is ruled out; that it
is a divergence detector). Replace its first line, and add the paragraph on the round.

```csharp
/// <summary>
/// The simulation's stable 64-bit structural hash: one multiply-rotate round per value added,
/// finished by xxHash64's avalanche (its PRIME64 constants). Byte-wise FNV-1a, used here until
/// HashVersion 328, cost eight dependent multiplies per value and was most of a headless tick;
/// it lives on as <see cref="Fnv1a64"/> for hashes that must not move.
///
/// For a fixed value a round is a bijection on the state (xor, rotate, multiply by an odd
/// constant, add), so two different states never merge on the same value.
/// (… keep the existing paragraphs on reproducibility and on being a divergence detector …)
/// </summary>
public struct Hash64
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
    private const ulong Prime5 = 0x27D4EB2F165667C5UL;

    private ulong _h;

    public static Hash64 Start() => new() { _h = Prime5 };

    public readonly ulong Value
    {
        get
        {
            ulong h = _h;
            h ^= h >> 33;
            h *= Prime2;
            h ^= h >> 29;
            h *= Prime3;
            h ^= h >> 32;
            return h;
        }
    }

    public void Add(ulong v)
    {
        ulong x = _h ^ (v * Prime2);
        // netstandard2.1 has no BitOperations; the JIT turns this pattern into one rotate.
        _h = (((x << 31) | (x >> 33)) * Prime1) + Prime4;
    }

    public void Add(byte b) => Add((ulong)b);
    public void Add(long v) => Add(unchecked((ulong)v));
    public void Add(int v) => Add(unchecked((ulong)(long)v));
    public void Add(uint v) => Add((ulong)v);
    public void Add(short v) => Add((long)v);
    public void Add(ushort v) => Add((ulong)v);
    public void Add(bool v) => Add(v ? 1UL : 0UL);
    public void Add(GameTick t) => Add(t.Value);
    public void Add(Fp32 f) => Add(f.Raw);

    /// <summary>Hashes a string by its characters, never by <c>GetHashCode</c>.</summary>
    public void Add(string? s)
    {
        if (s is null) { Add(ulong.MaxValue); return; }
        Add(s.Length);
        foreach (char c in s) Add((ushort)c);
    }
}
```

Check the overload list against today's file before deleting it. Every public `Add`
overload that exists today must still exist, with the same widening as before.

**Step 6: Run `DK2.Core.Tests`.**
- Expected: `KnownVector_IsPinned` fails, printing the actual value, and every other test
  passes.
- Put the printed value into the test and run again: all pass.
- That pin comes from the implementation, so it guards regressions, not correctness.

**Step 7: Bump the versions.** `HashVersion = 329` in `GameSimulation.cs` and
`FormatVersion = 329` in `WorldSnapshot.cs`. Both must change in the same commit (see their
doc comments).

**Step 8: Re-pin the determinism test.**
- Run: `dotnet test tests/DK2.Simulation.Tests --filter "FullyQualifiedName~DeterminismTests"`.
  Expected: `StateHash_IsStableAcrossBuildsAndMachines` fails with the new value.
- Put that value in the assertion and change the comment to `// HashVersion 329; ...`.
- Re-run. Expected: PASS.
- Then run `ThePinnedWorldIsPopulated...`. Expected: PASS, unchanged.

**Step 9: Prove behaviour did not change.**
- Run the shared procedure with `LABEL=after-task1`, against Task 0's baseline.
- Expected:
  - `events.log` and `world.txt`: no diff in any run folder;
  - `hashes.tsv`, `report.md` (if it prints a hash) and `keeper-commands.tsv`: may differ in
    hash columns only (`input.journal` is excluded, as in Gate A);
  - `keeper-commands.tsv`: tick, player and type columns identical.
- Check that last point with:

```bash
for f in $(cd build/perf/baseline && find . -name keeper-commands.tsv); do
  diff <(cut -f1-3 build/perf/baseline/$f) <(cut -f1-3 build/perf/after-task1/$f) || echo "DIFF $f"
done
```

- Any other diff is a bug in Step 5: the stream of `Add` calls must be the same as before.

**Step 10: Run the full test suite.**

```bash
DK2_DIR="E:/GogGames/Dungeon Keeper 2" DK2_REQUIRE_INSTALL=1 dotnet test DK2.slnx -c Release
```

Expected: all pass. In-process hash comparisons (`KeeperViewTests`, `StructureViewTests`,
`PacketLogTests`, …) compare two runs, not literals, so they need no change.

**Step 11: Docs and gates.**
- **`docs/research/hash-versions.md`:** a new top entry in the existing style. **329** — the new
  pin. FormatVersion also 329. Hash algorithm only: `Hash64` is one multiply–rotate round per
  value plus xxHash64's avalanche (ADR-0576). No hashed field, behaviour or save layout
  changes; journals and saves from 328 are refused by version. Evidence: the Task 1 Step 9
  equivalence run.
- **ADR-0576 in `docs/DECISIONS.md`:** "The state hash mixes one value per round". Include the
  context (the measured cost from Step 12), the decision (the design above, why not XxHash3,
  why `Fnv1a64` stays) and the consequences:
  - every journal and save from before 329 is refused, including evidence under `D:\DK2Evidence`,
    which replays only at its own commit;
  - `keeper-commands.tsv` and `controller-commands.tsv` hash columns are not comparable
    across 328/329;
  - Unity placeholder colours are unchanged.
- **Status line:** `dotnet run --project src/DK2.Tools -c Release -- status` prints the status
  line. Paste it into `DK2_REIMPLEMENTATION_PLAN.md` as the tool says. Then
  `dotnet run --project src/DK2.Tools -c Release --no-build -- status --check` must print
  "agrees with the tree".
- **Unity:** run `unity/sync-assemblies.ps1`, then build the Unity compile check (see
  `unity/README.md`) to confirm the adapter still compiles against the new `DK2.Core`.

**Step 12: Measure.** Shared procedure, 3 runs. Expected: the `state hash` marker drops from
~4.7 to ≤ 1.2 ms/tick on drako, and from ~5.7 to ≤ 1.5 on harkan. If it drops by less than
half, stop and profile before going on.
- Run the speed-budget gate. This is the only task that speeds up the campaign levels, which
  have no keepers, so Level 1–4 totals should fall. They must not rise.

**Step 13: Commit.** `core: the state hash mixes one value per round (HashVersion 329)`. Put
the baseline and after medians, and the speed-budget figures, in the body.

---

### Task 2: One staging mesh for all keepers, handed out as a fresh build would be

**The problem.**
- Each `KeeperView` that has a fighter builds its own `WorldSteering`. Its first `Route`
  builds the whole map's mesh. With four keepers that is four identical builds per tick.
- The builds cannot simply be shared. A query changes the mesh's 16-slot **locate cache**
  (`NavMesh._locateCache`, `src/DK2.Pathfinding/NavMesh.cs:52`, written in `Locate` at
  141–162). Where a walk starts decides which triangle a point on an edge resolves to
  (ADR-0486). So a second keeper routing on a used mesh can get a different answer from a
  fresh one.
- Today every view routes on a fresh mesh. That is the behaviour to keep.

**The design.**
- **A new `KeeperStaging` object, owned by whoever runs the keepers** (the harness), holds one
  `WorldSteering` per world.
- **Whole builds, never edits.** That steering is created with `wholeBuilds: true`: whenever
  its routing bytes changed it rebuilds the mesh whole, and it never edits in place. A mesh
  edited in place (`Retriangulate`) need not equal a fresh build. After each build it records
  the locate cache the build left.
- **A fresh-equivalent mesh for every view.** Before each view routes, the locate cache is put
  back to that record. The view then sees exactly the mesh and cache a fresh `WorldSteering`
  would have built.
- **Reuse across ticks.** While the routing bytes are unchanged the mesh is reused across
  ticks, which is most ticks.
- **The router can be reused.** `MeshRouter`'s per-query stamps are generation-numbered and
  cleared when the generation wraps (`src/DK2.Pathfinding/MeshRouter.cs:174-177`), so they
  carry no answer between queries. Step 1 checks this.

**Files:**
- Modify: `src/DK2.Pathfinding/NavMesh.cs` (two methods next to `Locate`)
- Modify: `src/DK2.Simulation/Navigation/WorldSteering.cs` (constructor overload; `Refresh` at
  71–100; new `RestoreBuiltLocate`)
- Create: `src/DK2.AI/KeeperStaging.cs`
- Modify: `src/DK2.AI/KeeperView.cs:23-24` (constructor parameter) and `:111` (the `??=`)
- Modify: `src/DK2.AI/KeeperAI.cs:69-70` (`Think` overload)
- Modify: `src/DK2.Harness/ScenarioRunner.cs` (~284 create the staging; ~331 and ~342 pass it)
- Test: `tests/DK2.Simulation.Tests/NavMeshLocateTests.cs` (or a new
  `NavMeshLocateCacheTests.cs` beside it)
- Test: create `tests/DK2.AI.Tests/KeeperStagingTests.cs`

**Step 1: Read before writing, and confirm three facts.** If any one is false, **STOP** and
report.
1. `PlatformRoutingGrid` (`WorldSteering.cs:238-244`) reads only `owner._routingBytes`.
   `_platformCentres` is not read by the build (the audit on 2026-10-29 found this; confirm
   it). `ConstrainTarget` (`WorldSteering.cs:357-374`) reads the live world on every `Route`,
   but it does so for a fresh steering and a shared one alike, so it needs no reuse key.
2. Nothing in `NavMesh` besides `_locateCache` changes during a query and affects a later
   answer. `_fan` is scratch. `_pointTri` is a lookup index that, per its comment, changes no
   answer.
3. The `Refresh` path after the build (the code following line ~100: `Rebuilds`, `Snapshot`,
   `_structures`) runs the same whether the mesh was built new or reused.

**Step 2: Write the failing NavMesh tests.**

```csharp
    [Fact]
    public void ARestoredLocateCache_AnswersAsTheBuildLeftIt()
    {
        NavMesh mesh = NavMesh.Build(/* the grid the existing NavMeshLocateTests use */);
        int[] built = mesh.CaptureLocateCache();
        NavMesh fresh = NavMesh.Build(/* same grid */);

        // Move the cache: locate points all over the grid. Locate takes 1/256-tile units (U);
        // MaxX/MaxY are the 1280-tile canvas, and the cache cell is x >> 14, so looping over
        // MaxX in raw units would only ever touch cell 0.
        for (int y = 0; y < grid.Height; y += 3)
            for (int x = 0; x < grid.Width; x += 3)
                mesh.Locate(x * U, y * U);
        mesh.RestoreLocateCache(built);

        // A point on a shared edge or vertex is where the start matters (ADR-0486).
        foreach ((int x, int y) in /* the edge/vertex points the existing locate tests use */)
            Assert.Equal(fresh.Locate(x, y), mesh.Locate(x, y));
    }

    [Fact]
    public void RestoreLocateCache_RefusesAWrongLength() =>
        Assert.Throws<ArgumentException>(() => NavMesh.Build(/* grid */).RestoreLocateCache(new int[3]));
```

Use the grid, `U` and the vertex case from
`NavMeshLocateTests.ALocateAtAVertexNamesTheFanTriangleReachedFirst_WhichTheLastLocateDecides`
(`tests/DK2.Simulation.Tests/NavMeshLocateTests.cs:18-30`), which already covers the
start-dependence. The grid must span more than one 64-tile cache cell, or the test proves
nothing: add an assertion that the scramble loop changed `CaptureLocateCache()`. Run: expected
FAIL, the methods do not exist.

**Step 3: Implement in `NavMesh.cs`,** next to `Locate`:

```csharp
    /// <summary>A copy of the locate cache. A caller that reuses a mesh across independent
    /// questioners takes it after the build and restores it before each one, so each sees the
    /// mesh as a fresh build hands it out (ADR-0486: the cache decides edge and vertex answers).</summary>
    public int[] CaptureLocateCache() => (int[])_locateCache.Clone();

    public void RestoreLocateCache(int[] cache)
    {
        if (cache is null || cache.Length != _locateCache.Length)
            throw new ArgumentException($"a locate cache has {_locateCache.Length} slots", nameof(cache));
        Array.Copy(cache, _locateCache, cache.Length);
    }
```

Run: expected PASS.

**Step 4: Write the failing `KeeperStaging` tests** in `tests/DK2.AI.Tests/KeeperStagingTests.cs`.
- Build the world the way `KeeperBattleStepTests.cs:198` and `:639` do (they assert
  `HeartRouteExists`): a keeper with a heart and own creatures in a combat state, so the view
  routes fighters.
- Compare views by their bytes: `KeeperViewSnapshot.ToBytes(view)`
  (`src/DK2.AI/KeeperViewSnapshot.cs:18`) serialises everything a view holds, including each
  creature's routed flag, `KeeperCreatureView.HeartRouteExists` (written at
  `KeeperViewSnapshot.cs:52`).
- Each test must also assert that at least one creature has `HeartRouteExists == true`.
  Otherwise nothing was routed and the comparison is empty.

```csharp
    [Fact]
    public void AViewThroughStaging_EqualsOneBuiltAlone()
    {
        (GameDatabase db, WorldState world) = /* keeper with heart and fighters */;
        var staging = new KeeperStaging();
        byte[] alone = KeeperViewSnapshot.ToBytes(new KeeperView(db, world, Keeper));
        byte[] shared = KeeperViewSnapshot.ToBytes(new KeeperView(db, world, Keeper, staging: staging));
        Assert.Equal(alone, shared);
    }

    [Fact]
    public void TheSecondKeeperThroughOneStaging_EqualsItsViewBuiltAlone()
    {
        // Two keepers with fighters; the first view's routes move the shared mesh's locate cache.
        var staging = new KeeperStaging();
        _ = new KeeperView(db, world, KeeperA, staging: staging);
        Assert.Equal(
            KeeperViewSnapshot.ToBytes(new KeeperView(db, world, KeeperB)),
            KeeperViewSnapshot.ToBytes(new KeeperView(db, world, KeeperB, staging: staging)));
    }

    [Fact]
    public void AfterARoutingByteChanges_TheStagingRebuildsWhole_AndStillEqualsAFreshView()
    {
        var staging = new KeeperStaging();
        _ = new KeeperView(db, world, Keeper, staging: staging);
        /* dig or claim a tile on the fighters' route, the way existing tests change terrain */;
        Assert.Equal(
            KeeperViewSnapshot.ToBytes(new KeeperView(db, world, Keeper)),
            KeeperViewSnapshot.ToBytes(new KeeperView(db, world, Keeper, staging: staging)));
        Assert.Equal(2, staging.Builds);
    }

    [Fact]
    public void WhileTheBytesAreUnchanged_OneBuildServesEveryView()
    {
        var staging = new KeeperStaging();
        for (int i = 0; i < 3; i++) _ = new KeeperView(db, world, Keeper, staging: staging);
        Assert.Equal(1, staging.Builds);
    }
```

Run: expected FAIL (compile errors).

**Step 5: Implement `WorldSteering` whole builds.**
- Add a field and an overload. The existing two-argument constructor chains to it with `false`.

```csharp
    private readonly bool _wholeBuilds;
    private int[] _builtLocate = [];

    public WorldSteering(GameDatabase database, WorldState world) : this(database, world, wholeBuilds: false) { }

    /// <param name="wholeBuilds">Rebuild the whole mesh when a routing byte changed, never edit it
    /// in place, and keep the locate cache each build left for <see cref="RestoreBuiltLocate"/>.
    /// For a probe that must answer as a fresh build would (the keepers' staging routes,
    /// <c>KeeperStaging</c>); the game's own steering edits in place, as DK2 does.</param>
    public WorldSteering(GameDatabase database, WorldState world, bool wholeBuilds)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _routingGrid = new PlatformRoutingGrid(this);
        _wholeBuilds = wholeBuilds;
    }
```

- In `Refresh`, change the build condition and record the cache. Leave the `else` branch
  (the in-place edit) as it is:

```csharp
        if (_mesh is null || _wholeBuilds)
        {
            _mesh = NavMesh.Build(_routingGrid);
            _router = new MeshRouter(_mesh);
            if (_wholeBuilds) _builtLocate = _mesh.CaptureLocateCache();
        }
```

- Add:

```csharp
    /// <summary>Puts the mesh's locate cache back to what its last whole build left, so the next
    /// questioner gets the answers a fresh steering would give. Only for a whole-build steering.</summary>
    public void RestoreBuiltLocate()
    {
        if (!_wholeBuilds) throw new InvalidOperationException("only a whole-build steering keeps its build's locate cache");
        _mesh?.RestoreLocateCache(_builtLocate);
    }
```

**Step 6: Implement `KeeperStaging`** in `src/DK2.AI/KeeperStaging.cs`. Licence header as in
the neighbouring files.

```csharp
namespace DK2.AI;

/// <summary>
/// The keepers' staging probe (each fighter's route from the staging point, <see cref="KeeperView"/>)
/// over one world: one routing mesh for every keeper's view, rebuilt whole when a routing byte
/// changes and handed to each view as a fresh build would be. Without it every view built its
/// own whole-map mesh, about 2.6 ms on a campaign map, for every keeper on every tick.
/// Views are built one at a time; this is not thread-safe.
/// </summary>
public sealed class KeeperStaging
{
    private WorldSteering? _steering;
    private WorldState? _world;
    private GameDatabase? _database;

    /// <summary>Whole-map builds so far.</summary>
    public int Builds => _steering?.Rebuilds ?? 0;

    internal WorldSteering SteeringFor(GameDatabase database, WorldState world)
    {
        if (_steering is null || !ReferenceEquals(world, _world) || !ReferenceEquals(database, _database))
        {
            _steering = new WorldSteering(database, world, wholeBuilds: true);
            _world = world;
            _database = database;
        }
        _steering.Refresh();
        _steering.RestoreBuiltLocate();
        return _steering;
    }
}
```

`Rebuilds` counts edits as well as builds; with `wholeBuilds` every one is a whole build.
Confirm that against `Refresh`.

**Step 7: Wire it through.**
- `KeeperView` public constructor: add the trailing parameter
  `KeeperStaging? staging = null`. Replace line 111 with
  `steering ??= staging?.SteeringFor(database, world) ?? new WorldSteering(database, world);`.
  It stays lazy: a view without fighters builds nothing, as today.
- `KeeperAI`: change the overload to
  `public IReadOnlyList<PlayerCommand> Think(GameDatabase database, WorldState world, KeeperStaging? staging = null) => Think(new KeeperView(database, world, PlayerId, _playerActivationOrder, staging));`
- `ScenarioRunner`: create `var staging = new KeeperStaging();` once per run, beside `keepers`.
  Pass it in `keeper.Think(_database, world, staging)` and in the controllers'
  `new KeeperView(_database, world, controller.PlayerId, staging: staging)`.

**Step 8: Run the tests.**
- `dotnet test tests/DK2.AI.Tests`, then `tests/DK2.Simulation.Tests`.
- Expected: all PASS, including the four new ones.

**Step 9: Equivalence check.** Shared procedure with `LABEL=after-task2`, against a fresh
`before-task2` run of the Task 1 commit. Gates A and B: **no diff at all**. A diff here means a
view routed differently. Find which by diffing `keeper-commands.tsv` first.

**Step 10: Measure.**
- Expected: `keeper AI` on drako falls from ~5–7 to ~1.5–2.5 ms/tick. Record it, with
  `Allocated` per tick before and after.
- If the drop is much smaller, profile what is left of the `KeeperView` constructor before
  going on. Each view also refills the whole map's navigation bytes (`CaptureNavigationBytes`,
  `KeeperView.cs:49`); see Task 4's "Not done".
- Run the speed-budget gate. It must pass, with the worst tick no higher than in Task 1.

**Step 11: Docs.**
- ADR-0577: "The keepers' staging routes share one mesh, rebuilt whole". Cover the locate-cache
  restore and why the game's own steering still edits in place.
- No `HashVersion` change: keeper state is outside the hash. Say so in the ADR.

**Step 12: Commit.** `ai: one staging mesh for every keeper's view, restored to its build's locate cache`

---

### Task 3: Hash the world while the keepers think

**Gate. Measure first, on the Task 2 commit.**
- Take min(`state hash`, `keeper AI`) per tick on drako.
- If it is under 0.5 ms, **skip this task**: record that in Task 4's note and go to Task 4.
- Otherwise continue.

**The design.** After `simulation.Tick`, two things only read the world:
- `StateHash()`;
- the keepers' view building and `Think`.

They can run at the same time.
- **The hash goes to a pool thread.** The keepers stay on the run's thread, in order. View
  building writes `world.StructureCache`, `world.RoomSweepCache` (whose scratch is
  `[ThreadStatic]`) and the staging steering, so views must stay serial.
- **The hash reads none of those.** It reads `WorldState.HashInto`, the *simulation's*
  `WorldSteering` (`_context.Navigation`, never the staging one) and `Rng.HashInto`.
- **Same order of effects as today.** The hash is awaited, then recorded in the journal, the
  divergence check and the monitor. Only then is a keeper's exception rethrown. Commands go to
  the next frame exactly as today.
- **Unity is untouched:** it does not run `KeeperAI`.

**Files:**
- Modify: `src/DK2.Harness/RunMonitor.cs:13` (`RunOptions`: one property)
- Modify: `src/DK2.Harness/RunTimings.cs` (`RunClock`: one marker)
- Modify: `src/DK2.Harness/ScenarioRunner.cs:306-357` (the tick loop)
- Test: create `tests/DK2.Behavior.Tests/PostTickOverlapTests.cs`

**Step 1: Audit, and write down what you find in the commit body.**
- **What the hash reads.** List every method reached from `GameSimulation.StateHash()`
  (`GameSimulation.cs:261-271`). Use the LSP tool (`ToolSearch select:LSP`;
  `workspaceSymbol` once, then `findReferences` / go-to-definition). Confirm that none of them
  *writes* anything (no lazy fill, no in-place sort, no cache) and that none reads
  `StructureCache`, `RoomSweepCache` or `KeeperStaging`.
- **What the keepers write.** List every write reachable from `KeeperView`'s constructor and
  `KeeperAI.Think` outside the keeper's own objects. Expected: exactly the two world caches and
  the staging. `KeeperAI` draws from its own `SimRandom` (`KeeperAI.cs:40`), never the
  simulation's.
- **Mutable statics.** `grep -rn "static.*List<\|static.*\[\]\|static.*Dictionary<" src/DK2.Simulation src/DK2.AI src/DK2.Pathfinding src/DK2.Core`.
  The 2026-10-29 audit found two non-`[ThreadStatic]` statics: `CallToArmsRules.RouteScratch`
  (`CallToArmsRules.cs:233`) and `FightRules.RouteScratch` (`Combat/FightRules.cs:535`). Both
  are reached from `Tick`, which is not overlapped, so they do not block this task. Confirm
  that neither is reachable from `StateHash()` or from the keepers' code. If either is, **STOP**.
- **Anything else is a blocker. STOP** and report it.

**Step 2: Write the failing test.**

```csharp
namespace DK2.Behavior.Tests;

/// <summary>The state hash runs beside the keepers' thinking (RunOptions.OverlapStateHash); the
/// run must be identical to the serial one, tick for tick and command for command.</summary>
public class PostTickOverlapTests
{
    [InstallTheory]
    [InlineData("keepers/level11a_drako_attack.yaml")]
    [InlineData("keepers/level18_nemesis_allies.yaml")]
    public void OverlappedAndSerialRuns_AreIdentical(string relative)
    {
        Scenario scenario = ScenarioCorpus.Load(relative);
        var runner = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path);

        ScenarioResult serial = runner.Run(scenario, new RunOptions { OverlapStateHash = false });
        ScenarioResult overlapped = runner.Run(scenario, new RunOptions { OverlapStateHash = true });

        Assert.Equal(serial.HashPerTick, overlapped.HashPerTick);
        Assert.Equal(
            serial.KeeperCommandLog.Select(e => (e.InputTick, Fingerprint(e.Command))),
            overlapped.KeeperCommandLog.Select(e => (e.InputTick, Fingerprint(e.Command))));
        Assert.Equal(serial.Events.Select(e => e.ToString()), overlapped.Events.Select(e => e.ToString()));
    }
}
```

`HashPerTick`, `ScenarioCorpus.Database` and `[InstallTheory]` exist. Check the name of the
events list on `ScenarioResult` (`ScenarioRunner.cs:19-92`).
- `Fingerprint` hashes each command with `Hash64`, as `KeeperScenarioTests.cs:244-249` does.
  Copy it, or make that one shared.
- Do not compare `ToString()`: on records with list members (e.g. `TagTilesCommand.Tiles`) it
  prints the collection's type name, not its contents.

Run: expected FAIL (compile error: `OverlapStateHash`).

**Step 3: Add the option and the marker.**

```csharp
    /// <summary>Hash the world on a pool thread while the computer keepers think (both only read
    /// it). Behaviour-neutral; false runs the two one after the other, for measuring the hash
    /// alone and for checking the overlap changes nothing (PostTickOverlapTests).</summary>
    public bool OverlapStateHash { get; init; } = true;
```

In `RunClock`:
`public static readonly SimulationMarker StateHashWait = SimulationMarker.Named("state hash wait");`.
It is the time the run's thread waits for the overlapped hash after the keepers finish. The
hash's own duration shows only in serial mode.

**Step 4: Restructure the loop.**
- Extract the keepers' loop (today lines 328–335: `foreach (KeeperAI keeper in keepers) next.AddRange(...)`
  inside `clock.Begin/End(RunClock.KeeperAi)`) into a local function `List<PlayerCommand> Think()`.
- Then replace lines 314–335 with this shape. Keep everything else in the loop (journal, the
  controllers block, `pending = next`) exactly as it is.

```csharp
                clock.BeginTick(t);
                simulation.Tick(given);
                bool last = t + 1 == scenario.DurationTicks;

                ulong hash;
                List<PlayerCommand>? thought = null;
                ExceptionDispatchInfo? keeperFailure = null;
                if (mode.Options.OverlapStateHash && !last && keepers.Count > 0)
                {
                    // Both only read the world (see the audit in the commit introducing this);
                    // the keepers' views stay on this thread because they fill the world's caches.
                    Task<ulong> hashing = Task.Run(simulation.StateHash);
                    try { thought = Think(); }
                    catch (Exception ex) { keeperFailure = ExceptionDispatchInfo.Capture(ex); }
                    clock.Begin(RunClock.StateHashWait);
                    hash = hashing.GetAwaiter().GetResult();
                    clock.End(RunClock.StateHashWait);
                }
                else
                {
                    clock.Begin(RunClock.StateHash);
                    hash = simulation.StateHash();
                    clock.End(RunClock.StateHash);
                }
                hashes.Add(hash);
                completed = t + 1;
                journal?.WriteHash(t + 1, hash);
                divergence.Compare(t + 1, hash);
                monitor.AfterTick(simulation, t, given, hash);
                // As before: a keeper's failure surfaces after its tick's hash is recorded.
                keeperFailure?.Throw();
                if (last)
                {
                    clock.EndTick();
                    break;
                }
                List<PlayerCommand> next = thought ?? Think();
                emitted.AddRange(next);
                ...  // unchanged from here
```

Rules for `Think()`:
- It must call `clock.Begin(RunClock.KeeperAi)` / `clock.End(...)` in a `try/finally`, so an
  exception leaves `RunClock`'s stack balanced. `RunClock.EndTick` throws on an open marker,
  which would hide the real error.
- It must not touch `monitor`, `journal` or `divergence`.

If the hash throws in overlapped mode, `GetResult()` rethrows that exception and the keeper
failure is dropped. That matches today's order, where the hash ran first. Add
`using System.Runtime.ExceptionServices;`.

**One order change, and why it is safe.** `Think` now runs *before* `monitor.AfterTick`, not
after it. The 2026-10-29 audit found that `AfterTick` only observes:
- `StartShadow` writes a snapshot and builds a separate simulation;
- `Sample` at most fills `RoomSweepCache` (`RunMonitor.cs:77-93`, `100-123`).

Re-confirm this in Step 1's audit, and say it in the ADR.

**Step 5: Run the test.**
`DK2_REQUIRE_INSTALL=1 dotnet test tests/DK2.Behavior.Tests --filter "FullyQualifiedName~PostTickOverlap"`.
Expected: PASS. Then run the whole `DK2.Behavior.Tests`.

**Step 6: Stress check.**
- Run the `nemesis` case 20 times (drako's 1200 ticks × 2 runs × 20 is too slow):
  `for i in $(seq 20); do dotnet test ... --filter "FullyQualifiedName~PostTickOverlap&DisplayName~nemesis" --no-build || break; done`.
  Cap it at 30 minutes.
- Expected: 20 passes. A data race shows up here as an occasional failure.
- Any failure: **STOP**, set the default to `false`, and report.

**Step 7: Equivalence check.** Shared procedure against the Task 2 commit. Gates A and B: no diff.

**Step 8: Measure.**
- Expected: the `tick p50` on drako falls by roughly min(hash, keeper AI).
- `state hash wait` should be near zero when keeper AI is the longer side.
- Harkan (one keeper, tiny AI) should be unchanged within noise.
- Run the speed-budget gate. It must pass.

**Step 9: Docs.**
- ADR-0578: "The harness hashes the world while the keepers think". Cover the read-only audit
  result, the exception ordering, and `OverlapStateHash`.
- Name the two cache writes as the reason views stay serial.

**Step 10: Commit.** `harness: hash the world while the computer keepers think`

---

### Task 4: Record what is not being done, and fix a false comment

**Files:**
- Modify: `src/DK2.AI/KeeperRouteCache.cs:9-14` (summary)
- Modify: this plan (append a "Not done" section)

**Step 1: Fix the comment.**
- It says "a router keeps nothing from one query to the next, so a reused router answers as a
  new one would". That is false: the mesh's locate cache (`NavMesh._locateCache`, ADR-0486)
  carries over between queries and between turns, and decides edge and vertex answers.
- Rewrite the summary to say what is true:
  - it keeps a keeper's last mesh while its bytes are unchanged;
  - a reused mesh's answers may differ from a fresh one's, because the locate cache carries
    over;
  - that is deterministic because the cache belongs to one keeper;
  - `KeeperStaging` shows how to hand out a reused mesh as fresh, if that is ever wanted.
- **Do not change the behaviour:** it would move keeper decisions. Behaviour only, no
  `HashVersion` impact, but the equivalence check must still show no diff.

**Step 2: Add a "Not done" section** to the end of this plan, with the measurements:
- **Running each keeper's `Think` in parallel.**
  - Safe (no shared mutable state; per-keeper `SimRandom`, state and route cache).
  - Worth ≤ 0.2 ms/tick on drako: `Think` is 0.8 ms of the keeper AI, and the slowest keeper
    is 0.5–0.56 ms.
  - Not built.
- **Narrowing `nav.refresh`'s cache key.**
  - `WorldSteering.Unchanged()` compares the whole `TileState` array, so health, gold, fog and
    tag writes force a navigation-byte refill 8–18 times a tick.
  - The ceiling is its self-time, ≈ 0.4–0.5 ms/tick.
  - A narrower key would have to name every input of the fill (terrain, owner, secret door,
    doors, ruined hearts, crypts, nav stamps).
  - Not built.
- **Building keeper views in parallel.**
  - Unsafe as the code stands. `StructureBlocks.For` rewrites `world.StructureCache.Scratch` on
    every call, even on a cache hit (`StructureBlocks.cs:81-93`). So even pre-warmed caches
    cannot be read from two threads.
  - Not built.
- **Sharing the navigation bytes between views (a serial saving).**
  - Each `KeeperView` refills the whole map's navigation bytes (`CaptureNavigationBytes`,
    `KeeperView.cs:49`), and the staging steering fills them again.
  - They are a pure function of the world at that tick, so `KeeperStaging` could hand out one
    copy.
  - Record what Task 2's profile showed it costs. Not built.
- **Two mutable statics that block any in-process parallel runs.**
  - `CallToArmsRules.RouteScratch` (`CallToArmsRules.cs:233`) and `FightRules.RouteScratch`
    (`Combat/FightRules.cs:535`) are shared `List`s, not `[ThreadStatic]`.
  - `CallToArmsRules`' list is never cleared, so it grows for the life of the process, Unity
    included.
  - Only the bool result is used, so hashes are unaffected. But a concurrent `List.Add` can
    throw. That blocks parallel `run <dir>` scenarios, shadow ticks, and today's xUnit
    class-level parallelism (no `xunit.runner.json` or `CollectionBehavior` exists).
  - The fix is `[ThreadStatic]` plus `Clear()`, as `ApproachRules.cs:77` does. It is
    behaviour-neutral. These are the only such statics in DK2.Simulation, DK2.AI, DK2.Core,
    DK2.Pathfinding, DK2.Data and DK2.Harness: this is the "review of mutable static state" that
    the autonomous-playtesting plan §6 asks for.
  - Not built here; propose it to the owner as its own task.
- **The creature and imp updates.** Serial by definition (see *Hard constraints*).
- **Task 3**, if its gate skipped it, with the measured min(hash, keeper AI).

**Step 3: Run `dotnet test tests/DK2.AI.Tests` and commit.** `ai: KeeperRouteCache says what a reused mesh carries`

---

### Task 5: `watch --jobs N` runs scenarios in parallel processes

**Step 1: STOP. Owner decision required.**
- `docs/plans/2026-10-27-autonomous-playtesting.md` §6 (lines 67 and 71) says "one worker", and
  "Parallel workers wait for a review of mutable static state". ADR-0552 has no §6; the
  `WatchLimits` comment (`Watchdog.cs:12`) cites "ADR-0552 §6" for it, and says "one run at a
  time, always".
- The static-state review is now done: see the two `RouteScratch` statics in Task 4's "Not done"
  list. They do not affect `--jobs`, because each run is its own process.
- Each run is its own child process (`Watchdog.SelfRun`), so in-process static state is not
  shared between runs. Present that to the owner, with the risks:
  - CPU contention can turn a slow run into a false `timeout` verdict;
  - each child loads its own `GameDatabase`, so memory is about N times one run's;
  - `watch-summary.tsv` and console lines need a fixed order.
- Ask for approval to amend ADR-0552 and the playtesting plan's §6. Without it, do not
  implement this task.

**Files (after approval):**
- Modify: `src/DK2.Harness/Watchdog.cs` (`WatchLimits`, `_current`, `Cancel`, `Watch`, `RunOne`)
- Modify: `src/DK2.Harness/Program.cs:~257` (parse `--jobs`) and the usage text at ~38
- Modify: `src/DK2.Harness/README.md` (the `watch` line and paragraph)
- Test: `tests/DK2.Behavior.Tests/WatchdogTests.cs`

**Step 2: Write the failing tests.** Use the existing pattern: the `launch` delegate returning a
`ProcessStartInfo`, temp scenario files, `TextWriter.Null`. To make a child slow, launch a
command that sleeps:
- on Windows: `new ProcessStartInfo("powershell", "-NoProfile -Command Start-Sleep -Seconds 2")`;
- elsewhere: `new ProcessStartInfo("sleep", "2")`;
- choose with `OperatingSystem.IsWindows()`.

```csharp
    [Fact]
    public void WithTwoJobs_TwoRunsOverlap()
    {
        // Relative, not absolute: PowerShell takes 0.5-1 s to start on Windows.
        TimeSpan serial = TimeWatch(jobs: 1), parallel = TimeWatch(jobs: 2);
        Assert.True(parallel < serial * 0.75, $"jobs 1: {serial}, jobs 2: {parallel}");

        TimeSpan TimeWatch(int jobs)
        {
            var limits = new WatchLimits(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), 20, Jobs: jobs);
            var clock = Stopwatch.StartNew();
            new Watchdog(Path.Combine(_root, $"out{jobs}"), limits, (_, _) => Sleep(2)).Watch([_good, _good], TextWriter.Null);
            return clock.Elapsed;
        }
    }

    [Fact]
    public void TheSummary_ListsRunsInJobOrder_WhateverOrderTheyFinish()
    {
        // Job 1 sleeps 2 s, job 2 finishes at once; the summary's rows still read 001 then 002.
        ...
        string[] rows = File.ReadAllLines(Path.Combine(summary.Root, "watch-summary.tsv")).Skip(1).ToArray();
        Assert.Equal(["first-label", "second-label"], rows.Select(r => r.Split('\t')[1]));
    }

    [Fact]
    public void Cancel_KillsEveryRunningChild() { /* jobs 2, two 30 s sleeps, Cancel after 1 s, the watch returns within 10 s and both verdicts are "cancelled" */ }

    [Fact]
    public void JobsBelowOne_AreRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WatchLimits(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), 1, Jobs: 0));
```

Use `WatchJob` labels to tell the rows apart. Run: expected FAIL (no `Jobs`).

**Step 3: Implement.**
- **`WatchLimits`:** add `int Jobs = 1` as the last positional parameter. Validate it in the
  record's body (`Jobs >= 1`). Update its summary: "at most `Jobs` runs at a time, each in its
  own process (ADR-0552 as amended; playtesting plan §6)".
- **Children:** replace `Process? _current` with a `HashSet<Process>` guarded by a `lock`.
  `RunOne` adds its child after `Start()` and removes it after the final `WaitForExit`.
  `Cancel()` sets `_cancelled`, then kills every child in the set (copy the set under the lock,
  kill outside it).
- **Launching:**
  - Keep `Watch`'s checks (cancelled, `MaxRuns`, budget) and run numbering exactly as today,
    evaluated in job order *at launch time*.
  - Launch while fewer than `Jobs` runs are active. Use a `SemaphoreSlim(Jobs)` acquired before
    each launch decision, and run `RunOne` in `Task.Run`, releasing in `finally`.
  - The per-run limit is still `min(PerRun, Total - elapsed)`, taken at launch.
- **Output order:**
  - Store each `Outcome` in an array by run number.
  - After any run completes, append summary rows and log lines for the longest finished
    prefix, in run order, under a lock. `watch-summary.tsv` and the console then read exactly as
    a serial watch's would, and a parent that dies mid-watch leaves a valid prefix.
  - After the loop, wait for all tasks and flush the rest.
- **Return value:** `WatchSummary` counts as today.

**Step 4: Parse `--jobs`** in `Program.cs` next to `--max-runs`: default 1, positive whole
number, and refuse values above `Environment.ProcessorCount` with the same usage error style.
Add it to the usage line.

**Step 5: Run the tests.** `dotnet test tests/DK2.Behavior.Tests --filter "FullyQualifiedName~Watchdog"`.
Expected: all pass, the old ones unchanged (they use the default `Jobs = 1`).

**Step 6: Real run and measurement.**

```bash
dotnet $H watch scenarios/keepers --out build/perf/watch-j1 --jobs 1
dotnet $H watch scenarios/keepers --out build/perf/watch-j4 --jobs 4
```

- Record wall time for both.
- Record peak memory for `--jobs 4`: watch Task Manager, or sample with
  `Get-Process DK2.Harness | Measure-Object WorkingSet64 -Sum` every second.
- Compare verdicts: `diff <(cut -f1-4 build/perf/watch-j1/watch-*/watch-summary.tsv) <(cut -f1-4 build/perf/watch-j4/watch-*/watch-summary.tsv)`
  must print nothing. The `seconds` column is excluded.
- Compare the run folders:
  `diff -r -x stdout.log -x stderr.log -x input.journal -x timings.txt -x watch-summary.tsv build/perf/watch-j1/watch-*/ build/perf/watch-j4/watch-*/`
  must print nothing. `stdout.log` names the output path, which differs between the watches
  (`Program.cs:128`).
- The 10 keeper scenarios fit within the defaults `--max-runs 20` and `--budget-minutes 30`.

**Step 7: Docs.**
- Add a dated **Amendment** paragraph to ADR-0552, in the style of its existing Stage B/C
  amendments.
- Edit `2026-10-27-autonomous-playtesting.md` §6, lines 67–71, in place (*Replace, do not
  append*), and link the amendment.
- Update the `WatchLimits` comment at `Watchdog.cs:12`.
- Update the harness README's `watch` paragraph: `--jobs`, the order guarantee, and the
  timeout caveat ("a contended run is slower; raise `--timeout-seconds` with `--jobs`").
- Pre-existing and unchanged: two *concurrent* `watch` commands can collide on the
  millisecond-stamped root folder. Mention it in the README; do not fix it here.

**Step 8: Commit.** `harness: watch --jobs runs scenarios in parallel processes`

---

## Every commit

- **Build first.** `dotnet build DK2.slnx -c Release`, so the commit hook can run `DK2.Tools`:
  docs budget, `kb check --staged` and `status --check`.
- **Never bypass the hook.**
- **Message format:** subject in the repo's style (lower case, scope prefix as above). Trailer
  `Assisted-by: <tool> <address>`, never `Co-Authored-By` for an AI (the hook rejects it).
- **Status line.** The hook's status check (`.githooks/commit-msg:55-66`) only warns. The status
  paragraph counts tests and ADRs (`StatusCommand.cs:150-158`), so every task that adds a test
  or an ADR makes it stale, not only Task 1. In each such task, regenerate it as in Task 1
  Step 11, and confirm `status --check` says "agrees with the tree".
- **One task, one commit** (Task 1 has two). Do not push.
- **After each task,** update `docs/HANDOFF.md` only where it states something this task made
  false (for example a hash version it names). Otherwise leave it.

## Done means

- Tasks 0–4 merged, and Task 5 either merged after owner approval or recorded as declined.
- `DK2_REQUIRE_INSTALL=1 dotnet test DK2.slnx -c Release` passes.
- `status --check` agrees.
- The speed-budget gate (ADR-0575) passes. Its Level 1–4 totals and worst tick, against Task 0's
  baseline, are recorded in the last commit's body.
- Drako's median tick, against Task 0's baseline, is recorded in the last commit's body.
