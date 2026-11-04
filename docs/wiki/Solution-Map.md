# Solution map

What each part of the repository holds and how the parts depend on each other.
The authoritative version, including the build-target rules, is
[ARCHITECTURE.md](https://github.com/franjom/netherhold/blob/master/docs/ARCHITECTURE.md).
The `.csproj` references are the final authority.

## Layering

Dependencies point downward only. Only the Unity project references `UnityEngine`.

```
unity/DK2.Unity            the only assembly that references UnityEngine
        │                  (loads the netstandard2.1 DLLs below)
DK2.Tools / DK2.Harness / DK2.Convert       net10.0 executables and tooling
        │
        ├──────────────► DK2.Presentation ──► DK2.Assets ◄── DK2.Art (KMF, textures)
        │                       │                  │            │
DK2.AI ──────► DK2.Simulation ──┘                  │
        │         │                                │
        │         └──► DK2.Pathfinding             │
        └──────► DK2.Data.Model ◄── DK2.Data (parsers)
                       │
                   DK2.Core                netstandard2.1 (Unity-loadable)
```

The game assemblies target **netstandard2.1** so that Unity can load them. The
executables, tools and tests target **net10.0**.

## Projects in `src/`

| Project | Role | Depends on |
|---|---|---|
| **DK2.Core** | Primitives with no game rules: fixed-point `Fp32`, `GameTick`, `TilePos`, hashing, the seeded `SimRandom`, `Confidence` and the `Dk2Behavior` attribute | — |
| **DK2.Data.Model** | Immutable game definitions with no IO. This is the seam between data and simulation | Core |
| **DK2.Data** | Readers for the original data (KWD, KLD, SDT, maps, audio and movies), plus campaign, save, settings and high-score storage | Core, Data.Model |
| **DK2.Pathfinding** | The original game's router (`NavMesh`, `MeshRouter`, `Funnel`) | Core |
| **DK2.Simulation** | All game rules and all mutable state (`WorldState`), advanced only by `GameSimulation.Tick` | Core, Data.Model, Pathfinding |
| **DK2.AI** | The computer keeper, which acts as an input source to the simulation | Core, Data.Model, Simulation |
| **DK2.Assets** | Logical asset keys and how they are resolved. All asset access goes through this layer | Core, Data.Model |
| **DK2.Art** | Readers for the player's original art: KMF models, engine textures and the art catalogue | Assets, Data |
| **DK2.Presentation** | Turns world state into drawables, independent of the renderer: HUD, panels, camera paths, cutscenes | Core, Data, Data.Model, Simulation, Assets |
| **DK2.Convert** | Converts original art to glTF/GLB and PNG | Art, Assets, Data |
| **DK2.Harness** | Headless scenario runner: runs YAML scenarios and writes reports, events and per-tick hashes | Core, Data, Data.Model, Simulation, AI, Assets, Presentation |
| **DK2.Tools** | The `dk2tools` command line: `status`, `docs`, `kb`, `coverage`, `convert`, `replay`, `simmatrix` and the data inspectors ([README](https://github.com/franjom/netherhold/blob/master/src/DK2.Tools/README.md)) | Assets, Convert, Core, Data, Data.Model, Simulation |
| **Shared** | `UnityBound.props`, which sets the netstandard2.1 target from inside each Unity-loadable project, and polyfills | — |

## Unity: `unity/`

| Folder | Role |
|---|---|
| **DK2.Unity** | The Unity project and presentation adapter: rendering, cameras, input and HUD. It holds no game state and decides no rule ([README](https://github.com/franjom/netherhold/blob/master/unity/README.md)) |
| **DK2.Unity.Compile** | Builds the adapter against Unity's modules outside the editor |
| `sync-assemblies.ps1` | Builds the shared assemblies and copies them into the Unity project. Rerun it after changing `src/` |

## Tests: `tests/`

| Project | Covers |
|---|---|
| DK2.Core.Tests, DK2.Data.Tests, DK2.Simulation.Tests, DK2.AI.Tests, DK2.Assets.Tests, DK2.Presentation.Tests, DK2.Convert.Tests, DK2.Tools.Tests | Unit tests for the project of the same name |
| DK2.Behavior.Tests | Behaviour checked against the real shipped data (`*OnRealDataTests`). These are skipped without `DK2_DIR` |
| DK2.Oracle.Tests | Checks our code against the original: runs functions from `DKII.EXE` and compares with state captured from the running game |
| DK2.TestSupport | Shared fixtures and helpers |

Architecture tests read the *built* assemblies to check that no simulation
assembly references Unity and that the Unity-loadable ones really target
netstandard2.1.

## Documentation and research: `docs/`

| Path | Holds |
|---|---|
| [`HANDOFF.md`](https://github.com/franjom/netherhold/blob/master/docs/HANDOFF.md) | Current state and the next useful action |
| [`DECISIONS.md`](https://github.com/franjom/netherhold/blob/master/docs/DECISIONS.md) | Architectural and behavioural decisions (ADR-numbered) |
| [`ARCHITECTURE.md`](https://github.com/franjom/netherhold/blob/master/docs/ARCHITECTURE.md) | Layering, simulation rules, system order, world state |
| [`TRAPS.md`](https://github.com/franjom/netherhold/blob/master/docs/TRAPS.md) | Known pitfalls |
| [`research/`](https://github.com/franjom/netherhold/tree/master/docs/research) | One document per DK2 mechanic (combat, rooms, spells, camera, lighting, campaign…), plus open questions |
| [`research/functions/`](https://github.com/franjom/netherhold/tree/master/docs/research/functions) | The function knowledge base: one record per `DKII.EXE` function, named by address |
| [`experiments/`](https://github.com/franjom/netherhold/tree/master/docs/experiments) | Comparisons with the original game: idle-trigger runs and packet-replay sweeps |
| [`plans/`](https://github.com/franjom/netherhold/tree/master/docs/plans) | Dated design and implementation plans |

## Everything else

| Path | Holds |
|---|---|
| [`scenarios/`](https://github.com/franjom/netherhold/tree/master/scenarios) | Declarative YAML experiments by topic (creatures, fog, imps, keepers, mana, rooms, smoke, spells, tiles, triggers) ([guide](https://github.com/franjom/netherhold/blob/master/scenarios/README.md)) |
| `tools/ghidra` | Headless Ghidra queries against `DKII.EXE` and the per-agent MCP server |
| `tools/kb` | Function knowledge base tooling |
| `tools/oracle` | State and memory capture from the running original game |
| `tools/blender` | Repeatable Blender scripts for asset work |
| `tools/*-probe`, `tools/meshreplay` | Unity and animation probes and mesh replay, used for visual verification |
| `reverse-engineering/` | Method and notes for the binary work |
| `original-reference/` | Reference material about the original game; no game files |
| [`DK2_REIMPLEMENTATION_PLAN.md`](https://github.com/franjom/netherhold/blob/master/DK2_REIMPLEMENTATION_PLAN.md) | The overall plan, milestones and acceptance criteria |
| [`AGENTS.md`](https://github.com/franjom/netherhold/blob/master/AGENTS.md) | Contributor and AI-agent rules: source order, evidence storage, documentation budgets |

## Where does a change belong?

- **A game rule or a balance behaviour:** `DK2.Simulation`, with numbers from `DK2.Data.Model`
  and a research note in `docs/research/`.
- **Something you see on screen that doesn't depend on Unity:** `DK2.Presentation`.
- **Unity-specific rendering, input or editor work:** `unity/DK2.Unity`.
- **Reading a new original file format:** `DK2.Data` (or `DK2.Art` for models and textures).
- **The computer opponent:** `DK2.AI`.

For the detailed version, see
[HANDOFF.md, "Where to work"](https://github.com/franjom/netherhold/blob/master/docs/HANDOFF.md).
