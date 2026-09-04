# Handoff notes

Working knowledge that would otherwise live only in chat history, per
`AGENTS.md` ("Do not leave important knowledge only inside chat history").
Update this whenever a session ends or context is compacted.

---

## Current state (2026-09-04)

**M3 tile mechanics, the first worker, rooms + the treasury, mana, the gold
carrier, where imps come from, **where every other creature comes from**, the
asset layer and the presentation boundary: complete, and each level now plays on
its own balance numbers. M3 as a whole: substantially complete — the Unity view
renders campaign level 1.** M2 and the five record types M1 needed are done, and
**every** record type's on-disk size is now known and pinned. **573 tests green**
with and without a DK2 install; twelve scenarios run, each drawing a picture of
its finished world; clean tree. (Core 17, Simulation 313, Data 63, Behavior 95,
Assets 60, Presentation 26.)

**The first room whose behaviour exists is the portal** (2026-09-04, ADR-0042):
a claimed portal produces its level's own creatures, and **level 1 produces
nothing until its keeper builds a lair**, because the Goblin's only attraction is
a Lair. See `docs/research/portals.md`. The one number in it to be suspicious of
is the interval scaling — `max(base, base × creatures ÷ 2)`, which no shipped
file expresses and which only a stopwatch on the original can check (Q86, top of
the observation checklist).

**Not every player is a keeper** (ADR-0043, `PlayerIds`): 0 unowned, 1 the hero
side, **2 neutral — what an unclaimed room belongs to** — 3–7 keepers. Assumed
away everywhere until the portal forced the question. `ImpSupplySystem` and the
whole test suite still use the old convention; Q91, fix named, deliberately not
bundled.

**Licence: GNU GPL version 3 or later** (2026-09-04, ADR-0041). Every source file
carries the three-line SPDX notice, and `LicenseHeaderTests` enforces it rather
than an analyser. **The owner then lifted the no-copy bar entirely** — *"if any
of your doubts can be answered from OpenKeeper, feel free to copy, reuse,
whatever"* — so OpenKeeper is now a source of implementation, not only of facts,
with attribution in `THIRD-PARTY-NOTICES.md` as the price. ADR-0003 has both
amendments; the caution that came with the first use is in the fourteenth
sitting. See also `docs/research/external-resources.md`.

> ### The asset layer and the presentation boundary landed 2026-09-04 (ADR-0034, ADR-0035)
>
> **`GOAL_ALIGNMENT.md` gap 3 is closed, and it was closed while it was still
> cheap** — the gap's whole point was that the asset layer costs nothing before a
> renderer exists and a great deal afterwards, so it went in first.
>
> | | |
> |---|---|
> | `DK2.Assets` | `AssetRef` (a validated logical key), `IAssetProvider`, `AssetProviderChain` (replacement → converted → placeholder), `PlaceholderProvider` (**total**), `AssetCatalogue` |
> | `DK2.Presentation` | `WorldView` → `TileView` / `CreatureView`, and `TextRenderer` |
> | The promise, now a test | every key the shipped database implies resolves **with no DK2 install and no art pack**. The game runs as boxes and that it does is checked on every commit (plan §7.2) |
> | The second renderer | every scenario writes `world.txt` and a picture in its report, through the same view and the same provider chain Unity will use |
>
> ### The Unity view renders campaign level 1 (2026-09-04, ADR-0036)
>
> **Unity 6 LTS `6000.6.0f1`**, installed and activated. `unity/DK2.Unity/` holds
> `DungeonScene` (`IRenderTarget` in GameObjects), `DungeonRunner` (the
> `MonoBehaviour`), an editor `DungeonSetup` and a committed `Dungeon.unity`
> scene. `unity/sync-assemblies.ps1` builds the seven netstandard2.1 DLLs into
> `Assets/Plugins/DK2` (build output, gitignored — **re-run it after any change
> under `src/`**). Everything about running it is in `unity/README.md`.
>
> **`unity/DK2.Unity.Compile/` compiles the adapter against Unity's real module
> assemblies**, which is the only way this project's "do not commit code you have
> not built" rule can reach Unity code. Deliberately **outside `DK2.slnx`**: a
> machine without Unity cannot build it and CI has none. Run it by hand —
> `dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj`. It caught
> three things on its first run that reading could not.
>
> **Unity itself compiles AND runs it, headlessly.**
> `Unity.exe -batchmode` exits 0 having built
> `DK2.Unity.dll` against the seven plugin assemblies — stronger than
> `DK2.Unity.Compile`, because it is Unity resolving Unity. And
> `-executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest` runs the whole
> pipeline: campaign level 1 (30x38) loaded from a real install through Unity's
> Mono runtime, **1140 tile objects, 1178 assets all resolved as placeholders**,
> 20 ticks, present again, and it fails unless there is one object per tile. Both
> commands are in `unity/README.md`.
>
> **AND IT RENDERS.** The owner pressed Play on 2026-09-04 and got campaign
> level 1, recognisably the starting dungeon, first attempt. The diagnostics line
> in that screenshot cross-checked three claims at once: 1140 tile objects (30x38,
> matching the smoke test), `pushed 0 tiles / 0 creatures this frame` (the diffing
> works — a settled dungeon costs nothing to keep on screen), and **2 creatures at
> tick 203**, which the map does not place and which therefore came from
> `ImpSupplySystem` (ADR-0021) — the imp economy running in Unity.
>
> **It also found a bug no test could have.** The camera framed the level using
> `world.Width` for its height, so on a level 30 wide and 38 deep the near end
> fell off the bottom of the screen. Now fitted from the bounding sphere, so the
> framing cannot depend on which dimension is larger. **A smoke test proves 1140
> objects exist; it cannot prove a camera points at them.**
>
> **Zero creatures on a campaign level is correct**, not a bug: creatures live in
> the THINGS record, unparsed until M6. The harness scenarios place their own.
>
> **Why the adapter is nearly incapable of being wrong.** `ScenePresenter` and
> `IRenderTarget` (ADR-0035) took every decision a renderer could get wrong out
> of it and put it under test: what counts as a visible change, push order, which
> asset key each thing needs, create versus update versus remove. Two properties
> are pinned against a running campaign level — **presenting an unchanged world
> pushes nothing** (60 ticks push under a tenth of a whole-world redraw) and
> **presenting never changes the world** (state hash, per tick). `DungeonScene`
> never sees a `WorldState`, never resolves an asset and never picks an order.
>
> ### ⚠ And it found a four-month-old defect in a load-bearing claim
>
> **ADR-0002 said the Unity-loadable assemblies target netstandard2.1, "enforced
> by the compiler from day one". They did not, and it was not.**
> `Directory.Build.props` chose the framework behind a `Condition` on a marker
> the project bodies set *after* that file had already been evaluated, so the
> condition was always false and **every assembly built as net10.0 from M1 until
> 2026-09-04**. `Polyfills.cs` sat behind `#if NETSTANDARD2_1` and was never
> compiled.
>
> **Unity cannot load a net10.0 assembly**, so the whole presentation plan would
> have failed at the first attempt to use it. It surfaced the instant the adapter
> was compiled against Unity's assemblies: `NU1201: not compatible with
> netstandard2.1`, from the seven projects that were supposed to *be*
> netstandard2.1.
>
> Fixed via `src/Shared/UnityBound.props`, imported from each project body where
> the ordering works. **The four months cost almost nothing to unwind** — one
> `Enum.IsDefined` overload, one `Stream.ReadExactly` polyfill, and the pinned
> determinism hash did not move — **but that was luck.** Every month of
> net10.0-only API use would have made it worse.
>
> Now enforced from the built artefact rather than from configuration:
> `ArchitectureTests.UnityLoadableAssemblies_TargetNetStandard21` reads
> `TargetFrameworkAttribute` off the compiled assembly. **A constraint expressed
> only in build configuration is a hope, and "enforced at compile time" in an ADR
> title is itself a claim that needs a test.**
>
> **Three things went wrong on the way, and all three are worth keeping:**
>
> 1. **The glyph scheme drew a lie, and a picture of a real level exposed it.**
>    `TextRenderer`'s wall alphabet contained `@` and `$`, which are also the
>    creature and stored-gold glyphs — so on campaign level 1 rock variants read
>    as imps, and the dungeon heart (which really does hold gold) was
>    indistinguishable from the rock beside it. `GlyphsAreDisjoint` now fails if
>    any alphabet overlaps another. **Rendering the real thing found in one look
>    what the unit tests could not.**
> 2. **A hash cannot promise distinctness.** The replacement test first asserted
>    that the shipped terrains spread across the wall alphabet; eight terrains in
>    eight hash buckets gave five distinct symbols, which is exactly what random
>    bucketing predicts. Widening the alphabet lowers the collision rate and
>    cannot abolish it, so the test now asserts the property that matters —
>    **Rock, Gold, Gems and Impenetrable Rock are distinguishable** — and says
>    out loud that it is not a uniqueness claim.
> 3. **`AssetRef`'s separator set was a guess, and the shipped terrain table
>    refuted it on the first run.** It allowed space, dash and apostrophe;
>    `Hero Gate [ Final ]` and `Mercenary Gate [<not used>]` exist. The set is
>    now measured over all 113 shipped names, and a test normalises every one.

> ### ⚠ Read this first: six sittings of owner answers, 2026-08-29 to 09-03
>
> From playing the original, the owner has supplied answers to **Q7, Q9, Q12,
> Q20, Q21, Q24, Q25, Q26, Q27, Q28, Q30, Q31, Q32, Q33, Q34, Q36, Q37, Q37a,
> Q37b, Q38, Q40, Q42, Q43, Q44, Q45, Q46, Q47, Q48, Q49, Q52, Q53, Q54, Q58,
> Q59, Q60, Q62, Q63, Q64, Q66, Q67, Q68** plus the one-dungeon-heart rule, the four-imp floor and the Create
> Imp price. `docs/research/open-questions.md` has every one;
> `docs/research/observation-checklist.md` is the file to hand back for more.
>
> **The measurements are worth more than the yes/no answers.** A number
> propagates where a yes does not, and these rewrote more than every earlier
> answer combined:
>
> | Measurement | What it settled |
> |---|---|
> | **273 gold a swing** | Q47 — an application is a fifth of the tabled delta, five a second (ADR-0020) |
> | **2 swings to dig a plain wall** | Q20 — `DigRockHealth`'s shipped −5000 cannot produce it, so it is corrected to −500 (ADR-0023) |
> | **13.38 s to reinforce a wall** | Q62 — an upward job costs the terrain's whole `MaxHealth`, not the gap (ADR-0024) |
> | **−28 then −35 mana, 4 then 5 imps** | Q53 — 7 a second per imp, and it is on the *spell* record, not the creature (ADR-0022) |
> | **5 traps, three distinct values** | Q59 — `Traps.kwd` 0x0202, pinned by a test, unimplemented until objects (M6) |
> | **Temple capacity 8 / 12 / 16** | Q60 — a temple's capacity is its rim, `2(w+h) − 4` (ADR-0027) |
> | **16000 gold in a heart** | Q52 — nine of a heart's 25 tiles are its platform. FIXED (ADR-0027) |
> | **Room costs, 5 of them** | Q32 — `Cost` is per tile, confirmed five times over |
> | **4 imps, 5s, 1500 mana** | The imp economy, all three named by DK2's own data (ADR-0021) |
>
> **Every known defect from the original list is now fixed, and three mechanics
> arrived that were never on it** - selling (ADR-0029), a wooden bridge burning
> over lava (ADR-0032) and hostile expansion (ADR-0033), the last of which fell
> out of rules already present. The final defect,
> **Q44/Q42**, went on 2026-09-01 (ADR-0028): an imp claims a floor tile by
> standing *on* it, and the ownership test moved to that tile's neighbour. It had
> been waiting on Q49 — whether repairing, attacking and filling in work the same
> way — and Q49 was settled **without an observation**, by deriving the stance
> from two physical clauses instead of listing it per work kind. See the fourth
> lesson below.
>
> **Eight lessons from these sittings, in order of how much they cost to learn:**
>
> 1. **Ask for the quantity by name.** "How much gold per swing" got an answer
>    that "how fast does mining feel" could not have.
> 2. **Ask for values that DIFFER from each other.** Two traps both measured 5,
>    two offsets fitted, and neither could be ruled out. It was the three-valued
>    spread that identified the field. A second identical measurement cannot
>    separate a real column from a coincidence.
> 3. **Anchor a scan on what was measured, never on a community figure.** The imp
>    upkeep search failed for a whole session because it required an unverified
>    2000 to also be true; the negative it produced was therefore worthless, and
>    it hid the fact that the search was in the wrong file entirely.
> 4. **Beware the number that tidies two observations into one.** ADR-0026 capped
>    workers at three a face and explained it as "how many bodies fit on a tile".
>    ADR-0028 then reused it for a job done standing ON the tile, because the
>    single-number story made that look obviously right. It was wrong - the
>    original allows exactly one claiming imp per tile - and the wrong cap had
>    also made a test pass for the wrong reason, by letting three imps claim one
>    tile after the wall they were digging fell. **Two observations that a tidy
>    rule unifies are still two observations.**
> 5. **Check our own extracted dump before recording a number as absent.** Q34
>    was written up for four sessions as "the refund is in neither candidate
>    variable" on the strength of a mis-transcribed 100. `RoomSellValuePercentageOfCost`
>    is 50, `docs/research/game-database-dump.md` said so all along, and the
>    owner's observation matched the shipped value exactly. A number believed
>    absent sends you looking for an override you do not need.
> 6. **Count what a tile BORDERS, not what a level contains.** The bridge
>    marker (Q64) was nearly refuted by comparing each level's remaining water
>    and lava counts against its bridges - Furnace carries bridges of both
>    markers and, apparently, no water at all. The reason is that **a fully
>    bridged pond leaves no water to count**: the bridge tiles *are* the pond.
>    Asking what each tile borders made a clean partition over 66 maps visible
>    immediately. Aggregate counts can hide the very thing they are counting.
> 7. **Some questions are cheaper to derive than to observe.** Q49 asked which of
>    four floor jobs are worked standing on the tile. Asking for four observations
>    was the obvious move; the right one was to notice that *filling a tile in
>    turns it to rock*, so a worker standing on it would be entombed — and that
>    two physical clauses then answer all ten work kinds with nothing left to
>    guess. Q48 fell the same way a day earlier (a diagonal is √2 tile-widths, so
>    octile is forced). **Before writing an observation request, check whether the
>    answer is already implied by something the data says.**
> 8. **A negative from a sweep is a claim about the sweep's scope, not about the
>    game.** The record-layout sweep (2026-09-04) reported that two THINGS types
>    appear in no shipped file. One of them, the camera, has 39 records sitting in
>    `GlobalVariables.kwd` — the sweep had globbed `maps/*Things.kld` and the
>    conclusion was about the glob. This is lesson 6 in a different costume, and
>    the two together are worth stating as one rule: **before believing an absence,
>    say out loud what was searched.** Where a sweep's scope matters, the
>    test now sweeps the whole corpus and carries a comment saying why.

Three honest caveats:
- M3's definition of done asked for gold reaching a player and a Unity view.
  **Gold now does**: an imp mines into its own hands and carries the load to a
  treasury or dungeon-heart tile (Q31, ADR-0019). The Unity view does not exist.
  What is still missing from the gold loop is the **floor lump** an imp leaves
  when there is nowhere to unload — it keeps mining, which is what DK2 does, but
  what it cannot hold is lost rather than left on the floor, because a lump is an
  OBJECT (M6). A worker does marked excavation, then claims, then reinforces
  (Q24, ADR-0018); what it does when all three run out is stand still, where DK2
  would have it wander (Q41). **Imps now generate themselves** (ADR-0021): a
  keeper below `MinimumImpThreshold` gets one free every
  `TimeBeforeFreeImpGeneratedSeconds`, and can buy more with Create Imp at 1500
  mana rising by 1500 each time. And **a room is an instance now** (ADR-0027) but still
  mostly does nothing: a treasury and a dungeon heart hold gold because a
  variable says how much per tile, and a heart correctly withholds the nine
  tiles of its platform (Q52). Every other room is inert terrain its owner
  holds - though the machinery to change that finally exists, since a room can
  now be asked its size, its rim and whether it is big enough to function. **Mana is now spent AND drained** - Create Imp costs 1500 to cast and
  every imp charges its keeper 7 a second to keep (Q53, ADR-0022), which is the
  red figure under the original's mana orb. What is still missing from the
  outgoings: every other keeper spell (a summoned Horned Reaper is 2000 a second
  and Call To Arms 90, both parsed and read by nothing until M7), research, and
  what traps draw - measured, located at `Traps.kwd` 0x0202 and unimplemented
  because a trap is an object (Q59, M6). And **losing a dungeon heart is defeat in DK2 and
  defeat is not implemented**: a keeper whose heart is beaten down here stops
  earning its 30 a second and plays on (Q40, scheduled with objectives at M6).
- M1's stated definition of done was "all 18 record types round-trip". Five are
  parsed (VARIABLES, CREATURES, TERRAIN, ROOMS, MAP) plus the WAD and KWD
  containers. The rest (THINGS, TRIGGERS, PLAYERS, OBJECTS, SHOTS, …) are needed
  at M6 and scheduled there — **but no longer from a standing start**: every one
  of them now has its record size pinned against the install, and THINGS and
  TRIGGERS split into records already (`KwdTaggedRecords`). What M6 adds is
  field meanings, not structure. See `docs/research/kwd-container.md`
  §*Update 2026-09-04*.
- The CI workflow is written and every step verified locally on Windows, but it
  has never run on GitHub. The Linux leg is what turns the pinned state hash
  into a real cross-platform claim; until it runs, that claim is untested.

What can be read from the original game:

| Capability | Entry point | Validation |
|---|---|---|
| WAD archives + decompression | `DK2.Data.Wad.WadArchive` | every entry of all 5 archives extracts to declared size |
| KWD/KLD containers | `DK2.Data.Kwd.KwdReader` | 13 editor KWDs + 71 map KWDs + 330 KLDs |
| Global balance table (197 vars) | `DK2.Data.Kwd.KwdVariables` | values spot-checked vs. community docs |
| 46 creature definitions | `DK2.Data.Kwd.KwdCreatures` | ids match Flame; happy-job % sum to 100 |
| 41 terrain + 26 room types | `DK2.Data.Kwd.KwdTerrainRooms` | cross-parser: room→terrain, room→creature |
| Level tile grids | `DK2.Data.Kwd.MapGrid` → `LevelMap` | every tile of every campaign map |
| Every record type's on-disk size | `DK2.Data.Tests.KwdRecordLayoutTests` | 12 strides pinned against the install, 8 of them new and each independently reproduced by summing a published field list |
| THINGS + TRIGGERS split into records | `DK2.Data.Kwd.KwdTaggedRecords` | records carry their own size; 133 tagged chunks consumed to the byte, 11 of 12 thing payload sizes confirmed both ways |
| **All of the above, as one object** | `DK2.Data.GameDatabaseLoader` → `GameDatabase` | loads the real install; every room's terrain resolves |

What the simulation can do:

| Capability | Entry point | Validation |
|---|---|---|
| Fixed-tick loop | `DK2.Simulation.GameSimulation.Tick()` | tick advances exactly one |
| Deterministic state hash | `GameSimulation.StateHash()` | identical over 1000 ticks; diverges on seed/input change; one pinned literal |
| Save/load | `DK2.Simulation.WorldSnapshot` | restored run continues identically for 500 ticks |
| World from real map data | `WorldState.FromMap` | every campaign level builds; tile health from `TerrainDef` |
| Headless scenarios | `DK2.Harness` + `scenarios/**.yaml` | runs on a real campaign map; reproducible hash series |
| CI gate | `.github/workflows/ci.yml` | Linux + Windows, no DK2 install |
| Dig / mine / claim / reinforce / repair / attack / fill in | `DK2.Simulation.TileWorkRules` | rules run against the real install; terrain graph terminates on a whole campaign level; **a swing is a fifth of the tabled delta and a shipped seam gives up 273 gold eleven times** (ADR-0020) |
| Dig markers, first real command | `TileTagSystem` + `TagTilesCommand` | per player; cleared only when the tile can no longer carry one |
| Three workers to a wall, one to a floor tile | `ImpCrowding` | the cap is per job, not per tile (ADR-0026, ADR-0030); the surplus goes and finds other work rather than queueing |
| Workers that excavate, then claim, then reinforce | `ImpTaskSystem` + `CreatureMovementSystem` | the priority order is CONFIRMED (ADR-0018); imps dig a corridor on a synthetic map and on a shipped campaign level, then claim the floor they opened |
| Where a worker stands to do a job | `WorkStance` | a wall is worked from beside, floor from on top of it (Q44/Q49, ADR-0028); derived from two clauses rather than listed, and both clauses are pinned by a test that fails without them |
| Grid search behind an interface | `DK2.Pathfinding.GridPathfinder` | shortest, wall-respecting, reproducible; reused buffers proven inert |
| An asset named logically, resolved by a chain | `DK2.Assets.AssetRef` + `AssetProviderChain` | `replacement → converted → placeholder`; every key the shipped database implies resolves **with no DK2 install and no art pack**, and no filename literal exists outside this assembly (ADR-0034) |
| Simulation state as something drawable | `DK2.Presentation.WorldView` | one translation for every renderer; asset keys agree with `AssetCatalogue` tile by tile on a campaign level, and rendering a whole world leaves the state hash untouched (ADR-0035) |
| A second presentation mode, in CI | `DK2.Presentation.TextRenderer` | every scenario writes a picture of its finished world through the same view and provider chain Unity will use; every glyph proven to mean one thing |
| Rooms: where one may go, what it costs | `RoomRules` + `BuildRoomCommand` | placement flags partition the shipped rooms; ground rules run on a campaign level |
| Taking a rival's ground | `TileWorkRules.Applies` + the existing priority order | claimed directly, no neutral step (Q68, ADR-0033); hostile expansion fell out of rules already present rather than being built |
| A wooden bridge over lava burning away | `BridgeRules.LifetimeTicks` + `BridgeBurnSystem` | 30 s from the build, CONFIRMED (Q67, ADR-0032); the tile half only - what happens to creatures on it needs creature health |
| What a bridge is built over | `BridgeRules` | the MAP record's third byte marks a bridge over lava (Q64/Q16, ADR-0031); the partition is swept over all 66 shipped maps, and one rule now serves both selling and destruction |
| Selling a room back | `RoomRules.RefundPerTile` + `RoomSellSystem` | half the cost per tile, every number from the data (ADR-0029); `ReturnPercentage` is 0 for exactly the rooms a keeper cannot sell, asserted both ways against the install |
| Gold, stored in tiles rather than on players | `GoldStore` | per-tile limits honoured; a first treasury is paid for out of the dungeon heart |
| Mana, earned by holding land and a heart | `ManaRules` + `ManaSystem` | the three paying terrains partition the install by exhaustion; income falls when claimed floor becomes a room; a 5x5 heart is worth 30/s, not 750 |
| Gold carried from the seam to the treasury | `CreatureState.GoldHeld` + `ImpTaskSystem` | an imp mines a shipped seam on level 1 and every coin of its 3000 ends in the shipped dungeon heart, 1000 to a tile, across as many trips as its hands need |
| Creatures never inside terrain they could not walk into | `WorldPathGrid` + the waypoint re-check in `CreatureMovementSystem` | CONFIRMED (owner, Q73, ADR-0038): solid means impassable, flight buys nothing over rock. Asserted over a 4000-tick dig — no creature on a solid tile, no route in flight across one |
| A dug-out tile never closing, and two terrains never opening | the terrain table's flags, and what `ImpTaskSystem` does *not* choose | CONFIRMED (owner, Q73/Q74, ADR-0039): DK2 has no keeper-triggerable fill-in; Impenetrable Rock is unmarkable and Gems destroy to themselves. `FillInHealth` and the `FillInable` flag are kept because they are in the shipped file, and pinned as unreachable |
| A level playing on its own balance numbers | `GlobalVariables.MergedWith` + `GameDatabase.ForLevel` + `GameDatabaseLoader.LoadForCampaignLevel` | 26 variable ids are overridden by at least one shipped level, and one was already live: `MinimumImpThreshold` is 0 on four secret levels against a global 4 (ADR-0040, Q82). Pinned against the install and, separately, as merge rules without one |
| **Where every other creature comes from** | `PortalRules` + `PortalSystem` | four gates all in the data — unlocked (`Availability` kind 2), pool has room (`CreaturePool`), something attracts it (`CreatureAttraction`), under capacity (15 + 5 per extra portal). **Level 1 gives nothing until a lair exists.** ADR-0042. First consumer of the RNG. The interval scaling is OpenKeeper's, not the data's — Q86 |
| **Which players are keepers at all** | `PlayerIds` | 0 unowned, 1 hero, **2 neutral (an unclaimed room)**, 3–7 keepers. CONFIRMED three ways over all 66 levels. ADR-0043. Only `PortalSystem` honours it so far — Q91 |
| **Claiming a room somebody else holds** | `TileWorkKind.ConvertRoom` + `TileWorkSystem.SpreadTakeover` | CONFIRMED (owner, Q92, ADR-0044): **whole, never tile by tile.** One tile at `ConvertRoomHealth`'s −4000 (13 swings, 2.6s) and the room changes hands, gold included. Fixed `Claim` silently taking rooms a tile at a time since rooms existed |
| **A tile that cannot be owned carries no owner** | `TileWorkRules.Convert` + `RoomRules.Unmake` | ADR-0045. Four-month defect: a keeper who dug their own reinforced wall could never grow through the hole, which on level 1 walls them in permanently. The reason it survived was a justification resting on Q17, answered the same day elsewhere |
| Where imps come from, and the first mana spender | `ImpSupplyRules` + `ImpSupplySystem` | four free imps five seconds apart on a shipped level, then 1500 / 3000 / 4500 for the next three and a refusal for the fourth |

Extracted data artifacts (the specification the simulation is written against):
`docs/research/game-database-dump.md`, `creature-table-dump.md`,
`terrain-room-dump.md`. The last of these now carries the terrain table's
`maxHealth` column and full flag set, and the room table's full flag set
(`dk2tools kwd terrain` / `kwd terrainflags` / `kwd terrainmana` / `kwd rooms` /
`kwd roomflags`), which is what M3's rules are built on.

**Next: what rooms actually do.** The Unity view is done and renders
(2026-09-04), and so is the record-layout sweep that used to head this list.
See "Immediate next steps". Every known defect that list ever held is built, and so is selling - the
gold carrier, the work rate, the imp upkeep, the dig rate, the upward-work rule,
eight-connected movement, the crowding cap, the heart's 16000, the work stance
and the sell refund.

How this milestone stands against the twelve long-term goals in `AGENTS.md`, and
which of them have deadlines attached: `docs/GOAL_ALIGNMENT.md`. Re-run that
review at every milestone; the asset-abstraction deadline in it (goal 4 must
land *with* the first Unity view, not after) is the one that would be expensive
to miss.

---

## Where the M2 code lives

| File | What it is |
|---|---|
| `src/DK2.Core/Hash64.cs` | FNV-1a structural hash. The determinism gate's foundation. |
| `src/DK2.Simulation/GameSimulation.cs` | The loop, `StateHash()`, system registration. |
| `src/DK2.Simulation/WorldState.cs` | `TileState`, `PlayerState`, `FromMap`. All mutable state. |
| `src/DK2.Simulation/ISimulationSystem.cs` | `SimulationContext` — the only things a system may touch. |
| `src/DK2.Simulation/InputFrame.cs` | `PlayerCommand` base and the per-tick input frame. |
| `src/DK2.Simulation/WorldSnapshot.cs` | Binary save/load, RNG position included. |
| `src/DK2.Simulation/SimulationEvents.cs` | Write-only observation. |
| `src/DK2.Data.Model/GameDatabase.cs` | The data/simulation seam. Immutable, no IO. |
| `src/DK2.Data/GameDatabaseLoader.cs` | Builds one from an install; loads campaign maps. |
| `src/DK2.Harness/Scenario.cs`, `ScenarioRunner.cs` | YAML scenarios and the run report. |
| `tests/DK2.Simulation.Tests/TestWorld.cs` | Synthetic database + `ChaosSystem`. Test-only, no DK2 mechanics. |
| `docs/architecture/simulation-loop.md` | Why the gate is built the way it is. |

## Where the M3 code lives

| File | What it is |
|---|---|
| `src/DK2.Simulation/TileWork.cs` | `TileWorkKind`, `TileWorkOrder`, and `TileWorkRules` — the rules, pure and static. |
| `src/DK2.Simulation/Systems/TileWorkSystem.cs` | Drains the world's work queue each tick. |
| `src/DK2.Simulation/Systems/TileTagSystem.cs` | Applies dig markers. |
| `src/DK2.Simulation/Commands/TileCommands.cs` | `TagTilesCommand` — the first real player command. |
| `src/DK2.Simulation/SimulationTiming.cs` | **The only** place ticks relate to seconds. `TicksPerSecond` is still the Q1 placeholder; `WorkApplicationsPerSecond` is measured (ADR-0020). |
| `src/DK2.Simulation/CreatureState.cs` | The first mutable creature state. Position is fixed point; the route is state, not a cache. |
| `src/DK2.Simulation/Systems/ImpTaskSystem.cs` | Which marked tile a worker takes, and when it applies work. |
| `src/DK2.Simulation/WorkStance.cs` | **The one** rule for where a worker stands: on the tile for floor, beside it for a wall (ADR-0028). |
| `src/DK2.Simulation/Systems/CreatureMovementSystem.cs` | Walking, at the speed the creature record gives. |
| `src/DK2.Simulation/Systems/SimulationSystems.cs` | **The one** declaration of system order (ADR-0013). |
| `src/DK2.Simulation/WorldPathGrid.cs` | What "walkable" means, for one particular creature. |
| `src/DK2.Pathfinding/IPathfinder.cs`, `GridPathfinder.cs` | The Q5 placeholder, and its interface. |
| `src/DK2.Simulation/RoomRules.cs` | Where a room may go, what it costs, what it does to the tile. |
| `src/DK2.Simulation/GoldStore.cs` | **The only** way gold moves. Gold is in tiles, not on players (ADR-0015); `DepositInto` is the carrier's route, one tile at a time. |
| `tests/DK2.Simulation.Tests/GoldCarryingTests.cs` | The whole gold round trip: hands, trips, spillover, and what is lost when there is nowhere to unload. |
| `tests/DK2.Behavior.Tests/GoldOnRealDataTests.cs` | The same round trip on shipped level 1 — a real seam into a real dungeon heart. |
| `scenarios/imps/an_imp_carries_gold_home.yaml` | The trip end to end on a campaign map, and the event log that reads like DK2. |
| `src/DK2.Simulation/Commands/RoomCommands.cs` | `BuildRoomCommand` — the second real player command. |
| `src/DK2.Simulation/Systems/RoomBuildSystem.cs` | Applies it: a tile at a time, every refusal counted by reason. |
| `tests/DK2.Behavior.Tests/RoomsOnRealDataTests.cs` | The room fixture's drift guard, plus the flag-partition claim. |
| `scenarios/rooms/` | A keeper builds a treasury and cannot afford the last tile of it. |
| `docs/research/rooms-and-gold.md` | Rooms and the gold model: evidence, what falls out, what is absent. |
| `src/DK2.Simulation/ManaRules.cs` | The whole mana model: income from owned land, the cap and the ceiling. |
| `src/DK2.Simulation/Systems/ManaSystem.cs` | Pays it once a game second (ADR-0017). Runs last, after anything that changes ownership. |
| `tests/DK2.Behavior.Tests/ManaOnRealDataTests.cs` | The mana fixture's drift guard, plus the "only held ground pays" partition by exhaustion. |
| `scenarios/mana/` | Land pays a keeper; paving three tiles of it into a treasury lowers the rate. |
| `docs/research/mana.md` | Mana: evidence, why per second, and two numbers that mean nothing here (Q37a). |
| `tests/DK2.Simulation.Tests/Dk2TerrainFixture.cs` | DK2's real numbers, transcribed so the rules test in CI. |
| `tests/DK2.Behavior.Tests/TileWorkOnRealDataTests.cs` | The same rules against the real install — keeps the fixture from drifting. |
| `tests/DK2.Behavior.Tests/ImpsOnRealDataTests.cs` | Same, for the Imp record, plus imps digging a real level. |
| `scenarios/tiles/` | Dig/claim on a synthetic map; gold mining on a surveyed campaign map. |
| `scenarios/imps/` | Two imps digging a corridor from nothing but a tag command. |
| `docs/research/tile-work.md` | The tile rules: evidence, confidence tags, three anomalies. |
| `docs/research/imp-tasks.md` | The worker: evidence, what is inferred, what is deliberately absent. |
| `src/DK2.Simulation/ImpSupplyRules.cs` | Where imps come from and what they cost. Both numbers are lookups; the project has no hardcoded balance constants. |
| `src/DK2.Data/Kwd/KwdKeeperSpells.cs` | Two fields of a 406-byte record nobody has a template for - the cast cost (0x11C) and the per-second upkeep (0x00E2) - and a long note on why those two offsets are believable. |
| `src/DK2.Simulation/Systems/ImpSupplySystem.cs` | Free imps up to the floor, bought imps on command. The first system that spends mana. |
| `tests/DK2.Behavior.Tests/KeeperSpellsOnRealDataTests.cs` | Makes both unparsed-record offsets earn their place: 13 plausible costs, and an upkeep column that is non-zero for exactly the five spells whose result persists. |
| `scenarios/imps/a_keeper_never_runs_out_of_imps.yaml` | The imp economy end to end on a campaign map. |
| `docs/research/imps-and-mana.md` | Imps, the Create Imp price, and **the mana drain research** — the biggest open hole. |
| `src/DK2.Data/Kwd/KwdTaggedRecords.cs` | THINGS and TRIGGERS, which carry their own record sizes. Stops at the tag and the payload bytes on purpose — decoding a payload is M6's job, and doing it early would be guessing. |
| `tests/DK2.Data.Tests/KwdRecordLayoutTests.cs` | Every record type's stride, pinned against the install. **A document cannot fail; this can.** |
| `src/DK2.Assets/AssetRef.cs` | **The one** way an asset is named, and the only place that validates a key. Its `[a-z0-9_]` rule is what makes the key-to-path mapping safe, not tidiness. |
| `src/DK2.Assets/PlaceholderProvider.cs` | **Total on purpose.** The "runs with no assets" promise is only true if this never fails, and `AssetCatalogueTests` checks it over the whole shipped database. |
| `src/DK2.Assets/FileAssetProvider.cs` | **The only** type that turns a key into a path. Reads `build/converted` and an art pack; knows nothing about DK2's own formats. |
| `src/DK2.Assets/AssetCatalogue.cs` | Every key the database implies — what a converter must produce, and what makes the placeholder's totality testable. Claims only what the game asks for. |
| `src/DK2.Presentation/WorldView.cs` | **The one** place world state becomes something drawable. Every renderer consumes this; two renderers disagreeing here would look like a simulation bug. |
| `src/DK2.Presentation/TextRenderer.cs` | The second presentation mode, and the one that runs in CI. Read it before writing the Unity adapter — same job, character grid. |
| `src/DK2.Presentation/IRenderTarget.cs` | **The Unity seam.** Six methods, no logic. Deliberately small because it is the one thing that cannot be compiled here. |
| `src/DK2.Presentation/ScenePresenter.cs` | Diffs the world and pushes only what changed, in a fixed order. **Everything the adapter would otherwise have to get right, tested here instead.** |
| `src/Shared/UnityBound.props` | **Imported from each Unity-bound project body, and the comment explains why it cannot live in `Directory.Build.props`.** Four months of a silently-broken target framework are written on it. |
| `unity/DK2.Unity/Assets/DK2/DungeonScene.cs` | `IRenderTarget` in GameObjects. Arithmetic and `Instantiate`; the only judgement is "file or placeholder". |
| `unity/DK2.Unity/Assets/DK2/DungeonRunner.cs` | The `MonoBehaviour`: owns a simulation, ticks it, presents it. Holds no game state. |
| `unity/DK2.Unity.Compile/` | **Compiles the adapter against Unity's real assemblies.** The only way "do not commit code you have not built" reaches Unity code. Outside `DK2.slnx` on purpose. |
| `unity/README.md` | How to run it, and four things it deliberately does not do. |
| `docs/research/wall-rendering.md` | How DK2 actually draws walls (Simon Schreibt, verified vs inferred separated). Corroborated `HealthPermille`, refuted one-mesh-per-terrain, and produced Q71/Q72. Rendering only — **no rule may depend on it**. |
| `docs/research/external-resources.md` | **The licence boundary, once**, plus five worked cross-checks. OpenKeeper is GPLv3 and so is this project since 2026-09-04 - so "no code" is now a **project standard, not a licence rule**: facts with provenance, never code, because the brief asks for clean-room work and the owner asked for reference-and-compare. **It has confirmed three scan-derived offsets and every record type's size** (eight of them new, each to the byte), the MAP flags byte from a second direction, and one place where its own layout is incomplete (Q70). Read before researching anything that might be answered outside this repo. |
| `docs/research/observation-checklist.md` | **Hand this to the owner.** Every open question a person playing DK2 can settle by looking, as experiments — and which ones playing cannot settle. |

## Running things

```
dotnet build DK2.slnx
dotnet test  DK2.slnx
DK2_DIR=/nonexistent dotnet test DK2.slnx      # what CI sees: install-gated tests skip
dotnet run --project src/DK2.Harness -- list
dotnet run --project src/DK2.Harness -- run scenarios
```

Harness output lands in `build/scenario-runs/<id>/` (gitignored) — each run now
writes a `world.txt` picture of the finished dungeon alongside the hashes and the
event log.

**Inspecting the original's data** (needs an install; `dotnet run --project
src/DK2.Tools --` , written as `dk2tools` throughout the docs):

```
dk2tools where                        # resolved install path
dk2tools kwd rooms                    # the ROOMS table, cross-checked against the 1999 manual
dk2tools kwd terrain                  # the 41 terrain types with flags and transitions
dk2tools kwd creatures                # all 46, and `kwd creature <name>` for one in full
dk2tools kwd vars                     # the global balance table (203 entries)
dk2tools kwd vars level1Variables.kld -a   # one level's own overrides, pool and availability
dk2tools kwd alterterrain -v          # every ALTER_TERRAIN_TYPE trigger in every shipped map,
                                      #   with what each target tile starts as (Q74)
dk2tools kwd recscan / creaturescan / creaturehex     # offset hunting
```

**Unity — none of it is in `DK2.slnx`, and that is deliberate** (CI has no Unity,
and a machine without it must still build everything else):

```
powershell -File unity/sync-assemblies.ps1        # after ANY change under src/
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj

"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics \
  -projectPath unity\DK2.Unity -logFile build\unity-batch.log
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics \
  -projectPath unity\DK2.Unity -logFile build\unity-smoke.log \
  -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest
```

The last one is the valuable one: it runs the whole pipeline inside Unity against
a real install and fails unless there is one GameObject per tile. **Exit 198 with
`Found 0 entitlement groups` means the editor is not activated**, not that
anything is broken. Full notes in `unity/README.md`.

---

## Immediate next steps (rest of M3)

**The order changed on 2026-08-29.** The owner's answers turned four research
questions into ordinary implementation work, and that work now comes before the
Unity view, because every one of them is a behaviour a renderer would display
incorrectly.

**Where to pick up, 2026-09-03 (sixth sitting).** **Every known defect from the
original list is fixed, selling is built, and Q64 - which was recorded as "known
wrong, reproduced deliberately" the same day - is fixed too.** Q31 the gold carrier, Q47 the
work rate, Q53 the imp mana upkeep, Q20 the dig rate, Q62 the upward-work rule,
Q28/Q48 eight-connected movement, Q43 the crowding cap, Q52 the heart's 16000,
Q44/Q42 the work stance and Q34 selling are all built. What is left, in the order
it is worth doing:

1. ~~**Sweep the unparsed record layouts against OpenKeeper.**~~ **DONE
   2026-09-04.** Every KWD record type now has its on-disk size, pinned by a
   test against the install: **eight new sizes, every one confirmed to the byte
   on the first attempt** (TRAPS 579, DOORS 616, OBJECTS 894, SHOTS 239,
   PLAYERS 205, CREATURE_SPELLS 266, EFFECTS 246, EFFECT_ELEMENTS 182). **And
   THINGS and TRIGGERS turned out to need no size at all** - each record carries
   its own, so a chunk splits with zero layout knowledge, validated over all 133
   tagged chunks the install ships. Tables, method and the one disagreement:
   `docs/research/kwd-container.md` §*Update 2026-09-04* and
   `external-resources.md` §*Worked example 3*. **What is left of this item is
   the field-by-field layouts, and it belongs with the mechanic that needs
   them** - M6 can now start parsing without deriving a single stride first.

2. ~~**Unity presentation.**~~ **DONE 2026-09-04** (ADR-0034 / 0035 / 0036).
   Gap 3 closed, the asset layer and presentation boundary built, the adapter
   written, and the owner pressed Play and got campaign level 1 first attempt.
   Full account at the top of this file and in `unity/README.md`.

   **What is left is not the view but the game**, and none of it is renderer
   work: **no input** (nothing builds an `InputFrame` yet, so you can watch a
   dungeon and not touch it), **no art loading** (there is no converter —
   plan §7.1), and **boxes rather than DK2's walls** (Q71).

3. ← **START HERE: what rooms actually do.** This is the next item, agreed with
   the owner on 2026-09-04.

   **The machinery is finally in place.** `RoomInstance` (ADR-0027) gives a room
   its tile count, its bounding box, its rim and whether it is big enough to
   function; `RoomRules` handles placement, cost and selling; a room's tiles are
   owned terrain that a keeper pays for and can sell back. **What no room except
   the treasury and the heart has is a behaviour.**

   **Expect research, not coding — but read `docs/research/manual-1999.md`
   first, because the research got a great deal cheaper on 2026-09-04.** The
   owner supplied the shipped 1999 manual, and it says what every room is *for*.
   Paired with the balance table, which has a per-second rate for nearly all of
   it, most rooms now have a described purpose and a number:

   | room | the manual says | the table says |
   |---|---|---|
   | Lair | creatures return below a health threshold and recover health and happiness; each owns a **bed**, and hostile roommates hurt | sleeps below **50%**, **+100** health/s, **−550** anger/s, **+150** anger/s beside a hated creature |
   | Training Room | levels a creature and **charges the keeper per turn** | caps at level **4**; `ModifyPlayerColdWhileTraining…` is a typo for **Gold**, −5/s |
   | Combat Pit | levels past 4 by fighting; winners gain happiness | caps at **8**, 10% chance of a kill, damage at **20%** of normal, victor **−16400** anger |
   | Library | research; **and it is stored in the room** — capture it and the spells are lost | `MotionlessWhileResearching` **70**s, plus per-creature `ResearchPerSecond` |
   | Prison | prisoners wither and rise as **Skeletons**; overcrowding causes jailbreak | **−3** health/s, `CreatedCreatureId` = 9 (Skeleton), jailbreak **50%** per extra creature |
   | Graveyard | corpses accumulate life-force until a **Vampire** rises | **1000** needed, `CreatedCreatureId` = 8 (Vampire) |
   | Casino | a payout lever, Smiley or **$** | every gambling variable comes in a `Smiles`/`Money` pair, and they match the lever exactly |
   | Temple | praying makes mana, more for Thinkers, double for converts; a sacrificial pool gives gifts | **−275** anger/s, **+33** health/s, **44** sacrifice recipes |
   | Workshop | a FIFO queue; more workers is faster | progress **decays at 1/s with nobody working** |
   | Guard Room | creatures stay put and patrol nearby Guard Posts | wander after **60**s, **7**-tile reporting radius |
   | Portal | the creature cap | **15** for the first portal, **5** per extra |

   So the method is still the one written up under *Research method* below —
   **ask the owner first** (step 0 has repeatedly beaten reading the files) — but
   the questions to ask are now sharp ones about *shape* rather than open ones
   about purpose.

   **Suggested order, easiest evidence first:** the **lair** (the manual gives it
   an almost complete model, and the only real unknown is capacity — note it is
   **beds, not tiles**, so `RoomInstance.TileCount` may be the wrong measure),
   the **library** (a rate per creature and a surprising ownership rule), the
   **training room** (Q63 already measured 20 gold at a dummy and 0 at the
   target, and the manual explains the per-second keeper cost), then the
   **hatchery** — which is **blocked on objects (M6)**, because a chicken is one.

   **Two traps worth naming before starting.** First, a room's *capacity* rule is
   not one rule: Q60 established that a temple's is its **rim**, `2(w+h) − 4`,
   which nobody would have guessed, so do not assume "capacity = tile count" for
   any room without evidence. **The manual strengthens this trap rather than
   weakening it**: it says the dungeon heart stores gold in its *"outer ring"*,
   so the rim is a DK2-wide idiom, and it says a lair holds **beds** — one per
   creature, placed by preference — which is a count of objects, not of tiles.
   Second, several rooms need creatures to have needs, and **creatures currently
   have no health, hunger, mood or experience** — `CreatureState` carries only
   what a built mechanic reads. A room whose behaviour needs a field the
   simulation does not have is blocked, and saying so is better than inventing
   the field. The manual makes it very tempting to invent them, since it names
   all four.

**A rival claims your ground directly, and hostile expansion fell out of it**
(Q68, ADR-0033). Worth reading for the method rather than the mechanic: **both halves
of the owner's sentence were rules this project already had**, arrived at from
unrelated evidence. "Jumping on it" is `WorkStance`'s on-the-tile stance, which
falls out of "standable while working and standable when the job finishes" - a
bridge is walkable and stays a bridge, and nobody had considered bridges when
that rule was written. "Adjacent to their keeper's territory" is the ownership
condition on the claimed tile's neighbour (Q42). Two derived rules predicting an
observation neither was built from is the best evidence either has had. What is
missing is not bridge-specific: `Claim` takes only unowned ground, so **hostile
expansion is unbuilt in general** - no imp ever chooses `AttackTile` and
`JobPriority` has no entry for taking enemy ground - <b>that was true until this
sitting and is not any more</b>. Asked whether a rival tile goes neutral on the
way, the owner said no: it is claimed directly. That refuted the two-step model,
and fixing `Claim` to take ground that is merely *not yours* turned out to be the
whole of hostile expansion - claiming was already second in the priority order,
already required adjacency, and was already done standing on the tile. A
milestone's worth of mechanic arrived as two lines, because the rules around it
were right. **`AttackTileHealth` now has no user at all** (Q69).

**A wooden bridge over lava burns away, and the tile half is built** (Q67,
ADR-0032). The owner answered the shape the same day it was asked: the clock
starts at the build, everything on the tile falls into the lava, a dead body
disappears, and a living creature burns until it escapes or dies. Only the tile
half is implemented, because **creature health does not exist** - and lava
damage is not a bridge rule anyway, it is `TerrainDef.Damage` (100 a second, the
same field that heals in a Hero Lair), so it belongs with creature health
generally. The immunity is already in the data: `CanWalkOnLava` falls on exactly
seven creatures and the Imp is not one of them. **Do not ask the owner about gold
on a burning bridge yet** - they offered, and it is blocked behind objects (M6)
either way, so the answer could not be used.

**What is banked and waiting for a milestone, not for research:** the trap mana
upkeep (Q59, `Traps.kwd` 0x0202, pinned by a test, needs objects at M6); the four
other spell upkeeps (Q55, parsed and read by nothing until spells at M7); payday
at 600 seconds (Q9, needs creatures that get paid at M4); creature attraction
(Q61, needs portals at M6); the floor gold lump (Q31 leftover, needs objects);
and defeat (Q40, needs portals and objectives).

**The cheapest unanswered observations**, if the owner is ever at the keyboard:
**a chain of ten claims timed end to end** would give the Q49 pause a number
(about 12 s with no pause against 17 s with one), and that is the only thing
blocking an implementation; fill-in now predicts **660 swings, about 132
seconds** for one tile (Q22) and that is untested and a long time; a **3x5
temple** is the only shape that can tell Q60's rim rule from a rule keyed on one
dimension, since all three measurements so far were squares; and a **5x5
graveyard's stated capacity** - 8 or 9 - would settle its grave rule in one look,
with a **4x4** as the fallback; **Q66**, whether a tile touching owned ground only
at a corner is claimable, which became a live question the moment movement went
eight-connected; and **Q69**, what `AttackTileHealth` is for now that Q68 has
taken away its only user, plus whether an enemy mana vault really claims five
times faster than a neutral one. **Q48 came off this list on 2026-09-03**: the
owner watched a creature cross open ground and it glides rather than
staircasing.

**Q47 came off this list on 2026-08-30 and is done.** It had said: get exact hit
counts before touching `SimulationTiming.WorkApplicationPeriodTicks`. The exact
count arrived - **273 gold a swing** at a gold seam - and it was worth the wait,
because the answer was five applications a second and not the two that the ranges
had suggested. See ADR-0020. The lesson is the same one ADR-0014 taught: the
half-delta reading was plausible, self-consistent, and wrong, and not having
implemented it made the correction free.

**Before touching worker behaviour, read `docs/research/imp-tasks.md`
§"What is not implemented, and is not guessed at".** The priority order is no
longer among the unknowns — it is CONFIRMED and built (ADR-0018) — and neither is
carrying gold (Q31, ADR-0019). What is still missing is missing on purpose:
wandering (Q41), the ~500 ms an imp lingers after claiming (Q49 - cooldown or
animation, and the two are indistinguishable from outside), dragging bodies,
fleeing, building rooms, picking up gold somebody else dropped, and the floor
lump. The crowding cap came off that list (ADR-0026) and so did the work stance
(ADR-0028).

**And before adding to `RoomRules`, read `docs/research/rooms-and-gold.md`
§"What is not implemented, and is not guessed at".** Rooms still have no
behaviour and no selling on purpose. Two of that file's open questions are now
answered and the file has not caught up with either: **a room tile must touch
land the keeper owns (Q33, not enforced — bridges can still start in open
water), and selling refunds 50% of cost per tile (Q34) — a number that is in
neither candidate variable, so the data cannot supply it.** The gold half of that
file *has* caught up: §"The round trip" is current as of ADR-0019.

**And before adding to `ManaRules`, read `docs/research/mana.md` §"What is not
implemented, and is not guessed at".** The mana ceiling is the cautionary tale
worth keeping: the data was *nearly* enough to build a computed per-keeper
ceiling out of `MaxManaGain` and `DungeonHeartMaxManaIncrease`, that reading was
tidier than the truth, and it was wrong. It stayed unimplemented only because it
required one more guess (per heart tile or per heart room?), and the owner then
settled it from the game in two sentences - the ceiling is 200000 from the first
second and never moves, which refutes the growing-ceiling variant as well.
`MaxManaGain` and `DungeonHeartMaxManaIncrease` are read by nothing, have no
candidate explanation left, and **may not be used to justify a mana rule**
(Q37a).

**And `docs/research/tile-work.md` §"What does not fit" still stands, but has
been reframed — read Q47 before Q20.** `DigRockHealth` destroying a rock tile in
one application is still a data anomaly and must not be "fixed" by changing the
number. What is new is that it is now the *only* anomaly: measured against the
original, every rate here runs about twice as fast, and an application landing
twice a second applying half the tabled delta reconciles mining exactly and
claiming approximately. That reading is self-consistent — half the delta twice a
second is the delta per second — so **the balance numbers are per-second and the
granularity is what is wrong.** Not implemented: the fit rests on ranges, and
exact hit counts are needed first. `tile-work.md` itself has not been rewritten
for this; Q47 in `open-questions.md` is the current statement.

---

## Decisions already made — do not relitigate

- **GPL-3.0-or-later** (owner decision 2026-09-04, ADR-0041; MIT until then).
  Every source file carries the three-line SPDX notice and
  `LicenseHeaderTests` fails the build if one does not. **The notice is not
  enforced by IDE0073 and that is deliberate** — see ADR-0041 for the semicolon
  defect that made an analyser silently enforce a truncated notice.
- **No code is copied from OpenKeeper — and since 2026-09-04 that is a project
  standard rather than a licence rule.** GPLv3 code may legally be combined with
  a GPLv3 work; what still forbids it is the brief's clean-room request and the
  owner's own instruction to *"reference it and compare with our own findings,
  not copy it"*. Flame is unaffected: unlicensed means no reuse rights whatever
  our licence is. Facts only, always with provenance. See the amendment on
  ADR-0003 and the rewritten boundary table in `external-resources.md`.
- GLKeeper (MIT) and YamlDotNet (MIT) are GPL-compatible and keep their own
  attribution; the GLKeeper WAD decompressor adaptation needed no action on the
  relicence (`THIRD-PARTY-NOTICES.md`).
- Integer/fixed-point for gameplay maths, `float` presentation-only (ADR-0001).
- Simulation assemblies target netstandard2.1 and must never reference Unity;
  enforced by `ArchitectureTests` (ADR-0002).
- `.slnx` solution format, .NET 10 (ADR-0005).
- Determinism gate design — RNG position in the hash, FNV-1a not `GetHashCode`,
  one pinned literal, test-only `ChaosSystem` (ADR-0006).
- Our own save format; DK2 save compatibility is not a goal (ADR-0007).
- YamlDotNet for scenarios, tooling only (ADR-0008).
- Tile transitions are triggered by the *work*, not by the health level
  (ADR-0009). This is not a style choice — the level-triggered reading is
  refuted by the data. See `tile-work.md`.
- Work orders are a FIFO queue on `WorldState`, filled by whoever is working
  (ADR-0010). Order is behaviour: never sort or deduplicate it.
- Ticks relate to seconds in `SimulationTiming` and nowhere else (ADR-0011).
- The pathfinder is an admitted placeholder behind `IPathfinder` (ADR-0012).
  Four-connected and uniform-cost by deliberate conservatism, not by belief.
- System order is declared once, in `SimulationSystems.Default()` (ADR-0013).
- **Imps come from a floor and a price** (ADR-0021). Below `MinimumImpThreshold`
  (4) a keeper gets one free every `TimeBeforeFreeImpGeneratedSeconds` (5);
  beyond it, Create Imp costs 1500 x (imps - 4 + 1), and the price FALLS BACK
  when imps die. Both numbers are read from `KeeperSpells.kwd` (ADR-0022); the
  hardcoded 1500 that ADR-0021 admitted lasted exactly one session.
- **Every imp costs its keeper 7 mana a second to keep** (ADR-0022, Q53),
  measured by the owner and read off DK2's own Create Imp record. Income and
  upkeep are capped separately and clamped to the ceiling together - an order
  fixed by a screenshot of a keeper pinned at exactly 200000 with +400 and -297.
- Imps take jobs in a strict CONFIRMED order - marked excavation, then claiming,
  then reinforcing (ADR-0018, superseding ADR-0014). Carrying gold is NOT in that
  list: it is the tail of a mining job, not a job an imp chooses.
- **Gold lives in tiles; a player has no gold counter** (ADR-0015). DK2 has no
  player-gold variable — it has four per-place limits. `GoldStore` is the only
  way gold moves.
- **Work lands five times a second, a fifth of the tabled delta at a time**
  (ADR-0020). Measured, not chosen: 273 gold a swing at a gold seam. The rate per
  second is unchanged; the swing *count* is now the measured quantity and is
  independent of Q1. `TileState.WorkRemainder` carries the fraction for the two
  deltas that do not divide by five.
- **A creature's hands are the fourth place gold can be** (ADR-0019).
  `CreatureState.GoldHeld`, bounded by the creature's own `maxGoldHeld`. A
  keeper's spendable total dips while their imps are walking home, and that is
  correct rather than an artefact. Deposits land in the tile the carrier reached,
  which is why spillover between treasury tiles needs no rule (Q35).
- Where a room may go is read from the room's own placeable-on flags, so nothing
  in the rules knows what a bridge is (ADR-0016).
- A `…PerSecond` rate from the balance table is applied **in whole, once every
  `TicksPerSecond` ticks** (ADR-0017), not smoothed across ticks. That keeps
  totals independent of the unmeasured Q1 constant, which is the point.
- **A keeper has at most one dungeon heart, ever, and losing it is defeat.**
  CONFIRMED by the owner from the original, and the ROOMS record agrees — the
  heart carries no `Buildable` flag. This is why the heart's mana is counted once
  per keeper rather than per heart tile, and it is worth knowing before designing
  anything that would otherwise need to count room instances.
- **The keeper mana ceiling is 200000 from the first second and never moves.**
  CONFIRMED by the owner from the original (Q37, Q37a). Do not reopen it with
  `DungeonHeartMaxManaIncrease` or `MaxManaGain`: they look like ceiling
  contributions, they are not, and a ceiling that never moves cannot have
  contributions at all.
- **OpenKeeper is cloned at `D:\OpenKeeper`** (owner, 2026-09-03) and is
  **GPLv3**. Use it the way the owner asked: **reference and compare, never
  copy** — field names, record layouts and offsets as facts with provenance.
  `docs/research/external-resources.md` holds the boundary and three worked
  cross-checks. It has confirmed three scan-derived offsets, both signedness
  choices and **every record type's size** — eight of them new and each to the
  byte — found a field we had no reason to look for, and turned up one place
  where its own layout is *incomplete* (Q70), which is the reason the install
  check is the method rather than a formality.
  **Where it is weakest is gameplay rules** — its creature AI has been an
  acknowledged stub for years, the same wall Q7 describes — so it corroborates
  the owner's observations rather than replacing them.
- Reference install: `E:\GogGames\Dungeon Keeper 2` (GOG). Resolved by
  `DK2.Data.Dk2Install`. **`DK2_DIR` is authoritative when set** — if it is set
  and invalid, no fallback is tried, which is how CI runs with no install.
  Tests auto-skip when it resolves to nothing.

---

## Conventions that are easy to violate by accident

- **A balance constant hardcoded in simulation code is a bug** — read it from
  `GameDatabase`. `GameDatabase[VariableTypeId.X]` throws rather than returning
  zero for a missing value, on purpose. Consequence, and it bites every time: a
  system that reads a *new* variable breaks every synthetic database until that
  variable is added to it — `TestWorld.Variables()`,
  `Dk2TerrainFixture.Variables()` and `ScenarioTests.Variables()`. That is the
  design working; add the entry rather than defaulting the lookup.
- **Changing simulation state breaks the pinned hash.** That is the design.
  Bump `GameSimulation.HashVersion` and update the literal in
  `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` in the same
  commit. Never "fix" it by loosening the assertion.
- **Nothing in the simulation may read an event back.** Events are write-only
  observation; a system that reads one makes the observer part of the observed.
- **New state must be reachable from `WorldState`** or it is invisible to the
  hash and the snapshot — and therefore silently non-deterministic. When you add
  a field, add it to `HashInto` *and* to `WorldSnapshot` (bump its
  `FormatVersion`) in the same commit.
- **A gameplay rule keyed on a terrain id is a rule about the reference
  install, not about DK2.** Ask the terrain's flags. `TileWorkRules` does this
  throughout and `TileWorkOnRealDataTests` checks it still holds against the
  real database.
- **A number that does not match how the original plays is not licence to
  change it.** Reproduce the data, record the discrepancy as an open question,
  and let the scenario output show it (Q20 is the live example).
- **A decimal printed from an `Fp32` is lossy.** Transcribe the raw value.
  `dk2tools kwd creature <name>` prints a `raw:` line for exactly this reason;
  the fixture's first copy of the Imp's hunger rate was 32767 where the raw
  value is 134213632, and only the real-data guard caught it.
- **Read a creature's capability from its flags, never its id.** `IsWorker`
  picks out the Imp and the Dwarf; `CanWalkOnWater` / `CanWalkOnLava` decide
  what a mover may cross. Same rule as terrain flags, same reason.
- Confidence tags are mandatory in docs and code comments:
  **CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN**.
- Every researched mechanic gets a `docs/research/<name>.md` using the template
  in `DK2_REIMPLEMENTATION_PLAN.md` Appendix B.
- Never commit anything from `original-reference/` or any `.kwd`/`.wad`/`.kmf`
  (gitignored, but check before `git add -A`; a third-party template slipped in
  once and had to be removed in `4cdcd38`).

---

## Environment gotchas (each of these already cost time)

**Bash heredocs and C#.** Writing C# via `cat > file <<'EOF'` has bitten twice
by eating backslashes (`Replace('\\', '/')` → `Replace('\', '/')`), and once by
choking on a file containing backticks. Use the `Write`/`Edit` tools for any C#
containing backslash escapes or backticks; verify with `grep` otherwise.

**⚠ `cat > file` with no stdin hangs** the Bash tool until timeout. So does
`python`/`python3` — it is not installed and the Windows Store stub blocks.
Never write a bare `cat >` redirect, and do not reach for python.

> **This entry has now cost time on seven separate occasions, five of them on
> 2026-09-04 alone, and it was already written down each time.** The seventh was
> the most instructive and the most stupid: `python - <<'EOF' … EOF || echo "use
> sed"` — a *guard* against the failure, written as a pipe into python, which
> hung for the full two-minute timeout and took a batch of four `sed` edits down
> with it. **Wrapping the mistake in a fallback does not make it safe; the hang
> happens before the fallback can run.** Reading a
> gotchas list is not the same as remembering it mid-task, and the failure mode
> is always the same: reaching for `python -` reflexively while thinking about
> something else. **The habit that actually works is not "remember the list" but
> "never pipe into an interpreter" — use `perl -0pi -e`, `awk`, or the
> `Write`/`Edit` tools, all of which are already sufficient for everything this
> project has needed.** If a future session finds itself typing `python`, that is
> the signal.
>
> Note: python *is* on PATH in this environment (`/d/Python/python`, 3.13), so
> `python -c "..."` works. That does not make `python -` or a piped heredoc safe,
> and the sixth occurrence was exactly that. The rule is about the pipe, not the
> interpreter's absence.

**⚠ Large heredocs into the Bash tool are unreliable.** Appending a long
multi-line block to a document with `cat >> file <<'EOF' … EOF` failed to parse
on 2026-09-04 (`unexpected EOF while looking for matching '`) even though the
delimiter was quoted and the content contained nothing exotic. **The reliable
pattern for appending a long block is: `Write` it to the scratchpad, then
`cat scratchpad/file >> target`.** Short heredocs are fine; anything with tables,
apostrophes and em dashes should go through `Write`.

**`ctx_batch_execute` sandbox quirks:**
- `for ... do ... done` loops fail to parse (the wrapper prefixes the command
  on one line). Use separate commands or `;`-chained statements.
- `/tmp` does not persist between commands in a batch — each gets a fresh
  workspace. Pipe directly, or `curl -o` and use the file in the *same* command.
- `python3` is not available; `curl`, `grep`, `sed`, `awk`, `perl`, `xxd` are.

**`dotnet build` takes one project, not several.** Pass `DK2.slnx` instead.

**Windows/git:** CRLF warnings on every commit are expected noise.

**Unity and winget (all found 2026-09-04):**

- **`winget search` without `--source winget` prompts** for the msstore
  agreement and then fails with `Error reading input in prompt`, because the
  tool has no interactive stdin. Always pass `--source winget`.
- **Unity Hub installs as an MSIX package**, so it is *not* in `Program Files`
  and `Get-AppxPackage *Unity*` is how to find it. The **editor** is a normal
  install at `C:\Program Files\Unity 6000.6.0f1`.
- **`pwsh` is not installed.** Use the `PowerShell` tool, or
  `powershell.exe -NoProfile -File ...` from Bash.
- **Never reference `UnityEngine.dll` alongside the module assemblies.** The
  top-level facade re-exports the same types and every Unity type becomes
  ambiguous (CS0433 on `GameObject`, `Transform`, `Color`, …). Unity's own asmdef
  compilation references only the modules; `DK2.Unity.Compile` mirrors that.
- **Unity exit code 198 with `Found 0 entitlement groups`** means the editor is
  not activated — sign in via Unity Hub, then *add a free personal licence*,
  which is a separate step from signing in.
- **A `.sln` in the Unity project root is Unity's, not ours.** It regenerates it
  for Visual Studio/Rider on every open. Unity opens the *folder*; the `.sln` and
  `.csproj` there are gitignored. This confused the owner once and the confusion
  was entirely reasonable.
- **Unity's first launch on a project takes minutes** and the window title says
  `Opening project...`. Not a hang.

---

## Research method that has been working

Cheap-to-expensive, stop as soon as the question is answered:

0. **Read `docs/research/external-resources.md` first if the question might be
   answered outside this repo.** It records the seven resources OpenKeeper
   points at, which of them still resolve, what each is good for and **when** -
   and, once, the licence boundary. **Both projects are now GPLv3, so "facts with
   provenance only, never code" is a project standard rather than a legal
   limit** - and it stands, because the brief asks for clean-room work and the
   owner asked to reference and compare rather than copy. It also holds five
   worked examples of these sources paying off, including an independent reversal
   confirming the MAP flags byte without knowing what it meant, and the Q74 case
   where their source supplied the mechanism and the install supplied the answer.

0b. **Ask the owner, or hand them `docs/research/observation-checklist.md`.**
   They have played DK2 and the original is the behavioural oracle. That file
   lists every open question a person in front of the game can settle by
   looking, as concrete experiments, and says which ones playing cannot settle.

   This is the cheapest source there is and it was under-used: Q37 (the mana
   ceiling) and the one-dungeon-heart rule were both settled in one sentence
   after being written up as unanswerable from the files. A question of the form
   "what does the player *see* happen" is almost always one of these. Record the
   answer as CONFIRMED with "owner observation" as the evidence, the same way a
   data extract is recorded — and record a "no" too, since it closes a question
   just as hard.
1. Is the answer just sitting in the KWD data? (Often yes — check first.)
2. ufdada/dk2-tools 010 Editor templates — independent format facts.
3. Flame's `mapping/DKII_EXE_v170.sgmap` — 868 structs, 10,548 globals.
4. OpenKeeper — consult as documentation only (**GPLv3**; see
   `external-resources.md` for what that permits and what it forbids).
5. Only then: IDA/Ghidra on DKII.EXE, using Flame's IDA import scripts.

**Validate offsets structurally, never on trust.** The techniques that have
caught real errors so far:
- Record size must equal `contentSize / itemCount` exactly (caught the 40-byte
  WAD entry stride, which the template got wrong at 36).
- Sum the template's field widths and check it reproduces the record size
  (confirmed terrain 552 and rooms 1055 before writing any parser). **This is
  now the workhorse**: on 2026-09-04 it produced eight new record sizes, every
  one landing exactly on the measured stride — and a sum that lands exactly
  cannot have a wrong field cancelled out by another, so it validates the
  *layout*, not just the total. Three ways it goes wrong, all caught by the
  stride and none by inspection: a **partial sum** stopped at the interesting
  field looks like a record size (TRAPS 516 vs the real 579); a **tagged union**
  sums to all its branches at once (VARIABLES 100 vs the real 16); and a
  **peek behind a mark/reset** double-counts (`readArtResource` 174 vs 84).
- **A whole stride is not proof of a fixed-stride record type.**
  `GlobalVariables.kwd`'s THINGS chunk divides perfectly at 92 bytes and is
  really 39 self-describing records that happen to be the same size.
- Look for semantic invariants: happy-job chances summing to 100 across all 46
  creatures proved the JobPreference offsets were right.
- Cross-check between independent parsers: room→terrain→creature references all
  resolving is strong evidence all three are correct.
- Sanity-check values against known DK2 behaviour (Vampire 666 damage, Prison
  spawning Skeletons, Hero Lair healing).

---

## Live research questions

`docs/research/open-questions.md` holds all 76 (Q1-Q72; Q51 superseded). The ones that gate progress:

- **Q53 is ANSWERED and built** (2026-08-30): 7 mana a second per imp, measured
  by the owner and read off DK2's own Create Imp spell record (ADR-0022). Left in
  this list for the method, which is in `imps-and-mana.md` §*How this was
  established*: a scan anchored on a measured number produces a trustworthy
  negative, and a scan anchored partly on a forum post does not.
- **Q59 is ANSWERED and now cross-checked** (2026-08-30, 2026-09-03): a trap is a standing mana charge, and the
  field is `Traps.kwd` offset 0x0202, confirmed against five owner measurements
  across three distinct values and pinned by `TrapsOnRealDataTests`. Not
  implemented, because a trap is an object and objects arrive at M6.
- **Q47 is ANSWERED and built** (2026-08-30): 273 gold a swing put an application
  at a fifth of the tabled delta, five a second (ADR-0020). Left in this list
  only to say that `DigRockHealth` is now the sole thing in the tile mechanics
  that does not fit, where it used to be one suspect among several.
- **Q40 — losing your dungeon heart is defeat.** Now fully described (the
  defeated keeper's creatures leave by the nearest portal; rivals claim their
  rooms and gold) and **not implementable yet**: it needs portals, which are an
  M6 record. Scheduled with objectives and win conditions.
- **Q7 — the creature mood model.** No longer a blank sheet. The four things that
  anger a creature are recorded and three of them map onto fields already parsed
  per creature (`AngerNoLair`, `AngerNoFood`, `AngerNoPay`). What is missing is
  the shape — accumulation, decay, thresholds. This was the area nobody had
  solved; it is now the area with a starting point.
- **Q1 — simulation tick rate.** Blocking any timing claim. Flame's `limit_tps`
- **Q1 — simulation tick rate.** Blocking any timing claim. Flame's `limit_tps`
  patch proves it is fixed; the value is unmeasured. It is one constant,
  `SimulationTiming.TicksPerSecond`, and it must stay that way. **Do not go
  looking in the KWD data again**: id 74 `GameTicks` exists in the format's id
  space but no shipped file carries it — checked across every editor KWD and all
  71 map KWDs. Q1 needs measurement against the running original.
- **Q19 — what the health deltas are *per*.** Tied to Q1. The variables table
  spells out `…PerSecond` / `…Seconds` on its other entries and not on these
  ten, which is the whole of the ambiguity.
- **Q20 — `DigRockHealth` is −5000 against Rock's 165 health.** One application
  destroys it, so digging earth is instantaneous, which is wrong. Every other
  work rate is sane. New at M3 and the first place the data and the original
  visibly disagree. Candidate explanations in `tile-work.md`.
- **Q18 is now answered** (2026-08-29): `maxHealthTypeTerrainId` is the
  destination of whichever upward job the terrain admits. Do not reopen it
  without new evidence.
- **Q5 — pathfinding.** The project's biggest risk (plan §9, R1). Flame names
  `Follow_Path` but has zero named fields for it. Nobody has solved this. Now
  load-bearing rather than theoretical: `GridPathfinder` routes every worker,
  and Q28 (four- or eight-connected?) and Q30 (does terrain weight a path?) are
  the two cheapest pieces of it to settle against the original.
- **Q2 — job selection.** Narrowed: candidates and weights are *data*. The
  remaining unknown is the procedure around them (re-evaluation timing, how
  mood picks which of the three tables applies, handling unavailable jobs).
- **Q7 — creature moods.** Genuinely unsolved; OpenKeeper's system is an empty
  stub after 12 years.
- **New at M2 — system update order.** DK2's own per-tick subsystem ordering is
  UNKNOWN. Ours is explicit and declared in exactly one place
  (`SimulationSystems.Default()`, ADR-0013). It starts to matter around M4;
  treat it as a research question then, not a convenience choice.

---

## Corrections carried forward

At the end of the previous session I told the owner that `AGENTS.md` references
a `docs/ai/CLAUDE.md` scoping the assistant to research and analysis, and
suggested it might need updating now that implementation has started. **That was
wrong.** No `CLAUDE.md` exists anywhere in the repository and `AGENTS.md` makes
no such reference. There is nothing to reconcile; do not go looking for it.

`AGENTS.md` itself is the operating instruction, and it does not restrict
implementation.

---

## Where the project stood at the 2026-09-04 sitting

Six sittings of owner measurements and ten ADRs - one of which needed no
measurement at all, and one of which needed no new data either, only a look at
the dump we already had. The state:

**486 tests green** with and without a DK2 install (Core 14, Simulation 254,
Data 55, Behavior 77, Assets 60, Presentation 26), ten scenarios running, clean
tree.

**Every known defect is fixed, and the room economy now runs both ways.** In the
order they fell:

| | |
|---|---|
| ADR-0019 | Q31 — gold moves seam → imp's hands → treasury |
| ADR-0020 | Q47 — work lands five times a second, a fifth of the tabled delta |
| ADR-0021 | Imps come from a floor and an escalating price |
| ADR-0022 | Q53 — every imp costs 7 mana a second, off the *spell* record |
| ADR-0023 | Q20 — `DigRockHealth` corrected to −500, the only such override |
| ADR-0024 | Q62 — an upward job costs the terrain's whole `MaxHealth` |
| ADR-0025 | Q28/Q48 — eight-connected movement, a diagonal costs √2 |
| ADR-0026 | Q43 — three workers to a face, the fourth goes elsewhere |
| ADR-0027 | Q52/Q45/Q60 — a room is an instance, derived rather than stored |
| ADR-0028 | Q44/Q42/Q49 — floor is worked from on top of it, walls from beside |
| ADR-0029 | Q34 — selling returns half the cost, and the data said so all along |
| ADR-0030 | Q43 — the crowding cap follows the stance: three to a wall, one to a floor tile |
| ADR-0031 | Q64/Q16 — a bridge reverts to the hazard it spanned, and the MAP record says which |
| ADR-0032 | Q67 — a wooden bridge over lava burns, and the clock starts at build |
| ADR-0033 | Q68 — a rival claims your ground directly, and hostile expansion falls out |
| ADR-0034 | An asset is named by a logical key, and a provider chain resolves it |
| ADR-0035 | Rendering goes through one view, and the harness draws it as text |
| ADR-0036 | Unity 6 LTS, and the adapter is six methods |

**Nothing is left on that list**, which is why the pick-up list above now starts
with the Unity view.

**Two mechanisms were created for numbers that come from observation rather than
from the files, and both are meant to stay narrow:**

- `ObservedCorrections` — the file carries a value that *cannot produce* the
  observed behaviour. One member: `DigRockHealth`. Four rules for adding a
  second are written on the type.
- `ImpCrowding` — the file carries *no* value at all. One member: three workers
  to a face. **Q34 was expected to join it and does not** (ADR-0029): the shipped
  `RoomSellValuePercentageOfCost` really is 50 and this file had it wrong, so
  selling needed no override at all. The live candidate is now the **~500 ms
  pause after claiming** (Q49) - real, observed, and in no field we read, because
  every `...Seconds` variable in the balance table is a whole number and none is
  below 1. It needs a measurement first; see `observation-checklist.md` for the
  chain-of-ten method.

**The research method that produced all of this is written up in three places
and is worth reading before the next research push:** the call-out at the top of
this file (eight lessons), `docs/research/imps-and-mana.md` §*How this was
established* (the failure and recovery in full), and
`docs/research/observation-checklist.md` (what is still worth asking, and what
playing cannot settle).

**And the seventh sitting (2026-09-04) added no ADR and no mechanic**, which is
the point of it: it was the record-layout sweep, and it produced **structure**.
Twelve record sizes pinned against the install, eight of them new; THINGS and
TRIGGERS revealed to carry their own record sizes and split accordingly; one
disagreement recorded as Q70. No gameplay rule came out of it and none was
expected to — **static sources for structure, the owner and the original for
behaviour** is now a division of labour with three worked examples behind it.
`docs/research/kwd-container.md` §*Update 2026-09-04* is the result;
`external-resources.md` §*Worked example 3* is the method.

**And the eighth sitting (2026-09-04, afternoon) built the asset layer and the
presentation boundary** (ADR-0034, ADR-0035) — `GOAL_ALIGNMENT.md` gap 3 closed
on the correct side of its deadline, which was the whole point of writing the
deadline down. Two new assemblies, 75 new tests, and the game now renders. **What
it did not build is the Unity project**, because Unity is not installed here and
this project does not commit code it has not compiled; the remaining adapter is
described in pick-up item 2.

**Three mistakes, and the shape of them is worth more than the fixes.** All three
were caught by real data rather than by reasoning: a glyph scheme that read rock
as imps until a campaign level was actually drawn; a test that asked a hash for
distinctness it cannot give; and a character set for record names that the
shipped terrain table refuted on first contact. **The pattern across the whole
day — record layouts in the morning, rendering in the afternoon — is the same
one: every wrong belief was found by running it against the install, and none by
inspection.**

**One process lesson, from the owner rather than from the code.** The Simon
Schreibt wall-rendering article had sat in `external-resources.md` for a day in a
row that said, in its own words, *"useful for the Unity view"*. The Unity view
became the task and nobody re-read the row; the owner asked directly whether it
had been read. It had not, and it was worth an afternoon — it corroborated one
design decision and refuted another. **A resource table's "when" column is a
commitment, and the table cannot tell you when its moment arrives.** The editor
manual's row says M6 and is set up for exactly the same failure.

**And the ninth sitting (2026-09-04, evening) wrote the Unity adapter and found
what four months of not using Unity had hidden.** The adapter itself was small,
as designed. What mattered was the first honest attempt to compile against
Unity's own assemblies, which produced `NU1201: not compatible with
netstandard2.1` from the seven projects that were supposed to *be*
netstandard2.1 — a claim ADR-0002 had been making since M1 and which had never
been true (see the correction on that ADR).

**The general lesson, and it is the one to carry:** every previous defect in this
project was found by running rules against the real install. This one was found
by *using the thing for its purpose*. A constraint that nothing exercises is not
enforced no matter what the build files say, and "enforced at compile time" in an
ADR title is itself a claim that needs a test. There is now one, and it reads the
built assembly rather than the configuration that was already wrong.

**What is genuinely unverified and should not be described otherwise:** Unity is
installed but not activated, so the adapter is *compiled* and has never *run*.
`TextRenderer` remains the only renderer anyone has watched work.

---

## The ninth and tenth sittings (2026-09-04) — the view, in one day

Two sittings ran back to back and they belong together, because the second only
happened because the first made it cheap.

**The ninth built everything that is not Unity.** `DK2.Assets` (ADR-0034) and
`DK2.Presentation` (ADR-0035) — the logical asset key, the provider chain ending
in a total placeholder, the catalogue that makes "runs with no art" a test rather
than an intention, the shared `WorldView`, `ScenePresenter`'s diffing, and a text
renderer that runs in CI and draws every scenario's finished world. That closed
`GOAL_ALIGNMENT.md` gap 3 **on the correct side of its deadline**, which was the
entire point of having written the deadline down months earlier.

**The tenth wrote the Unity adapter**, and it was small, because the ninth had
already taken every decision out of it. Unity 6 LTS renders campaign level 1.

### What that day actually taught, in order of how much it cost

1. **A constraint expressed only in build configuration is a hope.** ADR-0002
   claimed netstandard2.1 was "enforced by the compiler from day one" and it was
   never true for a single day: the condition selecting it could not fire, and
   every assembly built as net10.0 from M1 to 2026-09-04. **Unity cannot load a
   net10.0 assembly**, so the presentation plan would have failed at the first
   attempt to use it. Now asserted from `TargetFrameworkAttribute` on the built
   assembly. **The general form: "enforced at compile time" in an ADR title is
   itself a claim that needs a test.**

2. **A constraint nothing exercises is not enforced, whatever it says.** This is
   the same lesson from the other end, and it is the more useful half. Every
   earlier defect in this project was found by running rules against the real
   install. This one was found by **using the thing for its purpose** — no
   amount of re-reading the build files would have surfaced it, because they
   looked exactly right.

3. **Rendering the real thing finds what unit tests cannot.** Three times in one
   day: `TextRenderer`'s glyph alphabet contained `@` and `$`, so on campaign
   level 1 rock read as imps and the dungeon heart was indistinguishable from
   rock beside it — visible in one look, invisible to every test. Then the camera
   framed the level by `world.Width` on a level 30 wide and 38 deep, so the near
   end fell off the screen — found by the owner's first screenshot. **A smoke
   test proves 1140 objects exist; it cannot prove a camera points at them, and
   that is the boundary of a test suite rather than a hole in it.**

4. **A hash cannot promise distinctness.** The test replacing the glyph bug first
   asserted that eight solid terrains would spread across an eight-symbol
   alphabet; they gave five, which is exactly what random bucketing predicts.
   Widening the alphabet lowers the collision rate and cannot abolish it. The
   test now asserts what a reader actually needs — Rock, Gold, Gems and
   Impenetrable Rock distinguishable — and says out loud that it is not a
   uniqueness claim.

5. **A guessed character set met the real data and lost.** `AssetRef` allowed
   space, dash and apostrophe; the shipped terrain table carries
   `Hero Gate [ Final ]`. Measured over all 113 names now.

6. **A resource table's "when" column is a commitment.** The Simon Schreibt
   wall-rendering article sat unread in `external-resources.md` in a row whose
   own text said *"useful for the Unity view"* — while the Unity view was being
   built. The owner asked about it directly and it was worth an afternoon: it
   **corroborated** `TileView.HealthPermille` (DK2 shows wall damage by swapping
   textures) and **refuted** one-mesh-per-terrain (walls are drawn from a set of
   pieces chosen by the room behind them). **The editor manual's row says M6 and
   is set up for exactly the same miss.**

### One quiet piece of good news

Moving seven assemblies from net10.0 to netstandard2.1 — a change of framework,
runtime and, in two places, the actual API called — **did not move the pinned
determinism hash**. That is the best evidence yet that ADR-0001's
integer/fixed-point rule does what it was chosen for. The change being small was
luck; the change being *safe* was design.

### The state, precisely

**486 tests green** with and without a DK2 install (Core 14, Simulation 254,
Data 55, Behavior 77, Assets 60, Presentation 26). Ten scenarios run, each
writing a picture of its finished world. Unity compiles and smoke-tests clean,
and renders. Clean tree apart from the owner's own `AGENTS.md` edit, which is
**deliberately excluded from every commit** — use
`git add -A -- . ':!AGENTS.md'`.

### Next: what rooms actually do

Agreed with the owner. See item 3 under *Immediate next steps*, which names the
suggested order and the two traps — that room capacity is **not** reliably "tile
count" (Q60 proved a temple's is its rim), and that several rooms will be blocked
because creatures still have no health, hunger, mood or experience.

---

## The eleventh sitting (2026-09-04, after compaction) — the manual, and three rules

Two inputs from the owner, arriving together: the shipped **1999 game manual**,
and three behavioural rules stated *"just to make explicit"*. Between them they
were the most productive hour of research the project has had, and one of them
found a four-month-old hole.

### What the manual is, and the rank it was given

ADR-0037. It is EA's own document about EA's own game, which makes it the most
plausible external source encountered so far — and it shipped with **1.0** while
this project targets **v1.70**. So it is admitted **below the shipped data and
below the owner's observation, above every community source**: agreement raises
confidence, disagreement is recorded as a question and the data is implemented.

**It was tested before it was used**, on ground where we already had answers. Its
room pages print a Cost and a Minimum Recommended Size for all seventeen rooms,
both parsed from `Rooms.kwd` by offsets found without it. **17 of 17 costs
matched; 16 of 17 sizes matched**, and the exception (Workshop) contradicts the
manual's own creature page, so the record is right. That single check bought an
independent cross-validation of our room offsets *and* a third source for Q45 —
the manual states outright that the size gates *function* and not *building*,
which is `RoomInstance.IsFunctional` and Q36 together.

**What it is good at is the thing this project was short of: what a mechanic is
for.** The balance table says `ModifyAngerOfCreatureInLairPerSecond = −550` and
never says what a lair does; the manual says creatures recover health and
happiness in lairs and never says how fast. Read together they are a rule. That
pairing is now written up room by room in `docs/research/manual-1999.md`, and it
is why item 3 in the pick-up list is no longer a blank sheet.

**What it settled that nothing else had:** research is stored **in the library**
and is lost with it; `ModifyPlayerColdWhileTrainingPerSecond` is **Gold**, not
"Cold" (the manual says training costs the keeper per turn, so a variable that was
unusable is now −5 gold/s per trainee); the casino's payout lever is exactly the
`Smiles`/`Money` variable pairs; the creature cap is **per portal**, 15 then 5; a
gold treat is a **prepayment** of wages; a lair holds **beds**, one per creature,
not tiles.

**What it corroborated, independently:** the gold overflow chain treasury → heart
→ floor, word for word (Q31, ADR-0018/0019); the 50% sell rate (Q34, now on three
agreeing sources); `TrainingRoomMaxExperienceLevel` = 4 and
`CombatPitMaxExperienceLevel` = 8, both stated as prose in the manual and as
numbers in the table; `RoomDef.CreatedCreatureId` (Graveyard → Vampire, Prison →
Skeleton); `MinimumImpThreshold` = 4 — *"The Dungeon Heart always provides enough
Mana to sustain your four Imps"*, which is ADR-0021's semantics as well as its
number.

**Where it lost:** five disagreements, all in creature attraction, all in the same
direction — the manual is *more* demanding than the shipped record. In one of them
it describes a **third** attraction slot the record has no bytes for. A source
that describes a record layout the file does not have is describing a different
build (Q80).

### The three rules, and the one that mattered

1. **Creatures cannot pass through walls** (ADR-0038). Already implemented — and
   **implemented in one place and asserted in none**. `WorldPathGrid` had refused
   solid terrain since the first worker, so no route was ever *planned* through
   rock. But `CreatureMovementSystem` walked a stored route without re-checking
   it, and there is a shipped mechanic that invalidates a route mid-walk: a
   **wooden bridge over lava burns out thirty seconds after it is built** (Q67).
   Four lines fixed it; two 4000-tick invariants now pin it.

2. **A dug-out tile can never be closed again** (ADR-0039). This one argues with
   the data, which is why it is interesting: `FillInHealth` = 5, a
   `TerrainFlags.FillInable` bit on exactly the five floor terrains, and Claimed
   Path's route back to Rock are three *coordinated* pieces of shipped data, and
   three coordinated fields are not an accident. The owner has played the game and
   says no keeper can trigger it. Both accepted: the engine carries the concept,
   the shipped game exposes no way to reach it (Q74). **Nothing was deleted** —
   the transition is in the file — but the behavioural claim is now asserted.

   **Then the owner asked for this to be dug into properly, and two of the three
   candidate explanations closed** — write-up in `manual-1999.md`, method in
   `external-resources.md` §*Worked example 5*, which is the best illustration yet
   of how OpenKeeper should be used:

   - **OpenKeeper is in exactly our position.** `FILL_INABLE(0x00008000)` and
     `FILL_IN_HEALTH(11), // value=5` are each declared **once and referenced
     nowhere else** in its source, and its worker task list has no fill-in task.
     A negative result worth having: it rules out "somebody already solved this".
   - **It pointed at a mechanism we had not considered** — trigger action
     `ALTER_TERRAIN_TYPE(22)`, a direct terrain write with no health and no rate —
     and supplied a byte layout for the record.
   - **The install settled it, not them.** New command
     `dk2tools kwd alterterrain [-v]` decodes every such action in all 66 shipped
     trigger files. The layout **self-validated** (5408 action records, every
     decoded type inside the enumerated 1..60 range), and the answer was the
     reverse of what was being looked for: **94 of the 160 uses are
     `Impenetrable Rock → Dirt Path`**, scripts *opening* sealed passages.
     **Level 1's three Rock writes are opening too** — (15,18)/(16,18)/(17,18) all
     start as Impenetrable Rock and are softened into diggable Rock, the tutorial
     handing the player a route. **Exactly two writes in the whole campaign turn
     an open tile solid**: `Secret3` (13,20) and (5,25), Claimed Path →
     **Reinforced Wall**.

   So a level script can wall a tile up as a set-piece, twice in sixty-six maps,
   and **nothing anywhere uses the fill-in *health* mechanic**. Q74 keeps only
   that half open; the level editor is the last place to look, and there is now a
   note in the editor-manual section saying what to look for.

   **M6 consequence, recorded so it is not rediscovered:**
   `ALTER_TERRAIN_TYPE` must be able to write solid terrain onto an open tile.
   `DugOutTilesStayDugOutTests` is a statement about **workers**, not about the
   engine, and its doc comment now says so. Level 1's three writes are a
   ready-made first test for the action on a level that already renders.

3. **Two terrains can never be dug through**, for two different reasons:
   Impenetrable Rock is **not taggable** so a keeper cannot mark it, and Gems
   destroy to **themselves** so every swing yields gold and the tile survives.
   The data already said this and the model was already right; it now has tests,
   on the fixture and on the shipped table.

### What that cost, and the lesson worth keeping

**A rule that only one line of code knows is a rule that can be deleted by
accident — and worse, a rule nothing checks may already be broken.**

`PathfindingTests` proved the *search* would not cross a wall on a hand-drawn
grid. That is a property of the search. Nothing proved that the *simulation*,
running the real systems on real terrain over many ticks, never put a creature
inside one — and it turned out it could.

The sharpest part: adding the invariant **broke two existing tests**.
`WalkingOnTheBias_TakesRootTwoTimesAsLong` and
`SpeedDoesNotDriftFromRoundingItAwayEveryTick` hand-fed routes that ran straight
across the fixture's rock. They were measuring movement arithmetic, the arithmetic
was right, so they passed — while walking imps through walls for four months.
**Nobody noticed because nothing looked.** That is the same shape as the
netstandard2.1 defect (ADR-0002's correction) and the same shape as gap 6 in
`GOAL_ALIGNMENT.md`: the question to keep asking is not *is this claim true* but
*what would fail if it stopped being true*.

Second, smaller lesson: **the owner volunteering an "obvious" observation is worth
more than answering a question.** Neither of these was on the checklist. Nobody
would have thought to ask "can creatures walk through rock?" — and the answer
found a real hole. The `ask-the-owner-about-dk2-behaviour` habit should extend to
welcoming statements nobody asked for.

Third: **piping into `python` cost time again** — sixth occurrence, and it was
already written down five times. See *Environment gotchas*. The habit that works
is not remembering the list, it is never piping into an interpreter at all: use
`perl -0pi -e`, `awk`, or Write/Edit. This sitting also hit a **heredoc** failing
inside the Bash tool for a large multi-line append; Write-to-scratchpad then
`cat >>` is the reliable pattern for appending a long block to a doc.

### The state, precisely

**495 tests green** (Core 14, Simulation 262, Data 55, Behavior 78, Assets 60,
Presentation 26), with and without a DK2 install. Ten scenarios run. The pinned
determinism hash **did not move** — the movement guard changes nothing that any
scenario reaches, which is itself evidence that no shipped path was crossing a
wall.

New in the repository: `docs/research/manual-1999.md` (the extract, page
referenced), `tests/DK2.Simulation.Tests/WallsBlockMovementTests.cs`,
`tests/DK2.Simulation.Tests/DugOutTilesStayDugOutTests.cs`, ADR-0037 / 0038 /
0039, Q73-Q81, one more real-data test in `TileWorkOnRealDataTests`, and a new
tool command `dk2tools kwd alterterrain [-v]`.

The PDF itself lives in `original-reference/` — the owner moved it there when the
copyright question was raised, where the existing "NEVER COMMIT: original Dungeon
Keeper 2 material" rule already ignores it.

### Next

**Still item 3: what rooms actually do** — and now much better resourced. Read
`docs/research/manual-1999.md` §*What each room does* first, then ask the owner
about *shape* (capacity, eligibility, what happens to the state a room changes)
rather than about purpose. Suggested order: lair, library, training room; the
hatchery is blocked on objects (M6).

---

## The twelfth sitting (2026-09-04) — per-level variables, and the portal is next

Asked "where do we go from here", the answer was worked out by **checking what
the rooms work would actually need** rather than by planning on paper. That check
found a defect and reordered the plan.

### What the check found

**Rooms are researched and mostly blocked, and not on research.** `ImpTaskSystem`
skips anything without `IsWorker`, THINGS is unparsed so maps place nobody, and
portals do not exist — so **there are no non-imp creatures in the simulation, and
one dropped in would stand still forever.** A lair with nothing to sleep in it is
decoration, not fidelity work. The blocker map is in *Immediate next steps* item 3.

**And the loader was reading the wrong balance table.** `GameDatabaseLoader.Load`
read `GlobalVariables.kwd` and nothing else; every level also ships
`<name>Variables.kld`, and **26 variable ids are overridden by at least one
shipped level**. One was already live — `MinimumImpThreshold` is **0** on four of
the five secret levels and on the front end, against a global 4, so a keeper there
gets no free imps at all where `ImpSupplyRules` would hand out four. Level 1
doubles both payday variables.

Fixed: ADR-0040, `GlobalVariables.MergedWith` / `GameDatabase.ForLevel` /
`GameDatabaseLoader.LoadForCampaignLevel`, with the merge rules pinned without an
install and the shipped numbers pinned against one. **No scenario hash moved**,
which is the expected result and the point: level 1 overrides nothing any built
system reads, so the fix lands *before* the mechanics that need it.

### The by-product, which was larger than the fix

**`Availability` decoded, from the data first.** The record
(`u16 kind, u16 playerId, s32 typeId, s32 value`) was parsed, unread and
unexplained. Its `kind` enum falls out of how many type ids appear under each
(eleven traps, six doors, thirteen spells, up to seventeen rooms, and kind 2's
ids match that level's creature pool exactly); its `value` enum falls out of how
the states differ between the global template and levels 1, 5, 13 and 20.
OpenKeeper names both fields the same way — `ROOM/CREATURE/TRAP/DOOR/SPELL` and
`UNAVAILABLE/RESEARCHABLE/AVAILABLE/UNAVAILABLE_ROOM` — and that agreement was
reached **after** the derivation, which is why it counts as corroboration.

It explains the shape of a DK2 campaign: **the global list is a player-0
template** (every room available, everything else researchable), and **a level's
list is that level's own**. Level 1 starts its keeper with Treasury and Library
UNAVAILABLE, the Portal UNAVAILABLE_ROOM, every trap/door/spell UNAVAILABLE, and
exactly one creature — the **Goblin** — AVAILABLE, matching its one-entry pool of
fifteen Goblins. The tutorial unlocks the rest by trigger. `Secret1` carries no
availability and no pool at all.

**And it named a gap nobody had written down:** which rooms a keeper may build is
per level, and `RoomRules` does not model it — a keeper here can build any
buildable room on any level.

### Q1 has a datum for the first time

The id-74 `GameTicks` variable was recorded on 2026-08-29 as carried by *"no
shipped file — not GlobalVariables.kwd, not any of the 71 map KWDs"*. It is
carried by **all 66 per-level `*Variables.kld` files, at 340 in every one**. The
sweep behind the negative result looked at the `*.kwd` map files.

**What 340 means is UNKNOWN and is not being guessed** (Q83). 340 a second is
implausible; the id shares its "no trailing `unk1`/`unk2`" shape with
`LevelRating` and `AverageTime`, which are front-end metadata; and OpenKeeper's
comment on the same id records `value=4`, matching nothing in this install, so
even the community *name* is suspect. What changed is that Q1 has an invariant
number across the whole campaign instead of a blank. Note 340/60 = 5.67 against
Q47's measured **five** work applications a second — suggestive, and deliberately
not adopted.

### The licence headers

The `.editorconfig` added this session declares `file_header_template` and sets
IDE0073 to warning, which `TreatWarningsAsErrors` turns into an error in every
project — so a clean `dotnet build` was failing on **every** source file. Its own
comment says to add such a rule "only in a commit that also fixes every file it
flags", so the stamping was owed. **133 files stamped** from the one file that
already carried the header, in a commit of its own (`bd68066`) so the diff stays
readable.

`LicenseHeaderTests` is worth knowing about: it tied the three copies of the
notice together — `LICENSE`, `license.txt` (an IDE header template) and
`.editorconfig` (the copy Roslyn enforced). Before it, those three could have
said different things and every build and test would still have passed.

> **All of the above was MIT, and lasted about an hour.** The project moved to
> **GPL-3.0-or-later** the same evening (ADR-0041), the notice shrank to three
> SPDX lines, and IDE0073 was removed from `.editorconfig` altogether after it
> silently enforced a *truncated* GPL notice — an `.editorconfig` value ends at
> the first `;` and the GPL's prose has one in "WITHOUT ANY WARRANTY; without
> even the implied warranty". Enforcement is now `LicenseHeaderTests` alone,
> which also covers `unity/DK2.Unity/Assets` that no analyser in `DK2.slnx` can
> see. See the thirteenth sitting below.

### The lesson, and it is the same one for the third time in a week

**`kwd-variables.md` has said "per-level `*Variables.kld` files override it" since
2026-08-29.** The fact was researched, written down, and read by no code for six
days.

That is the same shape as the netstandard2.1 target (declared in a props file
under a condition that never fired) and the walls rule (implemented in one line,
asserted nowhere). Three different failures, one pattern: **something is stated in
a place that does not run.** The standing question at every review is in
`GOAL_ALIGNMENT.md`, and it now has a second half: not only *what would fail if
this claim stopped being true*, but **does anything read this fact at all?**

### The state, precisely

**511 tests green** (Core 16, Simulation 262, Data 63, Behavior 84, Assets 60,
Presentation 26), with and without a DK2 install. Clean `dotnet build` with no
suppressions. Ten scenarios, all hashes unchanged. Unity adapter compiles.

New: `GlobalVariables.MergedWith`, `GameDatabase.ForLevel`,
`GameDatabaseLoader.LoadCampaignVariables` / `LoadForCampaignLevel`,
`ScenarioResult.BalanceTable`, `LevelVariablesOnRealDataTests`,
`GlobalVariablesMergeTests`, ADR-0040, Q82–Q85, Q1 corrected, and
`dk2tools kwd vars -a` for the pool and availability lists.

### Next: the portal

Agreed with the owner. It is the one room whose behaviour needs no creature needs,
whose data is now entirely parsed and loaded, and which is the prerequisite for
every other room having occupants:

| what it needs | where it is |
|---|---|
| who may enter | `CreaturePool`, per level — level 1 is fifteen Goblins |
| whether they are unlocked | `Availability` kind 2 (CREATURE), state AVAILABLE |
| what attracts them | `CreatureAttraction` at CREATURES 0x0DAC, two slots (Q61, Q80) |
| how often one arrives | `EntranceGenerationSpeedSeconds` — 25 globally, 18 on level 1 |
| how many a keeper may hold | `CreaturesSupportedByFirstPortal` 15, `...PerAdditionalPortal` 5 |
| sacking one | drop it back into the portal; `Decrease/IncreaseEntrancePersentage...` = 33 each |

**What is not settled and should be asked or observed before it is built:** what a
portal does when the pool is empty, whether the cap is per keeper or per portal in
practice (the variable names say per portal), and whether attraction is checked
once per spawn or continuously. And note Q61/Q80's live disagreement — the owner
and the 1999 manual say a Dark Angel needs a 5x5 temple, the v1.70 record says
size 0.

After the portal: the **library** (needs no creature needs at all — a creature
stands in the room and contributes `ResearchPerSecond` until a spell completes,
and `Availability` states 2 and 3 are the before and after), then the **training
room**, then whatever objects unlock at M6.

---

## The thirteenth sitting (2026-09-04) — GPLv3, and what it changed downstream

The owner relicensed the project from MIT to **GNU GPL version 3 or later**, to
match OpenKeeper. The licence mechanics are all in **ADR-0041** and were already
committed (`4671293`) — `LICENSE` is the verbatim GPLv3, `license.txt` is the
three-line SPDX notice as a FreeMarker template, every source file carries it,
and `LicenseHeaderTests` enforces it. This entry covers the part that was left:
**what the relicence means for how research is done.**

### The one thing to carry forward

**"No OpenKeeper code" survived the relicence, but its reason did not.**

Under MIT the rule had a legal basis: GPLv3 is copyleft, so copying its Java into
an MIT project was the one outcome the licence choice ruled out. That basis is
gone — this project is GPLv3, and GPLv3 code may lawfully be combined with it.

Three things still forbid it, and **none of them was ever a licence
consequence**:

1. **The brief** asks for clean-room behavioural reimplementation from observed
   behaviour and documented research.
2. **The owner's standing instruction**: *"reference it and compare with our own
   findings, not copy it."*
3. **It has been working.** Under facts-only, OpenKeeper has confirmed three
   scan-derived offsets, every record type's on-disk size (eight new, each to the
   byte first time), the MAP flags byte from an independent direction, and one
   place where its own layout is incomplete (Q70).

So the *practice* does not change. What changed is that **a door is now open that
used to be shut, and walking through it is the owner's call rather than a legal
question.** If they ever do, it needs its own ADR, an entry in
`THIRD-PARTY-NOTICES.md` and retained upstream notices — GPLv3 attribution is an
obligation, not a courtesy. Recorded as an amendment on ADR-0003 with a
recommendation on file, and the boundary table in `external-resources.md` is
rewritten to separate *what the licence permits* from *what this project does*,
because those two columns now disagree.

Two smaller consequences, both settled: **Flame did not move** (unlicensed is
unlicensed; our licence grants nothing over someone else's work), and **the MIT
material already here is fine** (MIT is one-way compatible with GPLv3, so the
GLKeeper WAD adaptation and YamlDotNet keep their attribution and needed no
action).

### The defect the relicence uncovered, which is worth knowing about

Worth reading ADR-0041 for this even if the licence itself is of no interest.

The MIT header was enforced by Roslyn's **IDE0073** with `file_header_template`
in `.editorconfig`, which `TreatWarningsAsErrors` turned into a build error for
free. It worked — **for MIT.** An `.editorconfig` value **ends at the first
`;`**, and the GPL's prose notice has one in

> but WITHOUT ANY WARRANTY; without even the implied warranty of

so the template silently truncated. Roslyn then enforced, and `dotnet format`
*wrote into seven files*, a notice whose warranty disclaimer had been cut in half
— **with a green build**, because the files matched the truncated template
exactly.

The MIT text happens to contain no semicolon. That is the only reason the
mechanism ever looked correct: it was never right, only untested against text
that exercised it. The three-line SPDX notice has no semicolon either, so IDE0073
*would* work today, and it is still not used — "correct until somebody lengthens
the text" is the same accident, and it fails silently both times.

**This is the fourth instance of the week's pattern** (see `GOAL_ALIGNMENT.md`):
netstandard2.1 declared behind a condition that never fired; walls-block-movement
implemented in one line and asserted nowhere; per-level variables researched and
read by no code; and now a header analyser enforcing a text it had quietly
truncated. All four were *stated correctly somewhere* and *checked by nothing that
would notice being wrong*.

`LicenseHeaderTests` is the replacement, and it is a test rather than an analyser
for a reason that generalises: **it can see `unity/DK2.Unity/Assets`, which no
project in `DK2.slnx` compiles** — only `unity/DK2.Unity.Compile` does, and that
needs an installed editor. A build-time analyser is structurally blind to exactly
the files most easily forgotten. It also asserts its own file count is above 100
before checking anything, because a sweep that finds nothing passes every content
assertion.

### Documentation brought current

| file | what changed |
|---|---|
| `docs/DECISIONS.md` | ADR-0003 marked *licence half superseded, clean-room half in force*, with a table of which of its three rules survive and on what grounds, and a recommendation on record. ADR-0037's "this project is MIT" corrected. |
| `docs/research/external-resources.md` | Licence boundary rewritten: the table now has separate columns for *what the licence permits* and *what this project does*. New section on why the project bar outlives the licence bar. |
| `DK2_REIMPLEMENTATION_PLAN.md` §10.1 | Resolved-as-MIT changed to resolved-as-GPLv3, noting the original analysis's conclusion ("copying OpenKeeper obliges GPLv3") is now moot because the project *is* GPLv3. |
| `docs/research/manual-1999.md` | The legal note on the PDF — the licence change makes no difference there, and it says so. |
| `docs/HANDOFF.md` | Header, *Decisions already made*, the research-method step 0, and the twelfth sitting's MIT-era account, now marked as having lasted about an hour. |

No source changed in this sitting: **512 tests green** (Core 17, Simulation 262,
Data 63, Behavior 84, Assets 60, Presentation 26), clean build, ten scenario
hashes unchanged.

One gap noticed and deliberately not filled: **the repository has no
`README.md`.** For a public GPLv3 project that is a real omission — what it is,
that it needs an original DK2 install, that no original asset is redistributed,
how to build. Left for the owner because a README is a statement of intent about
the project's public face.

### Next: the portal

Agreed with the owner, and everything it needs is now parsed **and** loaded — the
per-level variables work of the twelfth sitting was the prerequisite.

**Why the portal before any other room.** Nothing places non-imp creatures today
(THINGS is unparsed) and `ImpTaskSystem` skips anything without `IsWorker`, so a
non-worker dropped into the world stands still forever. Every other room's
behaviour needs occupants. The portal's behaviour *is* producing them, and it
needs no creature health, hunger, mood or experience — none of which exists.

| what it needs | where it is |
|---|---|
| who may enter | `GlobalVariables.CreaturePool`, per level — level 1 is **fifteen Goblins** |
| whether they are unlocked | `Availability` kind **2** (CREATURE), state **3** (AVAILABLE) |
| what attracts them | `CreatureAttraction` at CREATURES 0x0DAC, **two** slots, `RoomId` + `RoomSize` in tiles (Q61, Q80) |
| whether a room qualifies | `RoomInstance.TileCount` / `IsFunctional` (ADR-0027) — already there |
| how often one arrives | `EntranceGenerationSpeedSeconds` — 25 globally, **18 on level 1** |
| how many a keeper may hold | `CreaturesSupportedByFirstPortal` **15**, `CreaturesSupportedPerAdditionalPortal` **5** |
| sacking one | drop it back in; `DecreaseEntrancePersentageOfLastSackedCreatureType` and `IncreaseEntrancePersentageOfNewCreatureTypes` are **33** each |
| the room itself | `RoomTypeId.Portal` = 3, terrain 12, `Cost` 0, not buildable — **claimed, not built** (manual §4.2.3) |

**What the manual adds** (`manual-1999.md`, p.22): the pool is finite in
multiplayer; **losing a room removes from the pool every creature that depended
on it**; a sacked creature leaves immediately and is replaced if below the cap;
and the portal is `UNAVAILABLE_ROOM` in level 1's availability set, which is
consistent with it being claimed rather than built.

**Ask the owner before building** — these are all things a player would see:

1. What does a portal do when the pool is empty? Nothing, or does it keep
   offering creatures already in the dungeon?
2. Is the cap really per portal (the variable names say so), and does a second
   portal raise it by 5 immediately?
3. Is attraction re-checked continuously, or only at the moment of a spawn? (It
   decides whether selling a lair stops the next goblin or only later ones.)
4. Does the 25-second (18 on level 1) interval run per portal or per keeper?

**And note the live disagreement in Q61/Q80 before relying on attraction sizes:**
the owner *and* the 1999 manual say a Dark Angel needs a 5x5 temple; the v1.70
record says `size >= 0`. Four creatures do carry a real minimum (Bile Demon and
Giant want Hatchery ≥ 25, Royal Guard Training Room ≥ 25, Maiden Of The Nest
Temple ≥ 9), so the mechanism can express it — it just does not, for the Dark
Angel. On the observation checklist.

**After the portal:** the **library** (needs no creature needs at all — a
creature stands in the room and contributes `CreatureDef.ResearchPerSecond` until
a spell completes, and `Availability` states RESEARCHABLE → AVAILABLE are the
before and after; plus the manual's surprise that research is stored *in the
room* and lost with it), then the **training room** (Q63 measured 20 gold a swing
at a dummy, and `ModifyPlayerColdWhileTrainingPerSecond` = −5 is a typo for
*Gold*), then whatever objects unlock at M6.

---

## The fourteenth sitting (2026-09-04) — the portal, and two things it dragged out with it

**The owner lifted the no-copy bar at the start of the sitting**, explicitly:
*"if any of your doubts can be answered from OpenKeeper, feel free to copy,
reuse, whatever, we are now on the same license as them."* That reverses the
recommendation the thirteenth sitting had put on record and it is now ADR-0003's
**second amendment**. It changed how this sitting went from the first minute:
instead of listing four portal questions for the owner to play, three of them
were answered by reading `CreatureSpawnSystem.java`.

### What was built

**The portal** — ADR-0042, `PortalRules`, `PortalSystem`,
`docs/research/portals.md`. Four gates, all four in the shipped data:

| gate | data | level 1 |
|---|---|---|
| unlocked? | `Availability` kind 2 (CREATURE) | one creature: the Goblin |
| any left? | `CreaturePool`, per level per player | `creature 7 = 15` |
| attracted? | `CreatureAttraction`, two slots at CREATURES 0x0DAC | Goblin wants a **Lair** |
| room for it? | `CreaturesSupportedByFirstPortal` 15, +5 each after | 15 |

**The nicest thing it produced is a behavioural fact nobody had written down:
level 1 produces nothing at all until its keeper builds a lair**, because the
Goblin's only attraction is a Lair. The tutorial's order — dig, claim, lair,
*then* creatures — turns out to be a mechanic rather than a script.

Evidence: **29 tests** on the transcribed fixture (`PortalTests`), most of them
negative — every gate gets a test that it *stops* a creature and one that
lifting it lets the creature through, because a suite that only checked "a goblin
eventually arrives" would pass against a portal that ignored the pool, the cap
and the lair. **10 tests** against the shipped install
(`PortalOnRealDataTests`), including level 1 end to end on its own map. And one
scenario, `scenarios/creatures/a_portal_fills_a_dungeon.yaml`, whose event log
is the mechanic in four lines:

```
t359  creature_entered: type=Goblin held=0 capacity=15 interval=360
t719  creature_entered: type=Goblin held=1 capacity=15 interval=360
t1079 creature_entered: type=Goblin held=2 capacity=15 interval=360
t1619 creature_entered: type=Goblin held=3 capacity=15 interval=540
```

That last `interval=540` is the one thing in the mechanic worth arguing about —
see below.

### The one number to be suspicious of

**The spawn interval scales with the keeper's creature count**:
`max(base, base × creatures ÷ 2)`. On level 1 the first goblin takes 18 seconds
and the fifteenth takes 126.

**Nothing in the shipped data expresses this.** There is no slowdown variable, no
per-creature multiplier, nothing. It comes entirely from OpenKeeper, and it is
the difference between fifteen goblins in four and a half minutes and fifteen in
about fifteen. Level 1's own `AverageTime` = 8 (minutes) sits between the two and
settles nothing.

**It is unfalsifiable from the files and it is the top of the observation
checklist.** A stopwatch on two consecutive arrivals at two different dungeon
sizes settles it in one sitting. If a future session is ever surprised that a
level feels slow, this is the first line to suspect.

Two lesser inferences, both also OpenKeeper's and both recorded with their
reasoning: **the pool is a standing per-type limit rather than a draining quota**
(Q87 — indistinguishable on level 1, where pool and capacity are both 15), and
**a two-slot creature needs both rooms, with the size meaning one room's tile
count** rather than the keeper's total.

### Reuse is permitted; trust is not. One case proves it

OpenKeeper hands a creature to whoever claims an idle portal. Read as intent that
is a design decision worth copying. It is not intent — it resets the clock to
Java's `Double.MIN_VALUE`, which is the smallest **positive** double, not the
sentinel the code reads it as, so `elapsed − lastSpawn` ends up being the whole
game clock and satisfies any interval. **The bug is indistinguishable from a
decision unless you read closely.**

Our clock does the opposite and says so: it counts only while a keeper owns the
portal, so claiming an idle one does not hand over a banked wait (Q86). That is
now the standing caution in ADR-0003's second amendment, and it is the one thing
about the licence change worth carrying forward: *a rule taken from OpenKeeper is
STRONGLY INFERRED, never CONFIRMED, because OpenKeeper is itself a
reimplementation and can be wrong.*

### The by-product, which is bigger than the portal: not every player is a keeper

ADR-0043, `PlayerIds`. Found by asking a small question — *whose portal is an
unclaimed portal?* Level 1's portal ships owned by **player 2**.

| id | meaning |
|---|---|
| 0 | unowned — unmined rock, undug dirt |
| 1 | the hero side |
| 2 | **neutral — what an unclaimed room belongs to** |
| 3–7 | keepers |

CONFIRMED three ways, only one of which is OpenKeeper's `Player` constants: across
all 66 levels **every dungeon-heart tile belongs to a player between 3 and 7**,
and **every creature pool and creature-availability entry names a player between
3 and 7**. Level 1 shows all four ids at once.

**The obvious argument for it is wrong, and that is the part worth keeping.** I
wrote the test first as "every solid tile is owner 0, so 0 must mean unclaimed" —
and it **failed**, because rock carries a non-zero owner all over the campaign:
DK2 colours the walls bordering a keeper's territory in that keeper's colour, and
`TerrainFlags.PlayerColouredWall` exists for exactly that. Ownership of rock is a
*rendering* fact. The argument that holds is the narrower one about hearts and
pools. A test written to check a plausible sentence caught the sentence being
false, which is precisely the discipline the week's pattern asked for.

**A latent problem this exposes and which is deliberately NOT fixed here.**
`ImpSupplySystem` loops every player in the world and would hand free imps to
players 0, 1 and 2 — harmless on shipped data, since none of them has a dungeon
heart, but the rule is unstated where it should be explicit. The whole test suite
also uses player 1 as "a keeper" and 2 as "a rival", which now names the hero
side and the neutral player. **Q91, fix named** — gate on `PlayerIds.IsKeeper`,
re-key the fixtures — and left as its own change because it touches five hundred
tests written on the old convention. `PortalTests` alone uses 3 and 4.

### The gap the portal cannot close by itself

**An imp cannot claim a portal.** Room conversion is a real DK2 job with its own
balance value — `ConvertRoomHealth` = −4000, and `AttackRoomHealth` = −1500
beside it — and there is **no `TileWorkKind` for either**. So the one thing every
level expects a keeper to do with a portal is the one thing that cannot be done,
and a scenario says "claimed portal" by re-owning the tiles. `ScenarioRunner`
reports that as a shortfall rather than passing it off as setup.

**Q92**, and it is on the observation checklist because the *rates* are in the
data and only the **shape** is missing: does claiming a 3x3 portal convert nine
tiles one at a time, the way claiming floor creeps, or does the room flip whole
once enough is taken? A screenshot mid-claim settles it.

### State changes worth knowing about

- `TileState` gained `EntranceIdleTicks`, so **`HashVersion` 9 → 10** and
  snapshot **`FormatVersion` 9 → 10**. The pinned determinism hash is now
  `0x06F71648486D0FEC` (was `0x4F824667CDA7EF25`). **Every scenario hash moved**
  as a consequence — that is the version bump, not a behaviour change, and this
  commit's scenario reports are the new baseline.
- **First consumer of `SimulationContext.Rng`.** It has been carried, hashed and
  snapshotted since M1 with nothing drawing from it. The draw happens even when
  only one creature qualifies, so the RNG position cannot become a function of
  the keeper's rooms — otherwise two runs differing only in a room built and
  sold would diverge forever.
- `Dk2TerrainFixture` gained Portal terrain and room, a **Goblin**, and the three
  portal variables. `dk2tools kwd creatures -a` dumps attraction slots.
- Two now-false statements in `ScenarioRunner`'s shortfall list were corrected:
  "nothing generates imps" (`ImpSupplySystem` has for two sittings) and "a lair
  is inert" (it attracts).

### The state, precisely

**551 tests green** (Core 17, Simulation 291, Data 63, Behavior 94, Assets 60,
Presentation 26), with and without a DK2 install. Clean `dotnet build -c Release`
with no suppressions. Eleven scenarios run.

### Next

**The library**, and it is the right one for the same reason the portal was: it
needs **no creature needs at all**. A creature stands in the room and contributes
`CreatureDef.ResearchPerSecond` until a spell completes, and `Availability`
states RESEARCHABLE → AVAILABLE are the before and after — which the portal work
has already decoded and loaded. Plus the manual's surprise that **research is
stored in the room and lost with it**.

Two things now stand between any room and being interesting, and both are
bigger than a room:

1. **A creature does nothing.** A goblin arrives, stands on the portal's hole and
   stays there forever. No health, hunger, mood, experience or job. Every
   remaining room behaviour is blocked on this rather than on research.
2. **Nothing can be picked up or dropped.** That is what gates sacking (Q90),
   dropping a creature into a library or a lair, and the whole of what a player
   actually *does* in DK2 minute to minute.

Ahead of the library, the cheapest genuinely useful thing is **Q92 —
room conversion as a work kind** — because it makes a portal claimable and makes
every room-taking scenario honest, and both balance values are already in the
table.

### Later the same day: two owner sentences, two four-month defects

Three answers arrived from the owner after the portal was committed, and between
them they cost more than the portal did.

**1. "Rooms are instantly claimed by imps, not tile by tile."** Asked as Q92 —
*how does a keeper claim a portal?* — and it was really a defect report.
`TileWorkKind.Claim` already applied to room terrain, because room terrain is
`Ownable` and not `Solid` and that is everything the claim rule asked. **So
rooms had been changing hands one tile at a time, at the wrong rate, since rooms
existed**, and a half-taken portal was a reachable state. Fixed as ADR-0044:
`Claim` excludes rooms, `ConvertRoom` takes them at `ConvertRoomHealth` (−4000,
an entry that had sat unused in `VariableTypeId`), and `TileWorkSystem` reads the
`RoomInstance` *before* the work lands and hands the rest of it over — it has to
be before, because a room instance is tiles of one terrain with one owner, so the
moment the worked tile flips the rest is unreachable from it.

**2. "Imps fill only their own keeper's treasury."** Volunteered while watching
me split a test, and it turned out to be exactly the rule the new behaviour
threatened: an imp holding gold with a rival's treasury next door now takes it
and then fills it. That is fine — it fills its *own* treasury, after taking it —
and it is structurally guaranteed, since `GoldStore`'s store lookup skips every
room whose owner is not the depositor. Now asserted as an ordering (takeover
before first delivery) and as a per-tick invariant, not just as an end state.

**3. The goblin timing, and it is still the open one.** The owner sees roughly 30
seconds between arrivals, measured **flower to flower** rather than from the
portal animation — which is the right way to measure it, since the animation
cancels. It does not yet discriminate the scaling: at a small dungeon size the
rule predicts the flat base (18s on level 1, 25s elsewhere), and ~30s is
consistent with a base of 25 and a low creature count. **What settles it is the
level and the creature count**, which is now the question at the top of the
observation checklist.

### The defect that was worse, and it was not about portals at all

Implementing (1) produced a scenario that dug from level 1's heart toward its
portal and **did nothing for 130 game-seconds.** Not a crash, not a wrong number
— nothing.

The owner byte survived every terrain conversion, on a reason recorded in two
files:

> *"which owner id means 'nobody' is UNKNOWN (Q17), and every rule that cares
> asks `IsOwnable` first, so a stale byte on unownable terrain is inert."*

**Both halves were false, and each failed differently.** Q17 had been answered
*earlier that same day* by ADR-0043 — 0 is unowned — so the first half had
expired and nothing connected the answer to the decision resting on it. And
`Claim` never asked `IsOwnable`: it tests `OwnerPlayerId != playerId` and nothing
more, so the byte was never inert.

Consequence: a keeper who dug through **their own reinforced wall** got Dirt Path
still stamped with their own id — neither claimable by them (the owner matched)
nor their own land (the terrain is not ownable). Territory could not grow through
it. **On level 1 the only route out of the dungeon heart is the keeper's own
wall**, so nothing beyond it was ever reachable: no portal, no lair, no treasury.
Four months. Fixed as ADR-0045.

Every existing claiming test opened *unowned* rock, which never carried the stale
byte. The gap was specifically a wall the keeper already owned, and nothing dug
one and then tried to grow past it. Eight tests in `TileOwnershipTests` and one
on the shipped level-1 map now do.

**`GOAL_ALIGNMENT.md` gains a third review clause**, because this is a new *kind*
of failure rather than another instance: **is this justification still true — and
would anybody notice if it stopped being?** A reason resting on an open question
is a dependency, and it is the only kind nothing in this project tracks. Two
habits adopted: when a question is answered, grep for its number (Q17 appeared in
two source files, and reading them would have taken thirty seconds); and a
justification citing an open question should name what it will do when that
question closes.

### What the level-1 scenario looks like now

One dig order, and everything after it is the imps' own:

```
t276-324  dig three tiles — the keeper's own wall, then rock
t343-396  claim the floor they opened
t465      room_converted: (21,28) player=3 Portal tiles=9 spread=8 from=2
t552+     reinforce the new walls (priority 3, doing its job)
t824      creature_entered: type=Goblin ... interval=360
```

`tiles=9 spread=8` is the mechanic in one line. **t465 + 359 = t824**, so the
portal's clock starts at the claim — the decision recorded under
`TileState.EntranceIdleTicks`, now visible rather than merely documented.

### State

**573 tests green** (Core 17, Simulation 313, Data 63, Behavior 95, Assets 60,
Presentation 26). Clean Release build, no suppressions. **Twelve scenarios.** One
scenario hash moved for a real reason (`tiles-mine-gold-001`: a mined seam's tile
no longer inherits the owner the rock happened to carry).

Two rules inside room conversion are ours rather than the owner's and both are
flagged with experiments: **a dungeon heart is exempt** (Q93) and **a multi-tile
bridge converts whole** (Q94) — the second being the largest extrapolation, since
"one imp takes a ten-tile span" is a lot to draw from a sentence about portals.

### And the last thing: Ghidra is on the table, and the answer is waiting on the owner

The owner offered Ghidra at the end of the sitting. Rather than answer from
theory, four cheap questions went to `DKII.EXE` itself — **strings only, nothing
disassembled** — and they changed the recommendation. Full account in
`docs/research/executable-strings.md`.

- **Not packed** (`.text`/`.rdata`/`.data`), and `DKII.EXE` and `DKII-DX.exe` are
  the same size, so there is no ambiguity about which binary to look at.
- **All four Windows timer APIs are imported** (`QueryPerformanceCounter`,
  `QueryPerformanceFrequency`, `timeGetTime`, `GetTickCount`). That is a direct,
  bounded route to **Q1 — the tick rate**, which is the project's biggest
  blocker: `SimulationTiming.TicksPerSecond = 20` is an admitted placeholder and
  Q1 is on the *playing cannot settle* list.
- **The balance variable names are not in the executable**, so the VARIABLES
  table is indexed by number. Q86's route therefore runs through arithmetic
  rather than a name search — harder, but still narrow.
- **72 of its own C++ source filenames survive** in the retail build, and this
  is the find worth carrying: `Entrance.cpp` (so the portal's real name is
  Entrance — no `Portal.cpp`, corroborating Flame from a third direction), about
  **25 `*State.cpp` files that are DK2's creature state machine by name**
  (`HungerState`, `PayState`, `SulkState`, `ResearchState`, `ManufactureState`,
  `RunAway`, …), and **sixteen per-room files** — so "what does a room do" really
  is sixteen behaviours rather than one parameterised room.

**Open, and it is the owner's call, not a technical question (Q96).** Reading
function bodies is a different step from reading strings: the brief asks for
clean-room reimplementation and says not to copy original source merely because
reverse engineering makes it available, and decompiler output *is* source derived
from the binary. The rule would have to be **facts and formulas into research
docs, never a transcribed function** — stricter than the OpenKeeper rule the
owner just relaxed. It also wants a licence check nobody has done (GOG's EULA is
a contract question distinct from copyright) and its own ADR. **Recommendation on
record: do it, targeted at Q1 first**, as one bounded search off the timer
imports that either pays for itself in a sitting or gets dropped; then Q86's
interval scaling, because that is the riskiest thing currently shipping.

> **A gotcha that nearly produced a false finding.** The first pass used
> `strings`, which is **not installed on this machine**, so every search returned
> zero and read as a definite absence. `grep -a` on the binary was the working
> route. **A missing tool and an absent string are indistinguishable unless the
> tool's presence is checked** — and this is the second time in two sittings that
> a tool assumption cost something (see the `python -` entry above, seventh
> occurrence).

### The three things waiting for the owner

1. **The goblin interval** (Q86). Their ~30 seconds, measured flower to flower,
   is the right measurement but does not yet discriminate: it is consistent with
   the flat base *and* with the scaling at a small dungeon. **What separates them
   is which level and how many creatures were in the dungeon.**
2. **Q96**, above — the rule for decompiling, if any.
3. **Q93/Q94** — the two rules inside room conversion that are ours: a dungeon
   heart being exempt, and a long bridge converting whole.

### Ghidra: where it is, what blocks it, and the rule to start under

The owner gave the location at the end of the sitting: **`D:\ghidra_11.0.1_PUBLIC`**.

| | |
|---|---|
| GUI | `D:\ghidra_11.0.1_PUBLIC\ghidraRun.bat` |
| Headless | `D:\ghidra_11.0.1_PUBLIC\support\analyzeHeadless.bat` |
| Target | `E:\GogGames\Dungeon Keeper 2\DKII.EXE` (2,987,568 bytes, not packed) |
| Java override | `support\launch.properties` → `JAVA_HOME_OVERRIDE=` (currently empty) |

> **⚠ It will not launch as things stand.** `java` on PATH is **1.8.0_401**, 32-bit
> Oracle via `C:\Program Files (x86)\Common Files\Oracle\Java\javapath`, and
> there is **no other JDK on the machine** (searched `Program Files`,
> `Program Files (x86)` and `D:\`). **Ghidra 11.0.1 needs JDK 17 or 21.** So the
> first action next sitting is not analysis — it is a JDK, then either `JAVA_HOME`
> or `JAVA_HOME_OVERRIDE` in `launch.properties`. Checked before compaction
> specifically so this is not discovered an hour in.

**The rule to work under, unless the owner says otherwise.** Pointing at the tool
is not by itself an answer to Q96, so this is the default and it is deliberately
the conservative one:

- **Facts, formulas and control-flow *descriptions* may cross into
  `docs/research/`.** A constant, a rate, an order of operations, "this reads
  variable 3 and multiplies it by the creature count" — all fine, with the
  offset or function name as provenance.
- **No decompiler output crosses into the repository.** Not into source, not
  into a doc, not into a commit message. Decompiler output is source derived
  from the binary, and the brief says not to copy original source merely because
  reverse engineering makes it available. This is **stricter than the OpenKeeper
  rule the owner relaxed**, on purpose: OpenKeeper is somebody's clean
  reimplementation under a compatible licence, and this is not.
- **Confidence does not rise to CONFIRMED just because it came from the
  executable.** It rises to *derived from the shipped binary*, which is stronger
  than inference and weaker than an owner observation of the game actually
  running — a misread disassembly is still a misread.
- **It needs its own ADR before the first fact lands**, plus a note on the GOG
  EULA, which is a contract question distinct from copyright and which nobody
  has looked at.

**First target, and only this one:** Q1, the tick rate, off the four timer
imports. It is the biggest blocker in the project, it is on the *playing cannot
settle* list, and it is a bounded search that either pays for itself in a sitting
or gets dropped. **Second, if the first works:** Q86's interval scaling, because
that is the riskiest thing currently shipping — find where the VARIABLES table is
indexed by 3 and see whether the result is multiplied by a creature count.
`Entrance.cpp`'s assert strings are the locality hint for that one.
