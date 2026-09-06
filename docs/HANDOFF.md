# Handoff notes

Working knowledge that would otherwise live only in chat history, per
`AGENTS.md` ("Do not leave important knowledge only inside chat history").
Update this whenever a session ends or context is compacted.

---

## Current state (reconciled 2026-09-06; narrative below written 2026-09-04)

> **Reconciled 2026-09-06 (evening) against the tree at `47ce875`.** The figures
> in the next paragraph are current. The narrative that follows it was written on
> 2026-09-04 and is left as written, with the sentences that have since become
> false struck through in place and corrected beside them. Where this section
> and the sittings at the **bottom** of this file disagree, the sittings win.

**1042 tests green** with and without a DK2 install (Core 17, Simulation 646,
Data 83, Behavior 167, Assets 61, Presentation 39, Convert 29); **23 scenarios**
run; **76 ADRs**; `HashVersion`/`FormatVersion` **30**, pin `0x2E7E116CA573398B`;
**202 catalogued questions to Q197**, about fifty still UNKNOWN; **clean tree**.
Milestones: M2 ✅, M3 ✅ substantially, **M4 ◐ four of seven** (portal, lair,
sleep, hunger→hatchery; **training ✗, payday ✗**, job selection for workers only).
Beyond the M4 list: death as a process, anger and desertion, levels, the Hand of
Evil, all three carry kinds, the prison, eating, prisoners that walk and eat, fog
of war, an original-save reader (terrain only, Q182), and a converter
(`DK2.Convert`: KMF→glTF, textures→PNG) **that the Unity adapter does not yet
load**. **Nine** KWD record types parse into `GameDatabase` (TERRAIN, ROOMS,
CREATURES, VARIABLES, KEEPERSPELLS, OBJECTS, DOORS, TRAPS, MAP); THINGS and
TRIGGERS are split into tagged records and not decoded, so **no level places its
own creatures and no level has objectives**. **Still absent:** combat, a job for
any non-worker, training, payday, every spell but Create Imp, traps and doors in
the simulation, heroes, the computer keeper (`DK2.AI` is an empty project), level
scripting, win and lose. Two standing caveats: the tick rate is read as **4** a
game second but `SimulationTiming.TicksPerSecond` is still the placeholder **20**
(Q1/Q47), and every real-data test returns early without an install, so CI runs
the Behavior, Convert and Data install-backed tests **vacuously green**.

**M3 tile mechanics, the first worker, rooms + the treasury, mana, the gold
carrier, where imps come from, **where every other creature comes from**, the
asset layer and the presentation boundary: complete, and each level now plays on
its own balance numbers. M3 as a whole: substantially complete — the Unity view
renders campaign level 1.** M2 and the record types M1 needed are done, and
**every** record type's on-disk size is now known and pinned.

**M4 has started, and ~~three~~ four of its seven items exist** (the fourth,
hunger→hatchery, landed 2026-09-06, ADR-0066/0067). The portal produces
creatures (ADR-0042, ADR-0047); a creature has **health and a hunger clock**
(ADR-0048); and it has **its own bed in a lair, and sleeps in it** (ADR-0049,
CONFIRMED by the owner) — which also gave the project **the first driver a
non-worker creature has ever had**, so a goblin out of a portal no longer stands
on the hole forever. It claims a bed on arrival, walks to it when tired or hurt,
sleeps, heals only while asleep in its own bed, and wakes up. **⚠ The claiming
half of that is now known to be wrong on two counts** — see the twenty-first
sitting below and `docs/research/lair-acquisition.md`.

**Anger landed on 2026-09-04 (ADR-0053)**, unblocked by two owner measurements
that between them fixed both the ceiling and the cadence: **32767** with the
ladder driven once a game second. A creature now goes Content → Unhappy → Angry
and **walks out of the dungeon**, and a bed both prevents and cures it (−550 a
second asleep in your own bed against +100 for having nowhere to sleep). Only the
two anger sources a player can actually turn off are applied; see the seventeenth
sitting.

**Death is a process, not an event** (ADR-0051): zero health is a 60-second
window on the floor, an imp can carry the creature back to its bed, and only then
does it die — except for the eight creatures that leave no body, which die on the
spot. And **hazard terrain acts on creatures that cannot cross it** — which for
water is the two Vampires and nobody else (Q117/Q118).

**Levels landed on 2026-09-04 as a stat multiplier** (Q119), found because the
owner mentioned that *"imps gain speed as they gain more levels"* — and the
32-stat × 9-level percentage table turned out to have been parsed into
`GlobalVariables.CreatureStats` since variables were first read and looked at by
nothing. Maximum health and walking speed are now the *levelled* values. What
raises a level is deliberately absent: the experience rate's unit is ambiguous
in the data (Q124).

**And the binary is now the primary research source** (ADR-0046's 2026-09-04
amendment), at the owner's instruction. The first hour of that returned EA's own
names for ~220 creature-instance fields and 23 object kinds, corrected two claims
this project had committed, and answered the shape of Q98.

~~What a creature still has no notion of is **experience or a job**, and it cannot
be picked up or dropped. It gets hungry and cannot eat, because chickens are
objects (M6).~~ **Corrected 2026-09-06:** experience (Q119, same day), pick-up
and drop (ADR-0062, ADR-0075) and eating (ADR-0067) all exist now. **Combat and a
job for a non-worker are what M4 still owes**, and combat is the first thing
that will subtract health per *tick* rather than per second.

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
   dungeon and not touch it), **no art loading** (~~there is no converter —
   plan §7.1~~ **corrected 2026-09-06:** `DK2.Convert` exists and writes glTF
   and PNG; `DungeonScene.Instantiate` has no branch that loads them), and
   **boxes rather than DK2's walls** (Q71).

3. ~~← **START HERE: what rooms actually do.**~~ **Done for four rooms by
   2026-09-06** — portal (ADR-0042/0047), lair (ADR-0049/0060), hatchery
   (ADR-0066/0067), prison (ADR-0064 to ADR-0074) — and the graveyard is a carry
   target (ADR-0063). Library, training room, workshop, guard room, torture,
   casino, combat pit and temple still do nothing. The rest of this item stands
   as the method and the table. It was the next item, agreed with the owner on
   2026-09-04.

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
   **hatchery** — ~~which is **blocked on objects (M6)**, because a chicken is
   one~~ (built 2026-09-06, ADR-0066/0067; objects arrived with ADR-0054).

   **Two traps worth naming before starting.** First, a room's *capacity* rule is
   not one rule: Q60 established that a temple's is its **rim**, `2(w+h) − 4`,
   which nobody would have guessed, so do not assume "capacity = tile count" for
   any room without evidence. **The manual strengthens this trap rather than
   weakening it**: it says the dungeon heart stores gold in its *"outer ring"*,
   so the rim is a DK2-wide idiom, and it says a lair holds **beds** — one per
   creature, placed by preference — which is a count of objects, not of tiles.
   Second, several rooms need creatures to have needs, and ~~**creatures currently
   have no health, hunger, mood or experience**~~ (**all four exist since
   2026-09-04/05**: ADR-0048, ADR-0053, Q119 — struck 2026-09-06) —
   `CreatureState` carries only what a built mechanic reads. A room whose behaviour needs a field the
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
  deltas that do not divide by five. **Re-derived 2026-09-05/06 (Q47, Q1):** the
  binary applies `delta × efficiency / 4` **once a tick, four ticks a game
  second** (`SimulationTiming.OriginalTicksPerGameSecond = 4`, ADR-0074). At 80%
  efficiency that is the same fraction by a different mechanism, and it is why
  `TicksPerSecond` is still the placeholder 20: five applications do not fit in
  four ticks, and nobody has reconciled the two yet. Do not quote "five a second"
  as DK2's mechanism; quote it as the measured swing count it was.
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

> **This entry has now cost time on eight separate occasions, six of them on
> 2026-09-04 alone, and it was already written down each time.** The seventh was
> the most instructive and the most stupid: `python - <<'EOF' … EOF || echo "use
> sed"` — a *guard* against the failure, written as a pipe into python, which
> hung for the full two-minute timeout and took a batch of four `sed` edits down
> with it. **Wrapping the mistake in a fallback does not make it safe; the hang
> happens before the fallback can run.**
>
> **The eighth, hours later, was the seventh again with a different excuse:**
> `python - <<'PYEOF' 2>/dev/null || echo "SKIP python"` written as a
> *deliberately empty probe* to test whether python was usable — followed in the
> same command by the `head`/`cat`/`mv` that actually mattered. It hung for the
> full timeout and took all of them with it. An empty heredoc still has to be
> read by an interpreter that never returns. **There is no safe way to "just
> check" — the check is the failure.** Nothing needs testing: `perl -0pi -e`,
> `awk`, `sed` and `Write`/`Edit` cover every case, so there is never a reason to
> find out whether python would have worked. Reading a
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

> **This section is the snapshot of 2026-08-29/30, marked as such on
> 2026-09-06.** `docs/research/open-questions.md` now holds **202 rows to Q197**,
> and the questions that gate progress today are in the latest sitting's *What
> to pick up next*, at the bottom of this file. The list below is kept for the
> method it records; where an entry has since moved, the move is in bold at its
> end. Q1's duplicated line and the second Q7 entry were removed.

`docs/research/open-questions.md` ~~holds all 76 (Q1-Q72; Q51 superseded)~~. The ones that gated progress then:

- **Q53 is ANSWERED and built** (2026-08-30): 7 mana a second per imp, measured
  by the owner and read off DK2's own Create Imp spell record (ADR-0022). Left in
  this list for the method, which is in `imps-and-mana.md` §*How this was
  established*: a scan anchored on a measured number produces a trustworthy
  negative, and a scan anchored partly on a forum post does not.
- **Q59 is ANSWERED and now cross-checked** (2026-08-30, 2026-09-03): a trap is a standing mana charge, and the
  field is `Traps.kwd` offset 0x0202, confirmed against five owner measurements
  across three distinct values and pinned by `TrapsOnRealDataTests`. Not
  implemented, because a trap is an object and objects arrive at M6. **2026-09-06:
  objects arrived (ADR-0054); traps and doors are still parsed only, with no
  reference in `DK2.Simulation`.**
- **Q47 is ANSWERED and built** (2026-08-30): 273 gold a swing put an application
  at a fifth of the tabled delta, five a second (ADR-0020). Left in this list
  only to say that `DigRockHealth` is now the sole thing in the tile mechanics
  that does not fit, where it used to be one suspect among several.
- **Q40 — losing your dungeon heart is defeat.** Now fully described (the
  defeated keeper's creatures leave by the nearest portal; rivals claim their
  rooms and gold) and **not implementable yet**: it needs portals, which are an
  M6 record. Scheduled with objectives and win conditions. **2026-09-06: portals
  exist (ADR-0042); still not implementable, because defeat belongs with
  objectives and TRIGGERS is undecoded.**
- **Q7 — the creature mood model.** No longer a blank sheet. The four things that
  anger a creature are recorded and three of them map onto fields already parsed
  per creature (`AngerNoLair`, `AngerNoFood`, `AngerNoPay`). What is missing is
  the shape — accumulation, decay, thresholds. This was the area nobody had
  solved; it is now the area with a starting point. **Built 2026-09-04
  (ADR-0053): ceiling 32767, ladder once a game second, desertion. `AngerNoPay`
  cannot fire because payday does not exist.**
- **Q1 — simulation tick rate.** Blocking any timing claim. Flame's `limit_tps`
  patch proves it is fixed; the value is unmeasured. It is one constant,
  `SimulationTiming.TicksPerSecond`, and it must stay that way. **Do not go
  looking in the KWD data again**: id 74 `GameTicks` exists in the format's id
  space but no shipped file carries it — checked across every editor KWD and all
  71 map KWDs. Q1 needs measurement against the running original. **2026-09-05/06:
  measurement can no longer settle it (`EXPERIMENTS.md`); the binary and all 66
  level files read 4 ticks a game second, not yet adopted because Q47's five
  applications a second do not divide it — see `RESEARCH_STATUS.md`.**
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
  **2026-09-06: settled and built for workers (ADR-0018/0058/0061); for every
  other creature still UNKNOWN, and it lives in the 281-state table (ADR-0072).**
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

---

## Where to pick up, 2026-09-04 (fifteenth sitting) — creatures stopped being scenery

Six commits. **622 tests green** (Core 17, Simulation 341, Data 63, Behavior
115, Assets 60, Presentation 26), thirteen scenarios, clean Release build, Unity
adapter compiles. HashVersion 12, snapshot FormatVersion 12.

| | |
|---|---|
| `f86e4e5` | The portal — four gates |
| `e05d72c` | Rooms claimed whole; a keeper walled in by its own wall |
| `77c3473`/`66a3c25`/`013c312` | Executable strings, handoff, Ghidra pre-flight |
| `92f57f0` | **The entrance has five gates** — the flag gate, ADR-0046/0047 |
| `fbd9002` | **Health and hunger** — ADR-0048 |
| `42f564f` | **Beds and sleep** — ADR-0049 |
| `14962e5` | **Anger fields signed; anger deliberately not built** — ADR-0050 |

### The decompiler is authorised, and quarantined

**ADR-0046** answers Q96: the owner said *"you are free to use it and decompile
it, put all decompiled resources under reverse-engineering folder"*. The rule is
**fact / expression** — numbers, formulas and control-flow descriptions may cross
into `docs/research/` in our own words with provenance; **decompiler output never
leaves `reverse-engineering/`**, which is gitignored except its README. Binary
facts rank **below** owner observation and may reach STRONGLY INFERRED but never
CONFIRMED on the binary alone.

**Ghidra works.** JDK 26 at `C:\Program Files\Java\jdk-26.0.2.1`, project at
`D:\ghidra projects\DK2_RE`, `application.java.max` is empty so 26 is accepted
despite 21 being documented. Set `JAVA_HOME` per invocation rather than on the
machine. Three reusable scripts and the exact command line are in
`docs/research/binary-entrance.md` §"How this pass was run".

**The way in is the assert strings.** The retail build kept its `__FILE__`
strings, so 72 translation units name themselves, and a C++ unit's functions sit
contiguously — one assert plus an address range gives the whole file.

**What it refused.** The creature state machine (`HungerState.cpp`,
`LairState.cpp`) is dispatched *entirely* through vtables, so Ghidra never
discovers those bodies; four approaches failed, including forcing a function at
the nearest prologue and re-running with Aggressive Instruction Finder.
Addresses and method are in `creature-needs.md` §6. **Next thing to try: read the
vtables**, which would name all ~25 state handlers at once and is the route into
the whole `*State.cpp` family.

### What a creature is now

Health, hunger, a bed of its own, and sleep. It can be hurt, healed and killed;
it gets hungry; it claims a lair tile on arrival, walks there when tired or hurt,
sleeps and heals. **`CreatureSleepSystem` is the first driver a non-worker has
ever had** — before it a goblin out of a portal stood on the hole forever.

Still absent: **mood/anger, experience, a job**, and it cannot be picked up or
dropped. It gets hungry and cannot eat (chickens are objects, M6).

### Three owner answers, and what each cost

- **Q92** rooms are claimed *whole* → exposed that `Claim` had been taking rooms
  a tile at a time since rooms existed, plus a second defect that had **walled a
  level-1 keeper in permanently** (ADR-0044/0045). One sentence, two four-month
  defects.
- **Q101** *"heals while it sleeps in its own bed"* → replaced ADR-0048's proxy,
  which was wrong in both halves, and settled a third thing nobody asked: **a
  lair's capacity is its tile count**, which is *not* the general rule for rooms
  (a temple's is its rim, Q60).
- **Q105** *"unhappy, can't sleep, then angry, then it leaves"* → confirmed the
  ladder a second time (Q9 said the same for pay) and validated a guess: a
  bedless creature **cannot sleep at all**, not on the floor.

### Anger is blocked on one number, and that is a decision

**Do not build anger before Q106 is answered.** Every rate is in the data and
`UnhappyThreshold` is 50 on all 46 creatures — a percentage of a ceiling that
exists **nowhere**: not in the 214 balance variables, not in the record, and not
in OpenKeeper, whose `CreatureMoodSystem` is an empty stub with a TODO. 65535
makes a lairless Goblin leave after 11 minutes; 10000 makes it 2.

**This is Q47's lesson applied.** Mood feeds job selection and desertion, so a
wrong ceiling propagates instead of staying in one rule. One stopwatch settles
it — see ADR-0050 and `docs/research/creature-anger.md`.

Fixing the fields' signedness on the way found that **slapping angers and being
picked up calms**; read unsigned, picking a creature up would have added 65286.

### The questions waiting on the owner, in priority order

| Q | Ask |
|---|---|
| **Q106** | Time a creature with no lair until it leaves, or count slaps to angry. **Unblocks anger and everything after it** |
| **Q97** | Big lair vs tiny lair — does arrival pace change? Separates the interval scaling from draining eligibility |
| **Q99** | Does a creature at zero health vanish or lie there? `CreatureDyingStateDurationSeconds` is 60 |
| **Q103** | Does a goblin sleep every ~4 minutes for ~9 seconds? |
| **Q86** | Two consecutive arrival gaps *with the goblins counted* — pins the interval slope |
| **Q100** | Does a wounded imp heal while digging, or only when idle? |
| **Q93/Q94** | Is a dungeon heart claimable? Does a long bridge convert whole? |
| **Q104** | Does a converted Dwarf take up a lair tile? |

### Known and not done

- **Q91**: `ImpSupplySystem` loops every player and would hand free imps to 0, 1
  and 2. Much of the suite still uses player 1 as "a keeper" and 2 as "a rival",
  where `PlayerIds` now says 1 is the hero side and 2 is neutral. Fix: gate on
  `PlayerIds.IsKeeper`, re-key the fixtures.
- **Q98**: DK2's portal capacity sums hero portals and counts portals that
  cannot produce; ours does neither.
- **The repository still has no `README.md`.**
- `AGENTS.md` carries the owner's own uncommitted edit and is excluded from every
  commit with `git add -A -- . ':!AGENTS.md'`.

---

# Where to pick up, 2026-09-04 (sixteenth sitting) — dying is a process, and the suite stops lying about who a keeper is

Four commits. All 645 tests green with and without an install, 14 scenarios,
clean Release build, Unity adapter compiles separately.

| | |
|---|---|
| `23db5d7` | **Zero health is a 60-second window, not death** (Q99, ADR-0051) |
| `dcbb9b5` | **Imps die instantly; the sarcophagus is a lair bed** (Q111/Q112) |
| `c961118` | **Only a keeper gets imps** (Q91 closed, ADR-0052) |
| `9b8eb0f` | **The DK2 wiki as a source**, with a licence flag |

`HashVersion` 12 → 13, snapshot `FormatVersion` 12 → 13 (`CreatureState.DyingTicks`).
Pinned hash `0xAEAF871CC4814DA1`.

## The rule that came out of this sitting

**Guard on the condition, not on the bookkeeping.** The collapse is *booked*
once a game second, so between health hitting zero and the booking there is a
window where a creature has no health and is still `Idle`. Guarding the other
systems on "is in the Dying state" let two absurd things through, and a test
written for the second found both:

- a creature at zero health near its bed decided it was wounded enough to sleep,
  walked there and healed — so it **never collapsed at all** whenever a bed was
  in reach;
- an Imp at zero health on its own land regenerated 67/s before the booking and
  climbed back to 500.

Everything now guards on `CreatureDeathRules.IsDown` — *no health, or already
dying*. This matters ahead: **combat will subtract health per tick, not per
second.**

Two consequences that are load-bearing and easy to undo by accident:
- **Own-land regeneration is suppressed at zero health; lair healing is not.**
  One route off the floor, and a bed is it.
- **In `CreatureHealthSystem`, healing is applied *before* the collapse test.**
  Reverse them and a rescue collapses the creature it just saved.

## What a creature's death is now

`health 0` → **Collapse** (60s, `CreatureDyingStateDurationSeconds`, keeps its
bed and gold, does nothing) → **Die** (releases the bed, emits `leaves_body=`) →
removed.

**Except** a creature with no `GenerateDeadBody`, which dies on the spot.
CONFIRMED from two directions that never met: the owner for the Imp, and
OpenKeeper's `HealthSystem.processHealthDepleted`, which opens with
`if (!isLeaveDeadBody(entityId)) { remove(); return; }` before any unconscious
handling. **One flag decides both halves**, which is coherent — the dying state
*is* a body on the floor, a live one.

**Missing, with a named cause:** the corpse. It is an OBJECT (M6), so nothing
accumulates toward a graveyard and **no Vampire can ever be made**. The second
timer (`DeadBodyDiesAfterSeconds`, also 60) is defined in
`CreatureDeathRules.BodyWindowTicks` and deliberately uncalled so the gap is
visible where the rest of the mechanic lives.

## Do not implement vampire reincarnation before levels exist

`CanBeResurrected` is set on **exactly the two Vampires**. It is read, tested,
and branched on by nothing — on purpose. **Reincarnation costs a level, levels
do not exist, and a vampire that came back without paying would be immortal:
the immortal imp again in a cape.**

And instant death is *correct* meanwhile rather than a placeholder: with no
levels every creature is level 1, and a level-1 vampire is permanently dead by
the owner's own rule.

Three permanent-death conditions, from two sources: level 1, killed by a Monk,
**or no Graveyard under your control** (the wiki's addition — the owner did not
mention it). **Q113 is a live conflict:** the owner says it reincarnates at its
sarcophagus, the wiki says in the Graveyard, and both have data behind them.

## The wiki: use it, cite it, never vendor it

`docs/research/wiki-as-a-source.md`. Three things to carry forward:

1. **`api.php`, not the page.** A plain page fetch returns **403** — Fandom
   blocks non-browser agents. Use
   `.../api.php?action=parse&page=X&prop=wikitext&format=json`.
2. **DK2 section only.** Every page covers four games with `(DK1)`/`(DK2)`/`(DKO)`
   tags and per-game headings. The Vampire infobox gives ten corpses for DK1 and
   five for DK2 *on adjacent lines*, and the untagged lead paragraph makes a
   health claim that is not a DK2 claim at all.
3. **⚠ Licence FLAGGED, not decided.** Fandom is CC BY-SA, this repo is
   GPL-3.0-or-later. No wiki prose committed. A local mirror, if wanted, belongs
   *outside* the repository like `reverse-engineering/` — needs the owner's word.

Worth fetching when the mechanic is being built, not before: **Graveyard,
Prison, Torture Chamber, Temple, Lair, Monk.**

## Q91's real lesson

"Harmless because none of them has a dungeon heart" was a claim nothing checked
— and it was **not even harmless**: `FreeImpCountdownTicks` is hashed state and
was running for players 0, 1 and 2 on every level, so a non-keeper's imp clock
sat inside the determinism hash. That is why seven scenario hashes moved on runs
where no imp ever appears.

The fixture half hid because **two errors agreed with each other**: fifteen files
declared `Keeper = 1`, and `Dk2TerrainFixture.Map()` gave player 1 the starting
room. Re-keying the constant alone produced **38 failures**.

Still deliberately not done: **`ManaSystem` runs for every player.** Mana comes
from owned terrain and non-keepers do own terrain, so "only keepers accrue mana"
has no source. ADR-0052 records it as a decision.

## Owner questions, in priority order

| | |
|---|---|
| **Q106** | **Anger's unit — still blocks the most.** Time a lairless creature until it leaves, or count slaps to angry (+3000 each). Now confirmed *three* times from three causes (Q9, Q105, and the bed-loss answer) and still unbuildable |
| **Q113** | Sarcophagus or Graveyard? Two good sources disagree |
| **Q97** | Big lair vs tiny lair — does arrival pace change? |
| **Q103** | Does a Goblin really sleep every ~240s for ~9s? |
| **Q112** | Do Skeleton and Stone Knight really vanish on the spot? |
| **Q107** | Can a creature whose bed is sold while it lies dying be rescued? |
| **Q100** | Does a wounded imp heal while digging? |
| **Q86** | Two arrival gaps with goblins counted — the interval slope |

## Known and not done

- **No `README.md`** still.
- **Q98**: portal capacity sums hero portals and counts portals that cannot produce.
- **Next Ghidra target: read the vtables** — would name all ~25 `*State.cpp`
  handlers at once. Four approaches to the creature state machine failed; the
  addresses are in the fifteenth sitting's notes.
- `AGENTS.md` carries the owner's own uncommitted edit and is excluded from every
  commit with `git add -A -- . ':!AGENTS.md'`.
- **The `python - <<EOF` gotcha bit three more times this sitting** (occurrences
  9, 10 and 11), twice on commands with no purpose at all, and occurrence 10
  swallowed the output of the real command chained after it. There is no safe
  way to "just check" — do not type it.

---

# Where to pick up, 2026-09-04 (seventeenth sitting) — anger has a ceiling, and water has almost no victims

Three commits on top of the sixteenth sitting. **674 tests green** with and
without an install (Core 17, Data 63, Assets 60, Presentation 26, Behavior 132,
Simulation 376), clean Release build, **15 scenarios** run.

| | |
|---|---|
| `451f01f` | Brought the handoff current |
| `42e01e1` | **Anger's ceiling is 32767, and the owner measured it twice** |
| `eb30cd9` | Water is crossable, and crossing it was free |
| `7614dab` | **Crossing water is free, and only vampires drown** |

`HashVersion` 13 → 14, snapshot `FormatVersion` 13 → 14 (`CreatureState.Anger`,
`AngryChoice`). Pinned hash `0x5DB64D0C91D99368`.

## Anger is implemented, and how it got unblocked is the method

Q106 had blocked the most of anything in the project and was confirmed from
three unrelated causes before it could be built. **Two owner measurements closed
it, and it took both** — either alone is ambiguous.

1. **The stopwatch.** A lairless Goblin, `AngerNoLair` = 100: *"it became unhappy
   after approx 02:45ish minutes and angry after approx 05:30ish minutes"*.
   That gives 165 s and 330 s, so **16500 and 33000**, and a ratio of **exactly
   2** — which pins linearity, pins the angry threshold at 100%, and proves
   `UnhappyThreshold` (50) is a *percentage*.
2. **The slap count.** *"exactly 6 slaps made a creature (salamander) unhappy,
   and after that 5 slaps made it angry."* `AngerSlap` is 3000 on both the
   Salamander and the Goblin (asserted, because the observation would otherwise
   say nothing about a Goblin), so 6 and 11 slaps **bracket the ceiling into
   (30000, 33000]**.

**A rate measurement alone could not have settled it** — 100/s into 32767 and
200/s into 65535 are indistinguishable on a stopwatch (the Q47 trap). A discrete
delta scales with the ceiling where a rate does not, so the slap count excludes
65535 and 10000 outright and thereby fixes the *cadence* at once per second too.
One meaningful number survives in that window: **32767**, `short.MaxValue`, the
same saturated 16-bit value the creature record uses for "never" (ADR-0053).

`CreatureAngerRules` + `CreatureAngerSystem`, per second, `Content / Unhappy /
Angry`. **Only two anger sources are applied**, under one rule worth keeping:
*a source is applied when the player's own choices can turn it off.* No lair
(+100) and asleep in your own bed (−550) qualify; no food, no pay and no work do
not, because nothing can currently satisfy them and anger would become a
countdown no dungeon survives.

The angry choice is a **weighted draw over `AngryJobs`, drawn once and kept**.
On the fixture Goblin that is Leave 50 / Rebel 50, and on seed 5 the scenario
goblin draws **Rebel** — which is not implemented, so it stands there fuming.
That is the honest outcome and is why the draw is kept rather than re-rolled.
*(Superseded 2026-09-05: the draw is re-rolled every five game seconds, ADR-0061
and Q122. What survives is the honesty rule — an unbuilt job ends the selection
rather than falling through to `Leave`.)*

## Water: four findings from one screenshot, and one of them was mine to fix

The owner marked a gem tile on an island cut off by water, no bridge, and
**imps waded across, mined it and carried the gold home**.

1. **Passability was already right.** `WorldPathGrid` reads `CanWalkOnWater`.
   STRONGLY INFERRED → **CONFIRMED** with no code change. And the original
   states the rule in its own tile tooltip, which is the closest thing to a
   specification this project has found in the game's own text:
   *"Water. Impassable to some, others are merely hindered by it."*
   The distinction that makes it work: water carries **Impenetrable** ("cannot be
   tunnelled through") and **not Solid**. Read the wrong flag and every imp is
   stranded at the water's edge.
2. **The cost was missing.** `TerrainDef.Damage` = 100 for Water and Lava, parsed
   for months, read by nothing but `AssetCatalogue`. Now applied per second.
3. **`Damage` is not damage.** Written as "non-zero on Water and Lava alone" —
   wrong. **Hero Lair carries −67**, so the field is a **signed health delta**;
   the hero side's lair heals what stands on it. Three of 41 terrains, the same
   shape as mana (three of 41) and own-land regeneration (two of 46), and the
   third time this project has claimed a column was simpler than it is.
4. **Then the owner refuted (2).** *"no creature that can traverse through water
   looses health."* Charging everything in water would have drowned in five
   seconds the exact imp whose behaviour prompted implementing the field. The
   crossing flag now gates the cost, per hazard.

### The vampires are the whole story, and the owner named them from memory

Volunteered in a parenthesis, unasked: *"(I think vampires can't cross through
water, they need bridges)"*.

**`CanWalkOnWater` is on 44 of the 46 creatures. The two without it are the
Vampire and the Unique Vampire.** A two-element set out of 46, named from play,
in a record whose flag layout this project *reconstructed* rather than read
anywhere — the strongest single confirmation that bit has had.

And it closes a loop that was already open: the DK2 wiki's warning that water may
leave a creature *"damaged, resurrect, and even die"* sits on the **Vampire
page**. Three sources that never met — play memory, the shipped `Creatures.kwd`,
and the wiki — agreeing about exactly one creature.

So **water is a hindrance with two exceptions, not a barrier with exceptions**,
which is how the game words its own tooltip: *impassable to some* is the
vampires, literally. **Lava is where the damage rule earns its keep** — seven
creatures cross it, so 39 burn — and the Imp is the clean pair: free in water,
dead in lava in five seconds.

### The fixture has a Vampire now, and it caught a fourth missing flag

It is the only creature a water test can honestly use. Its three **absent** flags
are each a mechanic: no `CanWalkOnWater` (water charges it), no
`GenerateDeadBody` (it reincarnates, Q113), and no `AvailableViaPortal` — a
vampire is not recruited but arrives from a **Graveyard**, which its own
attraction slot asks for by room id 14, and its `LairObjectId` is 26, its coffin.

Adding it caught **the Goblin missing `CanWalkOnWater`**, above a comment that
claimed "the real Goblin's flags are exactly these two". Fourth flag the drift
guard has found absent from that one record. The argument for asserting the whole
flag set rather than the flags a mechanic happens to read today is now four
incidents long.

## The rule this sitting adds to the previous one's

The sixteenth sitting's rule was *guard on the condition, not the bookkeeping*.
This one adds: **a permission flag is usually a cost gate too.** `CanWalkOnWater`
answers "may it enter?" and "does entering hurt?", and implementing only the
first is what left crossing free. When a flag decides access, ask what access
*costs* before deciding the flag is fully read.

## Owner questions, in priority order

| | |
|---|---|
| **Q118** | **How much does water slow a creature?** CONFIRMED to happen (*"water visibly slows imps and other creatures"*), no variable in any of the 214. Hypothesis: the unread `ShuffleSpeed` (Imp 4096 vs its 7536 walk = 0.54×, Vampire 1638 vs 2867 = 0.57× — not a constant ratio, so per-creature). **Time an imp over a known run of water and the same run of floor**; ≈0.54 implicates it, anything else rules it out |
| **Q118b** | **Is hazard damage per second or per tick?** The answer removed the cheap check — nothing the owner can walk into water is charged. Needs a **Vampire dropped in a lake**: 25 s to die means per second, 1.25 s means per tick |
| **Q113** | Vampire reincarnates at its sarcophagus or in the Graveyard? Two good sources disagree |
| **Q115** | Does anger have a floor at zero, or can it go negative and bank goodwill? |
| **Q116** | A deserter's routing — the portal it came from, or the nearest exit? |
| **Q97** | Big lair vs tiny lair — does arrival pace change? |
| **Q103** | Does a Goblin really sleep every ~240 s for ~9 s? |
| **Q112** | Do Skeleton and Stone Knight really vanish on the spot? |
| **Q107** | Can a creature whose bed is sold while it lies dying be rescued? |
| **Q100** | Does a wounded imp heal while digging? |
| **Q86** | Two arrival gaps with goblins counted — the interval slope |

## Known and not done (carried forward, still true)

- **No `README.md`.**
- **Q98**: portal capacity sums hero portals and counts portals that cannot produce.
- **`ManaSystem` runs for every player** — deliberate (ADR-0052); "only keepers
  accrue mana" has no source.
- **Rebel is not implemented**, so half the fixture Goblin's angry table is a
  no-op that leaves the creature standing still.
- **Next Ghidra target: read the vtables** — would name all ~25 `*State.cpp`
  handlers at once. Addresses are in the fifteenth sitting's notes.
- `AGENTS.md` carries the owner's own uncommitted edit and is excluded from every
  commit with `git add -A -- . ':!AGENTS.md'`.
- **The `python - <<EOF` gotcha bit again** (occurrence 12), this time failing as
  a bash parse error on a heredoc that had worked twice in the same session.
  **The mitigation that works:** write the script to the scratchpad with the
  `Write` tool and run `python <path>`. Multi-line edits went through cleanly
  every time after switching. Do not type the heredoc form at all.

---

# Where to pick up, 2026-09-04 (eighteenth sitting) — the binary became the primary source, and it started correcting us

Four commits after the seventeenth sitting's handoff. **687 tests green** with
and without an install (Core 17, Data 63, Assets 60, Presentation 26, Behavior
134, Simulation 387), zero warnings, **15 scenarios**.

| | |
|---|---|
| `2aa3202` | **The binary is the primary source now, not the last resort** |
| `041e917` | A deserter walks to the portal, and a vampire comes back at the graveyard |
| `27788fc` | Twenty-five files, one class: the RTTI corrects our state machine |
| `dc9b78e` | **A level multiplies a creature, and the table was here all along** |

`HashVersion` 14 → 15, snapshot `FormatVersion` 14 → 15 (`CreatureState.Level`).
Pinned hash **`0x38CCE7E988729817`**.

## The rule that governs everything after this

**The binary is the first place to look.** The owner, twice: *"use ghidra when
possible, that is the actual dk2 data"*, then *"I want you to make using ghidra
and decompiled sources as the primary source and go to when in doubt, please
memorize that, place it where any agent will pick it up as a rule."*

Recorded in four places: `AGENTS.md`'s "Existing research" order (**in the
owner's uncommitted working copy** — it goes in with their next commit),
**ADR-0046's 2026-09-04 amendment**, `reverse-engineering/README.md`, and the
session memory.

Two things it does **not** change, both stated in the amendment so it cannot be
read as a loosening:

- **ADR-0046 rule 2 stands.** Facts, formulas, thresholds and control flow may
  cross into `docs/research/` in our own words with provenance. **Decompiler
  output may not cross anywhere, ever.**
- **Consulted first is not trusted most.** For behaviour the owner can watch the
  running game still wins, and `[Dk2Behavior]` **CONFIRMED stays reserved for
  it**. Binary-derived facts are STRONGLY INFERRED at best.

### How to run it, because this took a few tries

Ghidra 11.0.1 at `D:\ghidra_11.0.1_PUBLIC`, project `D:\ghidra projects\DK2_RE`
(already analysed, 8363 functions), JDK at
`C:\Program Files\Java\jdk-26.0.2.1` — pass `JAVA_HOME` per invocation, do not
set it on the machine:

```text
support/analyzeHeadless.bat "D:\ghidra projects" DK2_RE -process DKII.EXE \
  -noanalysis -scriptPath <dir> -postScript <Script.py> <args>
```

Scripts are throwaway Jython in the scratchpad; output goes to
`reverse-engineering/` (gitignored — `git status` must never show a file there).
**Jython 2.7 traps that cost time:** no non-ASCII anywhere in a script, not even
in a comment (em dashes are a `SyntaxError`), and **`Memory.getBytes` with a
Python `bytearray` fails silently** — it reported "0 runs" across the whole
binary and looked like a finding. Use `Memory.getInt` per word.

**The way in is strings.** The retail build kept `__FILE__` asserts *and*
printf-style debug dump functions, so one string dump names a whole struct in
EA's own words. Search for a needle, then dump every string in the surrounding
address range.

## What the first hour of that bought

`docs/research/executable-strings.md` has the detail. The highlights, each of
which lands on an open question:

- **~220 creature-instance field names.** `InWater` (a state of its own,
  separate from `IsSlowed`/`IsHasted`), `Leaving`, `TurncoatPlayer`/`TurncoatTimer`,
  `RebelActive`/`RebelLeader`/`RebelPos`/`RebelTag`/`RebelDelay`,
  `HasBeenKnockedOut`, `DieInstantly`, `PortalGeneratedFromId`, and
  **`ContributesToPortalLimit`** — which is **Q98's shape**, open since the
  portal landed, and says the per-creature side is what we were missing rather
  than the portal-counting side.
- **`LastGameTickSuccessfullyChoseAngryJob`**, which says the angry job choice
  **can fail and be retried** — evidence *against* ADR-0053's draw-once rule.
  Recorded as Q122 and deliberately not acted on: a string does not settle
  whether a retry re-draws.
- **23 object kinds** from `Object.cpp`'s union (`Lair`, `TombStone`, `Chicken`,
  `Coop`, `Gold`, `Boulder`, `ResearchBook`, …), that objects are a **tagged
  union with per-kind state**, and that a `Lair` object carries a `CreatureId` —
  the bed knows whose it is, where `LairRules` puts that on the creature.
- **No `Sarcophagus` or `Coffin` string exists**, which is what closed Q113.

## Four owner answers, and what each cost

**Q116 — deserters. ANSWERED AND IMPLEMENTED.** *"angry evil creature goes to
the nearest available portal and leaves the dungeon. Angry converted good
creature turns turncoat and starts attacking the dungeon and its creatures."*

Both halves are one mechanic split by alignment, which finally explains the
`AngryJobs` tables: **`Leave` is the evil creature's exit, `Rebel` is the
converted one's turncoat.** `CreatureAngerSystem.Leave` now walks the creature to
the nearest portal its keeper owns — nearest **by path**, Dijkstra over the same
grid it walks — and it only goes when it arrives. New:
`CreatureActivity.Leaving`, `PortalRules.IsExitTile`, and a guard in
`CreatureSleepSystem` so a leaver cannot take the last free bed on its way out.

Two consequences are behaviour, not bookkeeping: **a keeper whose portal is
unreachable keeps the creature**, and a deserter is killable and still on the
payroll for the length of its walk.

**Q113 — the sarcophagus. CLOSED, and it corrected us.** *"sarchopagus at the
graveyard, I made a wrong claim earlier."* The previous sitting had read their
first wording as the vampire's own bed, because its `LairObjectId` is a coffin.
**That was a pun, not a finding**, and it stood in an ADR and a code comment for
a day. The data was there: the **Graveyard's `CreatedCreatureId` is 8, the
Vampire** (one of only two rooms that create a creature — the other is the
Prison's Skeleton), and the Graveyard's object ids are **31–34** against the
vampire's **26**.

**Q115 — anger cools both ways. ANSWERED, and we were already right.** *"anger
can be cooled down to unhappy, and unhappy can cool down to happy."* A mood is a
pure function of the value — no latch. **The arithmetic is the finding:** cooling
is −550 a second *asleep in its own bed* and a Goblin sleeps 9 seconds in every
~250, so **a bed cures anger in instalments of exactly 4950, once every four
minutes**. The test asserts the instalment size, which is also what fails if
Q123's reading is wrong.

**Q118 — water. The owner found the confound themselves.** *"no creature that can
traverse through water looses health"* (already implemented last sitting), plus
*"water visibly slows imps"*, plus **"imps gain speed as they gain more
levels"** — which turned into the whole level mechanic below.

## Levels: the table had been parsed and ignored the entire time

`GlobalVariables.kwd` carries a **32-stat × 9-level** table of percentages
against the level-1 value. `KwdVariables` has been parsing it into
`GlobalVariables.CreatureStats` since variables were first read. **320 rows,
looked at by nothing** — while every stat read in the simulation was silently the
level-1 value and claiming to be the creature's.

Health to **600%** at level 10, melee damage the same, speed and run speed to
**250%**, experience-for-next-level to **1000%** — and **ten of the 32 stats
never scale at all**, own-land regeneration among them. Same shape as mana (three
terrains of 41) and terrain deltas (three of 41): *read the whole column before
believing a rule is general.*

Implemented: `CreatureState.Level`, `CreatureLevelRules`, and the two stats the
simulation reads are now levelled — **maximum health and walking speed**. A
level-2 imp walks at 7912 raw against 7536, which is what the owner saw.

**`CreatureHealthRules.MaxHealth` lost its old signature rather than gaining an
overload**, deliberately: a call that forgot the level would silently cap a
level-10 Goblin at 1500 health. Eight call sites updated.

**Nothing raises a level (Q124)**, and the blocker is a unit ambiguity: the
creature record says **1200** for an Imp where the balance table says
**20 a second**, and 20 × 60 = 1200. Two and a half seconds per level against two
and a half minutes. Not guessed — the owner times a fresh imp.

## What the state-machine dig actually established (Q125)

Two claims died and one target appeared.

- **"A class hierarchy with about 25 leaves" is wrong.** The build kept RTTI —
  163 mangled class names — and **exactly one is a state, `CState`**.
- **"Read the vtables" is not the route.** No static dispatch table exists: a
  scan of `.rdata`/`.data` for runs of consecutive function pointers finds eight,
  all switch jump tables, none pointing into the state code.
- **What is there:** `State.cpp` indexes a table at **0x00699688, 0x40-byte
  records**, by state id — and it is **all zeros in the file**. The state machine
  is **registered at runtime**, so the thing to read is the registration
  function — and **the writer is at ~0x004798e0, inside `State.cpp`'s own
  unit**, in code Ghidra never identified because nothing calls it: it is
  reachable only from a data pointer at 0x0068e04c, which is a **static
  initialiser**. (An earlier note here said `FUN_00476d30` did the writing.
  Wrong: that function *reads*. The claim came from counting `DATA` references
  without asking which were writes.)
- **A reusable map came out of it:** the 55 translation units with an assert
  anchor, in address order. The state units occupy roughly
  **[0x00456000, 0x00484000)** and are laid out **alphabetically**, so the 11
  state units with no anchor can be *bracketed* — `HungerState` sits between
  `GuardState` (0x00463190) and `ImpState` (0x00464240).

**Next binary task:** read the call sites of the registration function. Each
should name a state id and its handlers, which yields DK2's state enum — the
numbering nothing here has. Worth doing **before combat**, since
`FightWithDrawState`, `ThrowingState` and `RunAway` are three of the states it
would name.

## The agreed order of work, as of this sitting

The owner approved this sequence explicitly:

1. **The state registration read** (above) — cheap, and it makes everything after
   it cheaper.
2. **OBJECTS (M6)** — one unparsed record closes four half-mechanics at once:
   hunger cannot be satisfied, death owes a corpse, no graveyard can fill (so no
   vampire can exist), and gold piles do not exist. The string pass just handed
   over the object-kind list and `Rooms.kwd`'s room→object mapping, so this is
   cheaper than it was.
3. **Levels' second half** once Q124 is measured.
4. **Combat**, on a state machine that can be read. Deliberately *not* before —
   it is the mechanic where fidelity matters most, and melee damage scales to
   600%, so a combat system written against the record value would be wrong for
   every creature above level 1.

## Owner questions, in priority order

| | |
|---|---|
| **Q124** | **Time a fresh level-1 imp to level 2.** ~150 s confirms the balance table's 20 a second; near-instant confirms the record's 1200. Last gate on levelling |
| **Q118** | **The water slope**: a low-level and a high-level creature in the same water. `ShuffleSpeed` has no level row, so if it is the mechanism the *relative* penalty deepens with level (45.7% at L1, 48.2% at L2, 78% at L10) |
| **Q123** | **Does a bed cool anger before the creature lies down?** Two orders of magnitude in how forgiving anger is |
| **Q118b** | Drop a **Vampire** in a lake: 25 s to die means hazard damage is per second, 1.25 s means per tick |
| **Q97** | Big lair vs tiny lair — does arrival pace change? |
| **Q103** | Does a Goblin really sleep every ~240 s for ~9 s? |
| **Q112** | Do Skeleton and Stone Knight vanish on the spot? |
| **Q107** | Can a creature whose bed is sold while it lies dying be rescued? |
| **Q100** | Does a wounded imp heal while digging? |
| **Q86** | Two arrival gaps with goblins counted — the interval slope |
| **Q65** | Sell a full 3000-gold treasury tile: one pile or two? |

## Known and not done

- **No `README.md`.** Still.
- **Q98**: portal capacity — now known to hinge on a per-creature
  `ContributesToPortalLimit` flag, probably one of `CreatureFlags`' unknown bits
  (`Unk11` / `Unk29`). Cheap once the bit is identified.
- **Q122**: the angry-job draw may need to be a retry rather than a keep.
- **Rebel / turncoat** unimplemented, so half a Goblin's angry table is a
  creature standing still.
- **`ManaSystem` runs for every player** — deliberate (ADR-0052).
- `AGENTS.md` carries the owner's own uncommitted edits **plus the new research
  order**, and is excluded from every commit with `git add -A -- . ':!AGENTS.md'`.
- **The `python - <<EOF` gotcha**: occurrence 12 this sitting, a bash parse error
  on a form that had worked twice in the same session. **The mitigation that
  works is to write the script to the scratchpad with the `Write` tool and run
  `python <path>`** — every multi-line edit went through cleanly after switching.

## Two more commits after the handoff above (same sitting)

| | |
|---|---|
| `0f552b2` | **An imp levels by working** — Q124 answered and implemented |
| `8d49c69` | **OBJECTS is parsed, and there is no corpse in it** |

`HashVersion` 15 → 16, snapshot `FormatVersion` 15 → 16
(`CreatureState.ExperiencePoints`). Pinned hash **`0x092072D0E7C9B04E`**.
**701 tests green**, 15 scenarios, zero warnings.

### Experience: the owner answered Q124 unprompted

> *"imps get experience by working, while they do their usual things around the
> dungeon, digging, reinforcing walls, mining gold, etc. they get exp and
> passively their level raises. creatures also gain levels through experience,
> mostly training and fighting. creatures can get up to level 4 using the
> training room, up to level 8 using the combat pit, and to get to level 10 they
> need to fight actual fights."*

**Two of those caps are shipped variables to the number** —
`TrainingRoomMaxExperienceLevel` 4, `CombatPitMaxExperienceLevel` 8 — the third
time an owner statement has landed on a variable name outright, and both
variables had been unread with nothing to say what they capped.

**It also resolved the unit that had looked unresolvable an hour earlier.** The
record says 1200 for an Imp, the balance table says 20 a second, 20 x 60 = 1200,
and nothing said which was which. The answer settles it from the side: imps earn
*by working*, which is what a global per-job rate is for, so the **global's 20 a
second is the real rate** and the record's 1200 is the same rate **per minute**.
On that reading a Goblin's 200 is 3.33 a second — fifteen minutes a level.

`CreatureExperienceSystem`, registered last so a creature is paid for a second it
actually worked. A worker earns while its activity is `Working`; **150 seconds of
digging buys level 2**; an idle imp earns nothing; anything that is not a worker
earns nothing at all, and **that zero is asserted by a test** because the
tempting fix is to give everything the imp's rate and call it progression.

Two readings chosen and written down: the threshold is **spent** rather than
accumulated (Q127), and a promotion **loops** so a lump sum buys two levels
instead of discarding one.

### OBJECTS: 145 records of 894 bytes, and the correction it forced

Only the **last 82 bytes are gameplay**. The first 812 are nine art resources and
a light, so `KwdObjects` models no art resource at all and starts at **0x32C** —
thirty lines instead of three hundred.

Four checks, three self-validating: the field list lands exactly on the
independently measured **894**-byte stride; `ObjectId` at 0x35A is a
**permutation of 1–145**; the sound category reads as text at 0x35E
(`OBJECT_BED`, `OBJECT_HEART`), pinning the record's *end* independently of its
start; and five pointers from three other records land — Goblin bed **22**,
Vampire bed **26**, Tomb Stones **31–34**, Chicken Coop **35**, Training Dummy
and Target **14 / 29**.

**THE CORRECTION — a corpse is not an object.** This project wrote *"a corpse is
an OBJECT (M6)"* as the named cause of three gaps (no body, no graveyard, no
vampire) in ADR-0051, `creature-death.md` and several code comments. **All 145
objects are named and none is a corpse**; the nearest are the untyped props
Skeleton (48), Skull (49), Bone (50).

The evidence had pointed the other way all along: both timers are **creature**
variables, `DecomposeValue` is a **creature** field, and the binary's
per-creature dump carries `HasBeenKnockedOut` and `DieInstantly`. **A corpse is a
creature in a state**, so the graveyard needs a **creature-carrying imp task**,
not a record. ADR-0051's *decision* still stands (a corpse must not be a fake
creature in the creature list); what was wrong was its reason. Corrected in all
three places; other mentions of the old phrasing survive in less load-bearing
docs.

What the record does say: **12 bed objects for 46 creatures** (beds are shared,
one is the Hero Creature Bed), the **Chicken is object 9** with `TypeFood` and
decays outside a room, gold on the floor is **three** objects, and **only 52 of
145 carry any type flag** — the rest is pillars, arches and per-room furniture,
which is a reason to ask a flag rather than name an id. The union of all flags is
**0x07F7F7FF**, so `ObjectFlags` names exactly the bits the data uses and a test
asserts it.

### Where to pick up

**Q130 is the gate, and it is a design question rather than research.** OBJECTS
unblocks eating, gold piles and beds in *data* terms and immediately exposes that
there is nowhere to put an **instance**: `WorldState` has tiles and creatures and
no third list. DK2's own object is a **tagged union with per-kind state**
(`Object.cpp`: `ThreatBurn` carries a threat and a timer, a mana vault an effect
delay, a `Lair` a `CreatureId`). Whether ours mirrors that — one type with a
union — or gives each kind its own list is open, and the determinism gate cares.

**Note the ownership clash before designing it:** DK2 puts bed ownership on the
*object* (`Lair.CreatureId`); `LairRules` puts it on the *creature*. Same
relation, opposite end, and only one copy may be authoritative.

**And one correction carried forward:** the state table's writer is at
**~0x004798e0** inside `State.cpp`, in code Ghidra never identified because
nothing calls it — reachable only from a data pointer at 0x0068e04c, i.e. a
**static initialiser**. An earlier note in this file named `FUN_00476d30`, which
in fact *reads*. The wrong claim came from counting `DATA` references without
asking which were writes; Ghidra's generic reference type does not distinguish
them, and `RefsToRange.py` now tests `isWrite()`.

### Owner questions still open (none blocking)

**Q124b** time a fresh level-1 imp to level 2 (~150 s confirms the rate) ·
**Q118** the water slope, low-level against high-level in the same water ·
**Q123** does a bed cool anger before the creature lies down ·
**Q126** what level is the oldest imp in a long game — imps neither train nor
fight, so nothing says what caps them · **Q129** a dungeon heart's real health,
against a stored 34464 that looks like a truncated 100000 · plus Q97, Q103, Q112,
Q107, Q100, Q86, Q65 from the list above.

## Nineteenth sitting: objects exist (`7dc753d`)

| | |
|---|---|
| `7dc753d` | **Objects exist, and a bed knows whose it is** — Q130 answered from the binary, ADR-0054 |

`HashVersion` 16 → 17, snapshot `FormatVersion` 16 → 17, pinned hash
**`0x6B5ECAD0E9EDB2F0`**. **721 tests green**, 15 scenarios, zero warnings.

### What the binary said, and why it is the strongest OBJECTS check yet

DK2's object dump prints a literal **`Union Data:`** header and then one of
**twenty-three** blocks, every one reading from offset **0x14A**; the next field
is at 0x15C, so the union is exactly **18 bytes**. One type with a tag.

The tag is the object's **own definition id**, branched on with hardcoded
literals — and **twenty-one of those literals were paired with a name and all
twenty-one agree with our parse of `Objects.kwd`** (9 Chicken, 35 Chicken Coop,
74 Boulder, 31–34 Tomb Stones, 4 Spell Book, 29 Training Target, …). The record's
four earlier checks were all *internal*; this is the first from outside the file,
and internal consistency cannot tell you the bytes mean what you think.

**Five type flags confirmed from the code that acts on them**: `TypeGold` 0x40,
`TypeSpecial` 0x04, `TypeLair` 0x20, `TypeCrate` 0x10, `TypeSpellBook` 0x08.
**Flags beat ids** in its precedence — the `Lair` branch is reached by flag
alone, which is how one branch serves all twelve beds.

### The shape chosen

`ObjectState` in one `WorldState.Objects` list, **flat rather than a union**
(layout is not a fidelity requirement; the union's behavioural content is that
one kind's state is live, which a flat record with a documented owner per field
gives). Every field says which kind writes it, every other kind leaves it zero,
and **the hash covers all of them unconditionally** so a stray write cannot hide.

**`CreatureState.LairTileIndex` is gone.** The bed carries `CreatureId`, as DK2
does. A bed now exists **only while owned**: claiming creates an object of the
creature's own `LairObjectId`, releasing destroys it, and occupancy is "a bed
stands on this tile". `LairRules.PlaceBed` is the placement half, for a level
file and for tests.

### The defect the scenarios caught and the tests could not

**`GameDatabase.ForLevel` had been silently dropping `Objects`** ever since the
parameter was added — the constructor gained it, the call did not. Nothing
noticed for two sittings because objects were only definitions; then a goblin
claiming a bed threw for want of object 22 **on a campaign map** while all 719
tests passed, because tests build their database directly and none called it.
The real guard now sweeps **every record collection by reflection**, so the next
record is covered the day it is added.

### Where to pick up

**The Hatchery.** Hunger has been half-built for many sittings: the clock runs,
`ChickensNeeded` fills, `CreatureHealthRules.Eat` exists and nothing calls it.
Everything it was blocked on is now present — the Chicken is object 9 with
`TypeFood` and `DieOverTimeIfNotInRoom`, the Coop is 35, and there is a list to
put an instance in. The binary's `Chicken` union member is
`FreeRange` / `PeckingTimer` / `StayThere` / `ScatterGameTicks`, and
`reverse-engineering/Hatchery.c` is already exported.

Then: **gold on the floor** (three objects), and the **graveyard**, which per
ADR-0051's correction wants a creature-carrying imp task rather than a record.

**Q131 is the one thing ADR-0054 assumed and nobody has watched:** whether a
fresh lair tile looks bare or arrives furnished. It is on the observation
checklist as the cheapest item on it.

## Nineteenth sitting, continued: the hatchery, and the Rosetta stone

| | |
|---|---|
| `8746cfc` | **The hatchery lays an egg, and one variable is read 223 times** — research only |
| `37f6e5a` | **A coop for every six tiles, and an egg on one of them** — `HatcheryRules` |

**741 tests green**, 15 scenarios, zero warnings. **Hashes unmoved** — a rules
class nothing calls cannot change a world, and the new fixture terrain is not on
the pinned map.

### THE BIG ONE: the balance table's id → global map

Chasing the hatchery's interval multiplier turned up the engine's
**balance-variable dispatch**: a 240-entry jump table indexed by **id − 3**,
each entry a three-instruction stub storing the value into **one dedicated
global**. Following all 240 gives a complete map for **181 variables**, which
answers a question this project could not previously ask at all — **what code
reads variable N.** There are 214 of them and several community names have
already proved wrong. `docs/research/balance-variables-in-the-binary.md`.

**Why the map is believed:** variable 18 is
`ChickenGenerationTimePerHatchery`, and **two of the three readers of the global
it maps to are functions inside `Hatchery.cpp`**. Plus: the globals are
contiguous in id order (an off-by-one would break the ordering, not shift it);
177 of the 181 are ids we had already named; and the **59 slots with no global
are ids 4–17, the entire tile-work health block**, which the work code reads as
a set indexed by job.

### Variable 19 is a phantom, and it may be Q1

- global read from **223 instructions** — the most of any; 225 of the 226 raw
  occurrences are **direct absolute operands**, so no array-indexing inflation;
- a **multiplier 28 times, a divisor 22 times**;
- **exactly one instruction in the whole binary writes it** — its own stub;
- lives in **uninitialised `.data`**, so the image carries no default;
- and **no shipped file supplies id 19** — checked across all **1236 files**,
  with a scanner first validated against ids 3, 18 and 74 (it finds those,
  including `GameTicks` = 340 in 64 level files, exactly as Q1 recorded).

Twenty-two sites divide by it and the game does not fault. Multiply-and-divide
is the signature of a **unit conversion**; the table is in seconds and the
engine counts ticks. **Strongest candidate yet for Q1** — and note the contrast
it draws: `GameTicks` is read **twice** against variable 19's 223. **Q132, and
it must not be guessed.**

### The hatchery itself

- **coops = min(tiles / 6, 36)**, integer division: a 3×3 room gets **one**;
- **no two coops within a squared distance of 3** — not the same tile, not
  orthogonally adjacent, not diagonally adjacent. The original compares
  fixed-point distances against 3.0; integer tile distances against 3 are
  **exactly equivalent**, because a coop always stands at a tile centre;
- placement **walks the room's tiles** — no RNG, and the original has none;
- **the room lays OBJECT 47, THE EGG**, at a randomly chosen coop, or on a
  randomly chosen empty tile if the room earned no coop;
- interval = `(int)(var18 / pow(tiles, 0.315477776)) × var19`, and the exponent
  is chosen so **nine tiles gives exactly 2.0001** — the check that the formula
  was read right.

**THE EGG IS NOT FOOD**, and tests pin the negative. Only object 9, the Chicken,
carries `TypeFood`, has `HpFromChicken` against it, and can be picked up; their
`StartState`s are 10 and 0. So a transition exists that this project had never
accounted for — **Q133**, and the owner can settle it by looking at a hatchery.

`HatcheryRules` has all of the above except the clock, and the free-range cap
(12) is **exposed but not enforced** because what its per-player counter counts
is UNKNOWN. Registered as no system, deliberately.

### Also established

`DKII.EXE` and `DKII-DX.exe` are **the same size to the byte** and differ in
**1126 bytes of 2.99 MB**, the first at 0x130 in the PE header. One game, two
renderer paths — so a fact read out of `DKII.EXE`'s game logic holds for the DX
build. The registry confirms **v1.70**.

### Where to pick up

**Gold on the floor** is the next clean piece: three of the 145 objects, the
world has a list to put one in, and `CreatureDeathRules` already reports the
gold it cannot drop. Then the **graveyard**, which wants a creature-carrying imp
task rather than a record (ADR-0051's correction).

**Do not build the hatchery's clock** until Q132 is answered.

### Owner questions added this sitting

**Q131** does a fresh lair tile look bare or arrive furnished · **Q132** what is
variable 19 and what supplies it (the big one) · **Q133** does a hatchery make
eggs or chickens, and does the loose flock stop at twelve · **Q134** why the
exponent is 0.315477776 (recorded, does not matter). Q131 and Q133 are on the
observation checklist as things to settle **by looking**.

## Nineteenth sitting, closed: the owner answered three, and the tree went clean

| | |
|---|---|
| `d5cafd9` | **The owner watched a hatchery, and the cap was not the cap** |
| `137fc4f` | **Nine chickens in a three by three, and three documents catch up** |
| `806efd1` | **AGENTS.md is committed, so every agent reads the same brief** |

**750 tests green**, 15 scenarios, zero warnings, **hashes unmoved**, and for the
first time in months **`git status` is completely empty** — `AGENTS.md` is in.

### Three owner answers, and one of them overturned a claim

**Q131 — a lair tile is bare until claimed.** *"Only when a creature enters a
lair and claims a tile by dropping its bed does that tile get furniture on top of
it."* Exactly what ADR-0054 assumed; **nothing had to change**, and that ADR now
has no open assumptions. STRONGLY INFERRED → CONFIRMED.

**Q133 — a hatchery lays eggs that hatch.** About **two seconds**, then a chicken
that roams. `HatcheryRules.Hatch` swaps object 47 for object 9;
`EggHatchSeconds` records the two seconds **as an animation length**, which is
what it is.

**Q135 — one chicken per room tile.** The owner counted **exactly nine in a 3×3**
and more than twenty in a 5×5. **And the record had said so all along:** the
Chicken's `RoomCapacity` = 1, a field parsed, documented as "how many of it a
room tile holds", and read from nowhere. `FlockCapacity` takes the number from
the definition rather than hardcoding nine.

**The one that overturned something:** `MaxFreeRangeChickensPerPlayer` (12) was
this project's assumed flock cap. Twenty-five in a 5×5 rules it out — it caps
*loose* birds. **The hedge is what saved it:** the cap had deliberately been
exposed and not enforced, because what its counter counted was UNKNOWN. Enforced,
every hatchery would sit at twelve and **every test would still have passed.**

`TryLayEgg` now refuses when full, counting eggs as well as chickens. The limit
is confirmed; the *mechanism* is a reading and says so — refuse-to-lay vs
lay-and-let-rot both fit the owner's description, and refusing is what reproduces
a 3×3 settling at *exactly* nine.

### Three stale documents brought current

Between them they carried nine claims the code had already falsified.

- **`RESEARCH_STATUS.md`** said creatures had no needs, that chickens were
  unreachable because OBJECTS was unparsed, and that anger was blocked on its
  unit. Added anger, levels, objects, the corpse correction, the hatchery and the
  variable map. Its unsolved list now leads with **Q132** and names **carrying**
  as the largest single blocker left.
- **`ARCHITECTURE.md`** predated objects entirely. Gains an **Objects** section
  and a **Creature needs** section; its "a room does nothing" paragraph was
  simply wrong.
- **`scenarios/README.md`** said non-workers are inert, that a lair heals
  whatever stands on it, and that a creature has no anger or experience.

### `AGENTS.md` and the second agent

Committed because **Codex reads it**, and 430 of its 494 lines existed only in
one working tree — a fresh clone would have shown a second agent 64 lines with no
rank-of-evidence rule, no ADR-0046 quarantine, no goals. Checked before
committing: no decompiler output, no addresses, no third-party prose.

**`docs/CODEX_TASKS.md` is new** and holds three paste-ready briefs: the
unverified-claim audit (read-only, run first), Q132, and an independent
re-derivation as a cross-check. It also states the division of labour — Codex for
verification, breadth and second opinions; **not** for `docs/research/` prose,
the `reverse-engineering/` quarantine, the determinism hash, or commits.

### Where to pick up

**Carrying** is now the largest gap and several half-built mechanics name it: no
corpse reaches a graveyard, no gold is lifted off the floor, no unconscious
creature is dragged to a prison or back to its bed. **Gold on the floor** is the
smallest clean piece of it — three of the 145 objects, and `CreatureDeathRules`
already reports the gold it cannot drop.

**Do not build the hatchery's clock until Q132 is answered.**

### Owner questions still open

**Q132** what variable 19 is and what supplies it — the big one, and the
strongest candidate yet for Q1's tick rate · **Q97** does lair size change how
fast arrivals slow · **Q118** the water slope · **Q123** does a bed cool anger
before the creature lies down · **Q126** what level is the oldest imp · **Q129** a
dungeon heart's real health against a stored 34464 · plus Q103, Q112, Q107, Q100,
Q86, Q65.

## Twentieth sitting: gold reaches the floor, and objects reach the screen

| | |
|---|---|
| `dfed434` | **Gold on the floor is an object, and its limit was never a limit** — ADR-0055 |
| `858f5d0` | **Objects cross the presentation boundary, so a bed can be seen** — ADR-0035 amended |

`HashVersion` 17 → 18, snapshot `FormatVersion` 17 → 18, pinned hash
**`0x55BEDDA879B4A281`**. **786 tests green**, 15 scenarios, zero warnings,
clean tree. (Core 17, Simulation 456, Data 64, Behavior 151, Assets 61,
Presentation 37.)

### The finding that mattered: a cap that was never a cap

`MaxGoldPileOutsideTreasury` is 2000, and this project had assumed for months
that it capped a pile. **It does not.** Its global has exactly three readers in
the whole binary, all three imp code choosing where to put a load down, and all
three make one test: a candidate spot holding a pile **strictly greater** than
the variable is not chosen. Nothing anywhere clamps.

Two of the three paths that make piles routinely exceed it — a sold treasury
tile becomes a pile of 3000.

**This is the second time in one week.** `MaxFreeRangeChickensPerPlayer` was the
first, three days earlier. Same shape: a variable named exactly right for the
thing you want to limit, and the only limit anyone had found. The hedge that
saved both — expose it, do not enforce it, say why in the code — is now the
house rule for a plausible cap, and `APileMayBeBiggerThanTheLimit` is the test
that fails if someone "fixes" it.

**Q65 is answered and its premise was wrong**, which is a better outcome than
its question being hard.

### What else the binary gave up

- **A treasury tile is an object in DK2** — type 3, "Gold Pile 1", one per tile,
  created on first deposit, carrying the amount in the same union field a floor
  pile uses. Ours keeps it in the tile (ADR-0015 unchanged) because no behaviour
  separates the two; the difference is written down rather than acted on. The
  record corroborates it from the other side: `RoomCapacity` = 1.
- **A dungeon heart's capacity is `MaxGoldPerDungeonHeartTile << 4`** —
  hardcoded, as a shift, not a count of the room's tiles. 16000, which is what
  the owner measured (Q52), so ADR-0027's "sixteen of the 25 tiles store gold"
  reading is confirmed from a second direction.
- **A creature drops its gold in one, two or three piles** — one below 1000, two
  below 3000, three above — each holding `gold / piles` with integer division,
  **so the remainder is destroyed**. 2999 leaves two piles of 1499 and one gold
  that ceases to exist. Reproduced.
- **DK2 spreads a deposit evenly across a player's treasuries and heart**, room
  by room, and only then drops the remainder on the floor. `GoldStore.Deposit`
  works in tile-index order and already said that order was ours; **this is what
  DK2's actually is**, and closing the gap is a separate hash-moving change.
- **A pile is on nobody's books.** Making one out of a treasury tile subtracts
  the amount from the player in the same breath.

### Objects can now be seen

`ObjectView`, an `object` asset category, three more `IRenderTarget` methods.
**This was a hole, not a feature:** objects had been in the world since ADR-0054
and every renderer drew bare floor where one stood — so the scenario pictures,
which are how this project checks its own work, were silently lying about a
whole category of state. The sell scenario's picture now shows a `$` where the
treasury was.

Objects draw **under** creatures, and the order lives in `ScenePresenter` so the
two renderers cannot disagree. No animation variants and no sub-tile position,
both for the same reason: the simulation cannot yet distinguish an object state
or move an object, and inventing either would put keys in a converter's work
list that correspond to nothing.

### Where to pick up

**Carrying, still — and it is now one owner question away.**

`FloorGold` makes the gold; nothing fetches it. The mechanism is mostly the
mining tail that already exists. What is missing is the **trigger**, and the
trigger cannot be guessed: fetching is not in the priority order the owner gave
(marked excavation → claim → reinforce, Q24 CONFIRMED), and the binary does not
offer it cheaply — an imp's job selection is part of the runtime-registered
state machine (Q125), not a visible list.

**So Q141 is at the head of the observation checklist**, and it is the cheapest
item on it: mark a wall, drop gold nearby, watch one imp. Four sub-questions,
one glance each, and the fourth (does an enemy imp take your dropped gold)
settles Q136 as a side effect.

After that: the **graveyard**, which wants the same creature-carrying task
(ADR-0051's correction), and **treasury deposit order** (§5 of
`gold-on-the-floor.md`) as a self-contained hash-moving correction.

**Do not build the hatchery's clock until Q132 is answered.**

### Owner questions added this sitting

**Q136** whose a floor pile is — a level-wide player id, not the room's owner ·
**Q137** does leaving by portal drop gold, as dying does · **Q138** does mined
gold an imp cannot hold stay on the floor · **Q139** which tile an overflowing
refund uses (ours) · **Q140** what a blocked imp does with its load ·
**Q141** where fetching sits in the priority order — **the one to ask first**.

## Twentieth sitting, closed: the owner answered the trigger, and corrected himself

| | |
|---|---|
| `bcca5f3` | **Gold on the floor is the first thing an imp does** — ADR-0056, Q141 |

**795 tests green**, **16 scenarios**, zero warnings, **hashes unmoved** — no imp
in any pinned run has ever met floor gold, so nothing that was already being
measured changed.

### The answer was not the strong form, and the owner caught that the same day

**Read the next section before this one.** What follows is what this project
believed for a few hours, and the section after it is what the binary says. The
sequence is kept rather than tidied away, because the way the wrong version got
in is more instructive than the right version.

### What was believed first

> *"If there is gold on the ground an imp will first take from that gold pile and
> take it to a treasury, until all of it is gone."*

Reported from a dungeon where imps had mined a **gem** tile, gold was all over
the floor, and **nobody was mining it** — the tile still there, still marked,
still inexhaustible. So fetching does not merely belong in the priority order,
it **displaces marked excavation**, which had been the top of that order since
ADR-0018.

Built the same day: `CreatureActivity.FetchingGold`, searched ahead of the three
tile-work priorities. Above claiming and reinforcing by transitivity rather than
by a second observation. Nothing else needed inventing — the pick-up hands over
to `DeliveringGold`, which is the same mechanic whatever put the gold in the
hands, and the one-imp-per-pile cap is the floor stance's existing cap rather
than a new rule.

`imps-fetch-dropped-gold-001` is the whole thing in fifteen log lines: digging
at t35, gold on the floor at t40, the wall in hand finished at t52, and at t53
**the imp walks past two still-marked walls** to reach the pile.

### The correction was worth more than the answer

> *"One important correction on my part, where I made a mistake: when I marked
> that gem tile for excavating and it wasn't connected to my territory via bridge
> and tiles around it weren't claimed, imps mined it, but they didn't carry any
> gold to treasury/dungeon heart."*

**Nothing here codes that and it reproduces it anyway.** Both searches walk, so a
stranded imp picks the gold up, finds nowhere to put it, and stands there while
the pile grows — the same shape as Q35's treasury spillover, arrived at rather
than coded.

**And it is evidence for the outcome, not for the rule.** The tile was neither
connected *nor* claimed, so reachability and an ownership test on the ground both
fit. **Q144**, and one bridge to an unclaimed tile separates them. Written down
rather than resolved, because assuming reachability *because it is what we
happen to implement* is exactly the error this project keeps finding in its own
history.

### One nuance that is ours, and it is flagged

The imp finishes the tile it is already swinging at before it turns round.
Fetching outranks excavation at the moment a job is **chosen**. Whether the
original interrupts a swing in progress is **Q145** — and the owner's own
observation cannot settle it, because a gem tile is inexhaustible: an imp
re-chooses after every completion either way, so both rules look identical from
outside. **A finite job separates them**: mark a long stretch of ordinary rock,
let imps get into a tile, then drop gold beside them.

### Where to pick up

**Carrying is no longer the largest gap.** What is left of it is the
**creature-carrying** task — a corpse to a graveyard, an unconscious body to a
prison or back to a bed — which ADR-0051's correction already named and which
needs no new research to start.

Then: **treasury deposit order** (`gold-on-the-floor.md` §5) as a self-contained
hash-moving correction, and the **graveyard** itself.

**Do not build the hatchery's clock until Q132 is answered.**

### Owner questions added this sitting

**Q142** does a part-loaded imp top up from a pile it passes · **Q143** does the
pick-up cost time · **Q144** reachability or claimed ground — **the one to ask
first, and it is one bridge** · **Q145** does fetching interrupt a swing already
in progress.

## Twentieth sitting, reopened: the owner doubted their own report, and was right to

| | |
|---|---|
| `572923e` | **The priority was wrong, and the owner caught it** — ADR-0056 amended, Q141 and Q144 re-answered from the binary |

**796 tests green**, 16 scenarios, zero warnings, hashes unmoved.

### What happened

`bcca5f3` shipped fetching gold **above** marked excavation, labelled CONFIRMED,
on the strength of the owner's *"if there is gold on the ground an imp will first
take from that gold pile"*. Hours later the owner read their own report back,
said they were **not sure**, and asked for a Ghidra pass rather than letting it
stand.

**The binary disagrees, and it is not close.**

### DK2's imp task list, which is the real find

The creature dump prints an **`ImpJobData`** block — `ScanningJob`,
`DroppedTask`, **`LastTaskType`**, `ImpTaskID`, `ImpTaskSlot` — and the player
dump prints **`TaskStack`**, `TaskListHead`, `NumberInTaskList`,
`NumberOfFreeTasks`. So a keeper accumulates **tasks with kinds**, and an imp
claims one. Three functions carry it: claim-a-task-of-kind-K-near-here,
raise-a-task-of-kind-K-here, and a `switch` on the kind to its handler.

**One function tries the kinds in a fixed order and returns on the first that
takes:**

| # | kind(s) | what |
|---|---|---|
| **1** | **1, 2** | **dig a marked tile / mine a marked gold-bearing tile** |
| 2 | 3 | unidentified — tests Lava and Water on owned ground, so a bridge |
| 3 | 9, 10 | unidentified |
| 4 | 13 | unidentified, and the biggest handler |
| 5 | 6, 16 | unidentified — tests Solid, so walls |
| **6** | **7** | **pick up a floor object flagged `TypeGold`** |
| 7 | 8 | unidentified — reads the whole object flags word |
| 8 | 12 | unidentified — tests for unowned ground |
| 9 | 11 | unidentified — tests Solid |

**Kinds 1 and 2 are pinned hard**, not inferred: every other kind is raised by
the handler that consumes it, but 1 and 2 are raised by the **tile-marking
code**, which picks kind 1 when the terrain's `GoldValue` is zero and kind 2 when
it is not. Our own parsed field selecting the task type.

So **Q24 stands exactly as ADR-0018 has had it since August.**
`docs/research/imp-task-priority.md` is new and holds all of it.

### And the same read answered the owner's other correction — differently

Their disconnected gem tile sent no gold home because the gold task **refuses a
tile that is neither water nor owned by the worker's own player**, before it
looks at anything standing on it. **Unclaimed ground was enough on its own; the
bridge never came into it.**

This project had reproduced that outcome **by accident** — both its searches have
to walk, so a stranded imp could not reach a store — and had written the question
down as "reachability or ownership, one bridge separates them". **The outcome
matched while the rule was wrong.** That is the exact shape of the three
multi-month defects in this project's history, and it happened again inside a
single day, with a green suite, a scenario that looked right, and an ADR that
said CONFIRMED. Q144 is closed as **ownership**, and `CanFetchFrom` tests it.

### THE THING TO READ NEXT: Q147

**DK2's kind chain is a callback**, registered in a table and invoked from a
scan, and every handler takes a position and looks around it. So the original's
primary key is **where**, and the kind order breaks ties **within** a position.
This project does the opposite — one whole-map search per priority.

**That explains the owner's observation with no priority claim at all.** Gold was
lying on every tile around the imps, so the nearest position with any task was a
gold position; every imp fetched while the gem tile stood marked and untouched.
Not because gold outranks mining — because it was underfoot.

**Not built.** It changes how *every* job is chosen. The experiment is on the
observation checklist and takes two minutes: mark a wall far from one idle imp
and drop gold at its feet; then mark a wall right beside the same imp with gold a
few tiles off. If the far wall wins the first, our shape is right. If the gold
wins the first and the near wall the second, this project's whole job-selection
model is wrong in a way that has nothing to do with gold.

### Where the code stands

- **marked excavation first** — CONFIRMED twice now, by the owner and by the
  binary;
- **gold second**, above claiming and reinforcing. Only "below excavation" is
  read; the rest is **ours** (Q146), chosen because putting gold last makes the
  mechanic dead — an imp can nearly always find a wall to reinforce;
- **an imp will not take gold off ground its keeper does not own**;
- the scenario now shows an imp **carrying on digging with 2000 in gold four
  tiles away**, and `MiningComesBeforeFetching` is the test that holds it there.

### Owner questions after this sitting

**Q147** is the priority global or positional — **ask this one first, and it may
retire Q146 entirely** · **Q146** where fetching sits below digging (ours) ·
**Q145** does fetching interrupt a swing in progress · **Q142** topping up a
part-load · **Q143** does the pick-up cost time · **Q136** which player id a
floor pile carries — both ends of it now agree, only the value is unread.

**Q141 and Q144 are closed from the binary.** **Q132 still blocks the hatchery
clock.**

## Twenty-first sitting: the owner filmed the game, and the clock was the wrong shape

| | |
|---|---|
| `11210d5` | **The free-imp clock was a countdown, and the game keeps a deadline** — ADR-0057, Q56 closed, Q54 confirmed a second time, Q100 answered |

**799 tests green**, 16 scenarios, zero warnings, quarantine holding.

### What happened, and the pipeline is the point

The owner **recorded DK2 playing** and had a second assistant analyse the
footage into a numbered list of questions for the decompiler. That list named the
free-imp timer as the most important target, on the grounds that the running game
**directly contradicted** what this project had shipped. It did.

**No test could have caught this**, because every test agreed with the wrong rule.
The only oracle was the game.

### Q56: it is a deadline, not a countdown

Ours held a countdown at zero while a keeper was up to strength and started a
fresh full five seconds when they fell short. The doc-comment defending it said
the alternative "is indistinguishable in the steady state and differs only in how
fast the first replacement arrives after a long spell at full strength". **Both
clauses were wrong in one sentence.**

The recording: thirty seconds at exactly four imps → one sacrificed → replacement
in about **1.4 seconds**, then ~5s each for the rest.

The binary, in our own words — the keeper's per-frame update, eight instructions:

    if (imps < MinimumImpThreshold and this keeper's nextFreeImpAt < now)
    {
        cast Create Imp at the heart, free;
        nextFreeImpAt = now + TimeBeforeFreeImpGeneratedSeconds × <variable 19>;
    }

**Nothing else in the executable touches that field.** So a keeper at full
strength carries a stale deadline and their next loss is replaced **on the spot**.

### Three more facts from the same eight instructions

- **the deadline advances whether or not the imp is placed** — no test of the
  call's result — so a keeper with nowhere to put one burns the interval;
- **a free imp is genuinely free**: the free flag skips the availability check,
  the mana check and the deduction;
- **`TimeBeforeFreeImpGeneratedSeconds` is multiplied by variable 19** — the
  223-reader global no shipped file supplies. Seconds in, engine time out. The
  clearest single use of it found so far. **It still does not give the value, and
  Q132 still blocks the hatchery clock.**

**And Create Imp's price is now read rather than reported.** The original computes
`max(1, imps − MinimumImpThreshold + 1) × base` — our formula including the
clamp, which the owner's play could not have shown because it only bites below
the floor. **Q54 confirmed twice.**

### What it cost

`PlayerState.FreeImpCountdownTicks` → **`NextFreeImpTick`**; HashVersion and
snapshot FormatVersion **18 → 19**. **Three tests changed meaning rather than
being repaired**, and that is worth knowing before touching them: there is no
longer any such thing as "the window before a keeper's first free imp" — a keeper
with a heart and no imps is staffed on tick zero. `ManaTests` now runs **without**
`ImpSupplySystem` so heart-income assertions measure one mechanic, and two
`ManaUpkeepTests` are re-based on the land a keeper *starts* with.

### THE THING TO READ NEXT: the lair is contradicted and NOT fixed

`docs/research/lair-acquisition.md` is new and is the design. Two counts:

1. **the trigger is not availability.** A free lair tile sat there while a
   bedless Goblin and Bile Demon went to the **Training Room**; the search began
   only when the mood turned Unhappy over having no lair (**Q150**);
2. **claiming is a journey with a test at the end.** Creatures crossed a lair,
   walked up to occupied tiles, **rejected them** and carried on. A controlled
   1×5 lair filled **1, 3, 5, 4**.

Ours claims a bed on the first tick one is free, from wherever the creature
stands. `CreatureSleepSystem` now says so at the top rather than hiding it.

**Half the replacement is read and half is not**, which is why it is written down
instead of built. Read: the room is chosen by **distance to its centre**; the tile
search on arrival is **own tile → a fixed table of neighbours → a chained walk of
the room**; the chosen point is jittered by a random angle *within* the tile. Not
read: the trigger (**Q150**) and the extra test that makes one lair room preferred
over another (**Q151**).

### Two smaller results

**Q100 is answered and ours was right by omission.** A wounded imp healed **while
digging** and **while running**. Combat, the Hand of Evil and possession are still
open (**Q152**) and are ten-second looks, not decompilation jobs.

**`TimeBetweenReattemptingSleepSeconds` is read by ZERO instructions.** The third
balance variable to fail its own name, after `MaxFreeRangeChickensPerPlayer` and
`MaxGoldPileOutsideTreasury`. Anyone building sleep retries would have reached
straight for it. **Before building on a variable's name, look at its reader
count** — `balance-variables-in-the-binary.md` has them all.

### A new research multiplier

`docs/research/source-file-map.md` — the original's **72 translation units** by
name, which is how the lair code and the keeper update were found at all. DK2's
creature AI is **one file per state** (`LairState.cpp`, `HungerState.cpp`,
`TrainState.cpp`, …), which is worth knowing before designing anything that
touches it.

### Owner questions after this sitting

**~~Q148~~ answered in the same exchange: an imp in the Hand of Evil
DOES count towards the four** — hold one of exactly four and no fifth appears,
which is what the binary's owner-change tally implied. The imp portrait's figure
is a *different* count and excludes them, so **do not reuse it when the Hand of
Evil is built**; the rule is written at `ImpSupplyRules.ImpCount`, which is the
code that would break. · **Q150** what makes a creature go for a lair
· **Q152** does regeneration continue in combat · **Q149** is the ~1.4s
simulation or animation · and **Q147 is still first in line**, because it may
retire Q146 entirely.

## Twenty-first sitting, closed: the docs caught up, and the new rule found something

| | |
|---|---|
| `7b58f3c` | **An imp in the hand still counts** — Q148 answered by the owner, and written where it will break |
| *(this)* | **The docs, the Codex brief, and a review clause that paid on its first run** |

**799 tests green**, 16 scenarios, zero warnings, clean tree, quarantine holding.

### Q148: an imp in the Hand of Evil counts towards the four

The owner held one of exactly four imps and **no fifth appeared**. Two lines of
evidence arrived separately and agree: the tally the original's free supply reads
is moved in exactly two places, when a thing gains an owner and when it loses
one, and picking a creature up does not change whose it is.

**The useful part is what it prevents.** DK2 keeps **two** counts of a keeper's
imps — this one, and the imp portrait's figure, which visibly *excludes* held
imps. When the Hand of Evil is built, the obvious thing to reuse is the count the
interface shows, and that would let a player hold one imp indefinitely and farm a
fifth out of the dungeon heart. Nothing enforces it today because nothing can be
picked up, so the rule lives in `ImpSupplyRules.ImpCount` — **on the code that
would break it**, not only in a research note.

### The fourth review clause, and it found one the same day

`GOAL_ALIGNMENT.md` now carries a fourth standing question, and it exists because
of how the free-imp defect survived three weeks:

> **when a comment says a difference is unobservable, does it name the
> observation that would settle it? If it does, that is not a defence — it is an
> experiment nobody has run.**

`ImpSupplySystem`'s doc-comment had described the exact experiment that refuted
it, in the sentence defending it. So: **grep the codebase for hedges** —
*indistinguishable*, *differs only in*, *not observed*, *makes no difference*.

**Seventeen hits, one promotion.** Most are honest — "indistinguishable **on the
shipped data**" is a limit of the evidence, and `RoomRules.RefundPerTile` is
squarely that. But `PortalRules.PoolLimit` hedges with *"on level 1 the two are
indistinguishable"*, and the campaign has 65 other levels. **Q87** — is a
creature pool a standing limit or a draining quota — is decidable by playing any
level where the pool exceeds the portal capacity gating it, and in four months
nobody had put it on the observation checklist. It is on it now.

Third kind, named so it is not re-audited: `WorkStance`'s Q49 linger really *is*
invisible from outside, so it is a decompiler question rather than an observation
one — same shape as Q149.

### A negative result expired, and the method matters more than the fact

On 2026-09-04 this project recorded that the creature state machine **could not
be reached** in the binary: four approaches, all failed, vtables named as the next
thing to try. That is now marked superseded in both `creature-needs.md` §6 and
ADR-0048.

**The route that worked was already half-present in the failed method.** Scan
initialised memory for the assert string's own address (`ScanForAddr.py`), then
**decompile the neighbourhood** rather than trying to make Ghidra own the exact
instruction. `LairState.cpp` shows zero referring functions by Ghidra's reference
database and one by the raw scan, and that one hit lands inside the
lair-acquisition code. Ten minutes, no vtables.

**The general lesson:** a recorded negative result is a snapshot of what was
tried, not a property of the binary. Prefer questions that widen ("what is
compiled either side of this?") over ones that narrow ("which function owns this
instruction?").

### Docs updated this sitting

- **`docs/CODEX_TASKS.md` — Brief 4 is new and is the highest-value entry in
  that file.** It writes down the pipeline that produced this whole sitting:
  owner records the game → Codex turns footage into a four-part document → the
  decompiler answers the numbered questions. The prompt is pasteable. It carries
  the reading rule (**its observations are oracle, its inferences are reasoning
  about our repo**) and the list of what to film next, headed by **Q150** and
  **Q147**.
- `RESEARCH_STATUS.md` — two new solved entries (the source-file map, the free-imp
  deadline) and a new unsolved one (lair acquisition, contradicted and
  deliberately unbuilt).
- `GOAL_ALIGNMENT.md` — a 2026-09-05 review: why this defect was the first that
  **no internal review could have caught**, the fourth clause, and the hedge audit.
- `ARCHITECTURE.md` — the bed-is-an-object passage now says the *relation* is
  right and the way a creature reaches it is not.
- `unity/README.md` — the level-1 walkthrough's imp timing.
- ADR-0021 amended (free-imp line), ADR-0048's "what the binary could not say"
  marked solved.

### Where the next session should start

**Nothing is half-done and nothing is blocked on me.** In order of value:

1. **Q150 — the lair trigger.** The design is written
   (`lair-acquisition.md`), half of it is read, and it is the largest known
   fidelity gap in creature behaviour. Needs one recording.
2. **Q147 — is imp job priority global or positional.** Two minutes of play, and
   it can invalidate the whole job-selection model.
3. **Q132 still blocks the hatchery clock**, and this sitting added weight to
   what variable 19 is *for* without touching what it *is*.
4. **Treasury deposit order** (`gold-on-the-floor.md` §5) is still the
   self-contained hash-moving correction available with no new research.

## Twenty-second sitting: one sentence from the owner, and the imp's whole job list

| | |
|---|---|
| `0daa03c` | **An imp chooses in two tiers, and gold is near rather than last** — Q146, Q147, Q142 closed; Q153–Q155 opened; ADR-0058 |
| `3e9d697` | **An imp claims the tile it has just dug, and the mover stops clipping corners** — Q154 built; ADR-0059; HashVersion and snapshot 19 → 20 |

**809 tests green**, 16 scenarios, zero warnings, clean tree, quarantine holding.

### What happened

The handoff asked for two recordings. The owner answered one of them in a
sentence instead — *"imps prioritise walls over gold on the floor, in both
cases"* — and that sentence, run back through the decompiler, produced the
whole of DK2's imp job selection. The research note that had said "gold is
sixth of nine" was rewritten from the top.

### How an imp chooses, read from `ImpState.cpp`

**Two tiers.** A byte table at `00698658` — `0e 08 09 01 03 0d 0c ff` — is
searched **whole-map, one kind at a time**: *claim the tile you just dug* (14),
loose objects to their room (8), build placed traps and doors (9/10), **dig and
mine** (1/2), **claim floor** (3), carry the unconscious to Lair or Prison (13),
corpses to the Graveyard (12). Only when every one of those is empty does the
imp **ring-scan outwards** trying, at each tile, **reinforce/fix (6/16) → gold
(7)** → objects (8) → an unidentified kind 11.

So a marked wall beats gold anywhere; a wall beats gold *on the same tile*; and a
nearer pile beats a farther wall. **The nine-step chain the previous sitting
had called "the order" is the per-tile callback of a two-tile scan that runs
once after an imp is dropped.** A fixed-order chain is not a priority list
until you know who calls it.

**All thirteen kinds are identified**, corroborated by the interface's own icon
table (`SJ-Dig, SJ-Claim, SJ-Reinforce, SJ-Fix, SJ-Take_Gold, SJ-Take_Crate,
SJ-Graveyard, SJ-Prison…`). Two of the earlier identifications were the right
flag read backwards: kind 3 tests for the *absence* of Water and Lava and is
claiming; kind 12 tests for *owned* ground and carries corpses. Also read: imps
claim **enemy** floor unless allied; the deposit routine picks the **nearest**
treasury or heart with room; an imp tops up a part-load until its hands hold a
level-scaled share of `MaxGoldHeld` (Q142).

### What changed in the code

- **`ImpTaskSystem.TryTakeJob` is Tier A** — claim-the-just-dug-tile, marked
  excavation, claim, each a whole-map search. **`TrySweep` is Tier B** — one
  nearest-first search asking *reinforce here?* then *gold underfoot?* at every
  standing tile. Gold dropped below claiming, and against reinforcing it is
  decided by distance. The ADR-0056 argument for gold-second ("gold last would
  never be collected") was a true premise in the wrong model: gold is not
  *last*, it is *near*.
- **`TileState.FreshlyDug`** (hash and snapshot 19 → 20): set when excavation
  destroys a tile, cleared by the next change of terrain. `ClaimFreshlyDug` is
  taken once none of the tile's **eight** neighbours still carries a *dig* mark
  (mining next door does not hold it back — the original checks its kind 1
  only). This is the dig-then-claim rhythm the game visibly has; ours used to
  dig every mark on the map before claiming any.
- **The mover walked a diagonal leg 45° first, then straight.** From an
  off-centre start — and creatures stop wherever they *crossed into* a tile —
  that sweeps through the side tile, and on tick 440 of the four-imp run the
  side tile was the gem face. Now **surplus of the longer axis first, then
  45°**, which runs exactly through the shared corner and touches nothing else;
  `LegLength` is octile; one guard nudges a creature a single position unit off
  the exact corner. ADR-0025's corner-cut stands. The pin did not move for this.

### Three tests changed meaning rather than being repaired

With bare rock beside an imp, the wall now correctly wins over gold two tiles
away, so the older gold-fetching tests pre-reinforce the room's walls
(`WallsAlreadyReinforced`) to measure fetching alone. Four new tests pin the
competition; six pin the just-dug claim (`ImpClaimsWhatItDugTests`).

### Docs

`imp-task-priority.md` rewritten (keep §9 — what the first read got wrong, and
why); ADR-0058, ADR-0059; Q141/Q142/Q146/Q147 rows rewritten and Q153–Q155
added; `imp-tasks.md` summary row; `gold-on-the-floor.md` §9; the floor-gold
scenario's header and duration (900 → 1800, so the second trip is in the log);
observation checklist (Q147 struck); Codex Brief 4's film list (Q147 done).

### Where the next session should start

**Nothing is half-done.** In order of value:

1. ~~**Q150 — the lair trigger.**~~ **Read the same day — see the section
   below.** What is first now is **building the lair rework**, every line of
   which is read (`lair-acquisition.md` §7).
2. **Q132 — variable 19's value** still blocks the hatchery clock.
3. **Treasury deposit order** (`gold-on-the-floor.md` §5) remains the
   self-contained hash-moving correction needing no new research — and this
   sitting confirmed the *carrier's* half of it from the binary (nearest room
   with space).
4. ~~**Q153 — the reinforce ceiling field**~~ — closed the same day: it is
   `MaxHealth`, and the file offset was wrong (see the last section).
5. **Q155 — task stealing by distance** — not built; matters once several imps
   share a small dungeon in a scenario.

### Owner questions after this sitting

**Q150** what makes a creature go for a lair · **Q152** does regeneration
continue in combat · **Q87** is a creature pool a standing limit or a draining
quota (any level where the pool exceeds the portal cap) · **Q66** does
"adjacent to claimed ground" include diagonals — **now live in a new way**,
because the just-dug claim tests eight neighbours for marks while our
ownership-adjacency test is orthogonal, and the original's claim handler
(`FUN_00453b30`) is orthogonal too, so the two are at least consistent.

## Twenty-second sitting, continued: the owner corrected a recording from memory, and Q150 fell

| | |
|---|---|
| `8a89d78` | **What sends a creature to get a lair is being idle without one** — Q150 and Q151 closed, `Unk8` → `IsGood`, Q156 opened |

**811 tests green**, 16 scenarios, zero warnings, clean tree, quarantine holding.

### What happened

Asked what was needed for Q150, the owner said *before I do anything* — and
then that Salamander, Goblin, Warlock and Firefly **always** went for the lair
first, that the creature in the earlier recording which trained first was a
**Troll** and not a Goblin, and that Bile Demon and Troll were **not consistent**
about it. A remembered observation contradicting a recording's reading. The
binary was asked to arbitrate and sided with the memory.

### The read (`lair-acquisition.md` §5, §8)

- **No mood gate.** The creature's decision routine runs its state's update,
  then **needs** (hunger, then sleep), then — only if unhappy or angry — that
  mood's job chooser. The **idle state's first act for a bedless creature is to
  try for a lair**; the unhappy and angry choosers do the same before rolling;
  and a **sleep need every `TimeAwake`** (EA's own field names from the debug
  dump: `LastSleptTime`, `SleepNeeded`, `LastAttemptedSleep`) is a second way
  into the same attempt.
- **The attempt fails when no reachable lair has a free slot**, and the
  creature then does a job — the happy roll over `HappyJobs`, or the fallbacks
  (train if a training room exists, casino, combat pit, wander to the heart) —
  and retries at its next idle decision. That is the Troll training first.
- **Q151 is alignment.** The room finder keeps the nearest reachable lair with
  a free slot and, preferred, the nearest such lair holding **no bed whose owner
  is of the other side**, read through creature flags 7 and 8. Bit 8 is the
  exact complement of `IsEvil` across all 46 creatures: **`Unk8` is `IsGood`**,
  and `LairPreferenceOnRealDataTests` holds the complement.
- **The job tables are live code.** The three choosers are the runtime of our
  parsed `HappyJobs` / `UnhappyJobs` / `AngryJobs`: cumulative chances,
  `rand(100)`, fall through in list order, a five-second re-roll gate. Job ids
  match the record — **1 Sleep, 2 Eat, 3 Research, 4 Train, 5 Manufacture** …

### What was wrong, and the shape of it

The first reading saw the lair search begin as the mood turned Unhappy and
called the mood the trigger. Both halves were real — the unhappy chooser *does*
try the lair first — and the inference was still wrong, because the creature's
earlier attempt had failed for a reason the camera could not see. **A
coincidence between a visible state change and a behaviour is not the trigger
until the behaviour is shown to be absent without it.** The owner's four other
creature types were that absence.

### Where the code stands

`CreatureSleepSystem` still claims a bed on the first tick one is free, from
anywhere — unchanged, and its header now says the whole replacement is read and
awaits building rather than that half of it is unknown.

### Where the next session should start

1. **Build the lair rework.** Every line is read: an idle bedless creature
   attempts a lair first; the room is the nearest reachable lair with a free
   slot, preferring one with no beds of the other alignment; the tile is found
   on arrival (own tile → fixed neighbour table → a walk of the room); a failed
   attempt falls through to a job and retries at the next idle decision; a
   sleep need every `TimeAwake` is the second way in. Mark as ours: one bed per
   tile (Q156), the idle re-decision cadence, the neighbour table's contents.
   It is a new creature state and deserves its own sitting.
2. **Q132**, the treasury deposit order, ~~**Q153**~~ (closed below), **Q155** — as before.

### Owner questions after this sitting

**Prediction to break:** with a large, empty, reachable lair built before the
portal delivers anything, *every* creature — Troll and Bile Demon included —
beds down before its first job. If one ever trains first, Q156 is where the
difference lives. · **Q152** regeneration in combat · **Q87** pool limit or
quota.

## Twenty-second sitting, coda: the ten-minute dump, and why the ten minutes were needed

Q153 asked which terrain field the imp's reinforce and fix predicates compare
tile health against. The dump of `Terrain.kwd` at the predicted file offset
`0x1c2` came back **zero for every one of the 41 terrains** — not health-shaped,
not anything-shaped — so the prediction was wrong, and the question became *why*.

The answer is that the in-memory terrain record is **not the file record with a
constant shift**. A loader reads the 552-byte record at exactly the offsets our
parser uses and copies each field into its own runtime slot, and the slot the
predicates read (`+0x1ad`) is filled from file `0x1ff`: **`MaxHealth`**. The
flags word and `GoldValue` had sat at a constant −0x15 by coincidence, and one
coincidence was taken as a rule.

So: **reinforce (kind 6) takes solid rock below half its `MaxHealth`; fix (kind
16) takes anything not at `MaxHealth`**; the two share a step of the ring scan,
and a wall part-way up is a *fix*. Our `Reinforce` predicate is by type and
selects the same tiles the two do between them — **no code change**, one
comment. The runtime ↔ file table is now in `kwd-terrain-rooms.md` so the next
binary read does not repeat the arithmetic.

Docs only; tests unchanged at 811.

## Twenty-second sitting, second coda: a plan from elsewhere, checked against the binary

The owner brought an implementation plan for the lair rework written by another
tool from `lair-acquisition.md`, and asked for it to be verified. It was checked
point by point against the binary, and the three "unknowns" it listed were
taken to Ghidra. Two of the three fell, and a fourth thing fell with them.

- **Q156 closed: one bed per tile.** The lair's capacity is the room's tile
  counter, copied straight in; the free-slot test is `used + 1 ≤ tiles`.
- **The neighbour table is read**: underfoot, then (−1,−1), (−1,0), (−1,+1),
  (0,+1), (+1,+1), (+1,0), (+1,−1), (0,−1), then a chained walk of the room
  from a **random** starting tile.
- **Q104 closed on the way: the gate is `TimeSleep ≠ 0` as well as
  `LairObjectId ≠ 0`.** The Dwarf and Stone Knight have `TimeSleep` 0 and never
  go for a bed; our `NeedsBed` tests only the bed id and must gain the second
  half in the rework.
- **One correction to the note**: the random offset is on the *walk target*
  (about a third of a tile), not the bed — the bed is created at the exact tile
  centre with a random facing. Also new: a creature that finds a tile while
  owning a bed in another room destroys the old bed (moves house), and after
  making its bed it sleeps only if health is below variable 88
  (`CreatureSleepsWhenBelowPercentHealth`, 50).
- **The plan added one clause the binary lacks** — a "valid/functional"
  test on the candidate lair. The finder has no such test. Everything else in
  it matched. Q157 opened: which tile of the room the travel step aims at.

Docs only; no code changed; 811 tests.

### Where the next session should start

~~**Build the lair rework**~~ — built the same day; see the next section.

## Twenty-third sitting: the lair rework, built

**ADR-0060.** A bed is now walked to. `CreatureSleepSystem` lost its remote
claim and gained a state, `SeekingLair`: an idle bedless creature picks the
nearest reachable lair with a free slot — preferring one with no bed of the
other side — walks to the room's centre tile, and on arrival tests the tile
underfoot, then the eight neighbours in the original's order, then walks the
room from a random start. Nothing is reserved. A room found full on arrival
sends it back through the finder, a failed attempt drops it to idle and is
retried every 12 ticks, a destination sold mid-walk ends the attempt cleanly.
The bed goes down at the tile centre and the creature sleeps at once only if
its health is under 50%. `NeedsBed` now tests `TimeSleep` as well as the bed id,
so the Dwarf and the Stone Knight are never sent for one (Q104).

**Ours, marked in code and in the ADR:** the 12-tick retry cadence, the
arrival tile (room centre — Q157), the order of the room walk (ascending tile
index; the random start is the original's), and no sub-tile jitter on the walk
target.

**What moved.** No new field, so `HashVersion`/`FormatVersion` stay at 20 and
the determinism pin did not move. Tests that painted a lair behind rock and
expected a bed now paint it a walk away and run the mover. The anger scenario's
third goblin arrives at a taken tile, walks the room once — one random draw —
and so its angry choice on seed 5 flipped from `Rebel` to `Leave`, which the
portal-less map blocks; the scenario notes say so. `LairAcquisitionTests`
holds the eighteen cases the verified plan asked for. 829 tests; 16 scenarios.

**For the owner to watch, when there is time:** the §5 prediction — with a
large empty reachable lair built before the portal fires, *every* creature
beds down before its first job — and, in a lair with its centre tile taken,
whether the newcomer goes to the tile north-west of it first.

### Where the next session should start

1. **Q132**, the treasury deposit order, **Q155** — as before.
2. **Q157** and the idle cadence are readable in the binary
   (`FUN_00471a70` and the idle state's fallback block), if a sitting wants
   to strike two "ours" marks.

## Twenty-second sitting, third coda: a second tool's audit, checked against the binary

The owner had another tool audit `open-questions.md` against OpenKeeper and the
binary and asked for its findings to be verified. Every claim was re-read against
Ghidra (three headless passes), the shipped level files and OpenKeeper before a
row moved.

- **Q1 and Q132 fell together: a game second is four ticks.** Variable 19's
  global has **two** writers — `balance-variables-in-the-binary.md` said one —
  and the second is the level loader copying the LEVEL record's 16-bit field
  after the trigger id (OpenKeeper's `ticksPerSec`). All 66 level files carry
  **4**, scanned directly. Three uses already read (`seconds × var19`,
  `5 × var19`, `tick % var19 == 0`) agree on the unit. STRONGLY INFERRED.
- **Not adopted in code.** `SimulationTiming.TicksPerSecond` stays 20: Q47's
  five work applications a second cannot be scheduled in four ticks
  (`WorkApplicationPeriodTicks` would be 0). The conflict is written into Q1
  rather than resolved by fiat.
- **Q122 answered: the angry job is re-rolled** — a fresh `rand(100)` per
  allowed selection, a five-game-second gate, fall-through in list order.
  ADR-0053's draw-once is wrong in shape; amended, code change scheduled after
  the lair rework.
- **Q123 narrowed to STRONGLY INFERRED:** one routine applies both in-lair
  modifiers and is reached only from three state handlers once a game second —
  state-gated, not terrain-gated. The three states are not yet named.
- **Q35 deposit half read:** the room is the nearest reachable treasury or heart
  with capacity; the tile is the first free one along the room's chain, not the
  nearest tile. Withdrawal still open.
- **Q83 narrowed:** 340 is a slack subtracted from a level-scaled record field
  in the imp's gold code, not a rate. Still open.
- **Q2, Q12, Q77, Q98 were stale** against research already in the repo;
  rewritten to point at it.
- **Judged not evidence and left alone:** OpenKeeper on Q1 (its `1363 / value`
  transform of `GameTicks`), Q97, Q123, Q127, Q136, Q140 — its systems there
  are stubs, or its own comments call the behaviour remembered.

Docs only; no code changed; the lair rework was live in `src/` throughout.

### Where the next session should start

The lair rework landed as `f785cb6` while this was being read. Next: **re-roll
the angry job on its five-second gate**
(Q122), and **resolve four ticks against five swings** (Q1 against Q47) before
`TicksPerSecond` is flipped — trace the tick counter's incrementer, or time a
Dirt Path claim against a stopwatch.

## Twenty-fourth sitting: the angry job is rolled again, and the lair comes first

The correction the third coda scheduled, built: **ADR-0061**. Q122 said the
angry chooser keeps nothing but a timestamp and rolls afresh every five game
seconds; ADR-0053's draw-once-and-keep was wrong in shape, and its reason for
existing — that re-rolling would make a Goblin desert by luck — turned out to be
a description of DK2 rather than an argument against it.

**Re-read from the binary before building**, not taken from the coda's summary,
and the pass paid for itself four times:

- **The sort is not a sort.** The chooser `qsort`s its cumulative ranges with a
  comparator that returns 0 for less-than and 1 for everything else, never
  negative, over an array that is already ascending. It cannot reorder anything.
  The order is the record's, and the fall-through walks it.
- **The ranges are inclusive at both ends** and `rand(100)` is `% 100`. So a
  Goblin's 50/50 is really **51/49**, and the last entry's upper bound is
  unreachable. Both reproduced, both tested.
- **`Leave` cannot fail at selection.** Job 10's routine sets two states and
  returns success unconditionally — the portal search is the leaving *state's*
  problem. Which is what this project already did, arrived at from Q116.
- **The unhappy chooser stamps the angry chooser's timestamp** on a successful
  lair attempt (`+0x101` where its own is `+0xf9`). A copy-paste in EA's source,
  recorded in `creature-anger.md` for whoever builds unhappy jobs.

**What changed in code.** `CreatureState.AngryChoice` became
`LastAngryChoiceTick`; `CreatureSleepSystem`'s finder moved out into
`LairSeeker`, which both systems now call, because an angry creature tries for a
lair *before* it rolls (§8) and two copies of "which lair, and can I get there"
would be two answers. `HashVersion` and snapshot `FormatVersion` 20 -> 21; the
pin is `0x6AF149EE8E20D98A`, moved by the version alone (the fixture is imps,
whose angry table is empty).

**The one deliberate stop:** an unbuilt job ends the selection where DK2 would
have succeeded, rather than falling through to `Leave`. Otherwise every angry
Goblin would desert, including the half of them DK2 turns into rebels. The
fall-through is still built and still reachable — a 1% table misses on 99 rolls
out of 100 — and there is a test that lands on it.

**Visible in the scenarios**: the goblin with no lair now rolls **16 → Leave** at
t6559 and is blocked for want of a portal, once, in silence thereafter; and the
531st `lair_attempt_failed` in that run is the angry chooser's own, under
`[CreatureAnger]`, one line before the roll.

834 tests, 16 scenarios, clean tree.

### Where the next session should start

1. **Q1 against Q47** — four ticks a game second against five work applications
   a second — is still the blocker under `TicksPerSecond`, and nothing should
   flip it until one of them gives. A stopwatch on a Dirt Path claim would do
   it.
2. **Q35's withdrawal half**: the deposit tile is read (first free along the
   room's chain); taking gold back out is not.
3. **The two flags at the top of the angry chooser** — a per-creature bit and
   definition flag `0x4000` — are unidentified, and the second diverts a
   creature out of choosing entirely.

## Twenty-fourth sitting, coda: the work rate, and a prediction the owner broke

Chasing Q1 against Q47 with the owner timing the original in parallel. Four
measurements came in over one conversation and the last one refuted the middle
two, which is the whole value of the exchange.

**Found in the binary.** Both the claim handler (`FUN_004974c0`) and the
reinforce handler compute `delta x (creature[0x3af] + creature[0x3b3])` in 4096
fixed point. `0x3af` is `0xccc` - **exactly 0.8** - at spawn and `0x3b3` is 0.
DK2 names the concept: `IncreasedWorkRatePercentageFromSlapping` (50),
`...WhenCasinoBigWinOccurs` (35), `...OfPlayersTorturedCreatureTypes` (25),
`IncreasedWorkRateDurationFromSlappingSeconds` (60). So `0x3af` is a base work
rate and `0x3b3` is where a slap lands.

**Which explains Q47's fifth**: `0.8 / 4 ticks a game second` = a fifth of the
tabled delta per application. The number the code uses does not change; it
stopped being a constant fitted to one gold measurement.

**The prediction, and its refutation.** The level-up routine appeared to add a
level-scaled stat to `0x3af`, so a levelled imp should work faster - at the stat
table's level-3 percentages a 13-second reinforce should take 7.5-11 s. The
owner measured a **level-3 imp at ~13.1 s** and then a **freshly created level-1
imp at 13.3-13.4 s**. No level effect, on the one job long enough to show one
unmistakably. An earlier pair of *claim* readings (1.30-1.40 s at level 1 against
1.16-1.20 s at level 2) had looked like a level effect and is now recorded as
unsupported: 0.17 s of hand timing against a 13-second null result.

**What that bought anyway:** Q62's `maxHealth` rule is now confirmed against a
**level-1** imp rather than an imp of unknown level, which is a confound nobody
knew existed until this sitting.

**Q1 is still open and a stopwatch cannot close it.** 5 ticks a second at rate
0.8 and 4 ticks a second at rate 1.0 are the same wall-clock speed. The way in
is the game loop: `FUN_00517490` is a bare field load of the tick counter, and
whoever increments that field settles it.

**Owner tests still outstanding, both discrete and neither needing a timer:**
a **level-2 imp mining** should show 341 gold a swing if the work rate scales
after all (273 says it does not), and a **slapped level-1 imp** should show 409
if the +50% is a percentage of the base and 443 if it is percentage points.

Docs only; no code changed.

## Twenty-fourth sitting, second coda: Efficiency, and a day of being wrong in public

The owner timed and read the original for a whole session while this end read the
binary. It ended with a screenshot that made the whole day redundant and correct
at the same time.

**DK2 prints the answer on its status bar.** *"Imp. Name: Snipe. Job: idle.
**Efficiency: 80%.** Mood: Happy."* Efficiency is the work-rate multiplier -
`creature[0x3af] + creature[0x3b3]`, whose base the constructor sets to `0xccc`
= exactly 0.8. **Q47's "a fifth of the tabled delta", the constant every dig,
claim, mine and reinforce timing in this project runs on, is 80% Efficiency
spread over the four ticks of a game second.** It was never a cadence.

| Efficiency | Health a swing | Gold | Owner saw |
|---|---|---|---|
| 80% base | 60 | 272.7 | 272-273 |
| 90% slapped, or dropped onto marked work | 67.5 -> 68 | 309.1 | 309-310 |
| 100% both | 75 | 340.9 | 341 |

**A slap is +10 points for 60 seconds and does not stack. Dropping an imp beside
a tile you have marked is +10 points too** - and dropping it on unmarked ground
is worth nothing, so it is the job and not the drop. DK2 pays the player 12.5%
for handling imps, and 25% for slapping them as well.

**What this cost, and it is worth writing down.** Three claims were committed
during the day and two were withdrawn within the hour by the owner's own control
tests:

- *"The work rate scales with level"* - built on a level-1/level-2 pair, killed
  by a level-3 imp reading the level-1 number, and finally explained: the
  "levels" were this bonus coming and going.
- *"The 341 run had a second bonus source we have not found"* - it had, and it
  was the hand.
- The one that held: **the base is 0.8 and is not level-scaled**, which a fresh
  imp against a level-3 imp confirmed twice.

**The lesson is the cheap instrument.** A 13-second reinforce timed by hand
resolves about 3%; four consecutive gold decrements off a seam resolve 0.3% and
also proved the health-to-gold proportion and that the running total truncates.
And the status bar resolves it exactly, for free. `observation-checklist.md` now
says so; it previously cited "273 gold a swing" in four documents without one of
them recording where the number appears.

**No code changed.** `TileWorkRules`' `delta / 5` is `delta x 0.8 / 4`, so every
worker that is neither slapped nor hand-placed is already exact. Efficiency
becomes a real `CreatureState` field when the hand and the slap exist, and
ADR-0020 carries the amendment.

### Where the next session should start

1. **Q1 is still the blocker under `TicksPerSecond`** and no stopwatch can reach
   it - 5 ticks a second at 80% and 4 at 100% are the same wall-clock speed. The
   way in is the game loop: `FUN_00517490` is a bare field load of the tick
   counter, and whoever increments that field settles it.
2. **Carrying** is still the largest unbuilt blocker, and the hand is what
   Efficiency now waits on.

## Twenty-fifth sitting: the Hand of Evil, and DK2 telling us its own field names

The keeper can now pick a creature up, put it down and slap it, and Efficiency
stopped being a constant and became a number a keeper can move (ADR-0062,
`docs/research/hand-of-evil.md`).

**The find that made the day cheap.** DK2 ships its debug dumps, and they are
`printf` calls with the field name in the format string and the field itself as
the argument. Reading the creature dump gives **a name for 162 creature fields,
keyed by offset, from the developers**: `WorkModifier` and `WorkEnhancement` for
the two halves of Efficiency, `WorkFasterTimer` for the slap's clock, `InHand`,
`PickupTick`, `LastSlapTick`, `ImpJobData.JustDropped`. Yesterday's session
inferred those from arithmetic; today they had names. **Whoever needs a creature
field next should read `FUN_00490240` before searching for anything.**

**What went in:**

- Three commands and a `HandSystem` that runs before every system that makes a
  creature act. The hand is an array on the keeper, 64 things,
  **last in first out** - the original's drop handler reads back the slot it is
  about to free, and the drop takes a position and no id because the hand
  chooses.
- **Efficiency in whole percents**, not DK2's 4096ths, because its constants are
  approximations and the percentages are not: `0xccc + 0x199` gives 67.47 health
  a swing at 90%, which rounds to 67 by every rule and would have read 304 gold.
  The owner read 309, which is 68. `EfficiencyRules.Scale` returns its input at
  80%, so 529 existing tests and every scenario hash survived untouched.
- `AngerInHand` is **a rate**: once a game second, from twenty seconds after the
  pick-up. Moving a creature costs nothing; parking one calms it.

**Three defects this mechanic exposed, all of the same shape** - a value nothing
read:

1. **The fixture said an Imp's `SlapDamage` was 0.** It is **100** against 500
   health, so five slaps kill an imp. Parsed for weeks, read by nothing, wrong.
2. **The fixture's Goblin had neither `CanBePickedUp` nor `CanBeSlapped`**, which
   the real record has. Two hand tests passed the wrong path silently before
   this was found.
3. **A claim built and withdrawn inside the hour.** The first draft read the
   original's `now < LastSlapTick + var19` as a one-second cooldown on the whole
   slap. Its test failed, the handler was re-read, and that window guards **only
   the knock-back impulse**. There is no slap cooldown: a keeper who clicks fast
   angers a creature fast, and Q114's six countable slaps were six because a
   hand is slow.

**And one conflict left open on purpose (Q159).** The slap's work bonus is a
hardcoded 100 ticks = **25 game seconds**, matching `SlapWorkFasterDurationSeconds`
exactly - and contradicting the owner's *"after a minute"* and the
`IncreasedWorkRateDurationFromSlappingSeconds` of 60 that this project wrote down
yesterday. Built as 25. **The test is now one look**: slap an imp, keep it
selected, watch the status bar fall from 90% back to 80%. `observation-checklist.md`
has it, with two more free observations beside it.

**Q158 closed itself.** The definition flag `0x4000` that ADR-0061 left
unidentified is `IsHorny` - the slap handler tests the same bit first and
diverts the same way, and the enum has carried the name at bit 14 since
`creatures_struct.bt` was transcribed. Both halves were in the repository and
nothing had put them together.

### Where the next session should start

1. **Carrying**, which the owner has already asked for and which is what the
   hand is really for: gold, chickens, corpses to a graveyard, and an
   unconscious creature to a lair or a prison. Four half-built mechanics name
   it, and Q161 (the hand refusing a collapsed creature) goes away with it.
2. **Q1 is still the blocker under `TicksPerSecond`.** Today added a second
   independent confirmation that variable 19 is *ticks per game second* - the
   hardcoded 100 and the named 25 agree only at four - but the wall-clock length
   of a tick is still untouched, and the way in is still whoever increments the
   counter `FUN_00517490` reads.

## Twenty-fifth sitting, second half: carrying, and the debt a false claim was hiding

The owner asked for the hand and then carrying, and both are built. Carrying is
ADR-0063 and `docs/research/creature-death.md` is now a document about something
that works rather than about something that cannot.

**What it completes.** The owner described dying in one breath a week ago (Q99)
and it is three states with two sixty-second timers - collapse, corpse, gone -
with an imp task at each seam: carry the unconscious to a bed, carry a corpse to
a graveyard. Only the first state existed. `CreatureDeathRules.BodyWindowTicks`
had been sitting there uncalled for five sittings on purpose, so the missing half
would be missing visibly. **That worked**, and it is worth noting because it is
cheap: pin the number, leave the caller absent, write down why.

**What was blocking it was a sentence nobody had checked.** ADR-0051,
`creature-death.md` and several code comments all said *"a corpse is an OBJECT,
and OBJECTS is unparsed"*. When somebody read all 145 OBJECTS records, **not one
of them is a corpse**. A corpse is a creature in a state, and what was missing
was an imp task. That is the **fourth** time a claim nothing checked has cost
this project a mechanic, and the pattern is now well enough attested to be worth
saying plainly: *ask what would fail if the rule stopped holding, and then check
that it holds.*

**What went in:**

- `CarryRules`, `CreatureActivity.Dead`, and DK2's own Tier A tail. Its imp job
  table is `14, 8, 9, 1, 3, 13, 12` and the last two are the carrying kinds, so
  **carrying sits below claiming** - an imp buries nobody while there is
  unclaimed ground. That is visible in the new scenario: the corpses are
  claimable at t1219 and the imp does not come for one until t1311.
- **Two clocks in one field.** `DyingTicks` is the rescue window while `Dying`
  and the corpse window while `Dead`, reset at the seam, because they never run
  at once.
- **Both ends of the carry relation are stored**, and a third field for the body
  a worker is walking *towards*, which is a different thing that fails in
  different ways.

**One performance bug caught in review rather than by a test**: the whole-map
search predicate called `CarryRules.TryDestinationFor`, which rebuilds the room
index - so the search was quadratic in the map. The graveyard is found once now.

**Four tests changed their minds.** A creature that leaves a body no longer
vanishes at death, and four assertions said it did. Every one of them was
encoding this project's truncated model rather than the game, which is what a
half-built mechanic does to a suite: it makes the gap look like a rule.

### Where the next session should start

1. **The prison and capture** - the other branch of kind 13 (Q163), and the
   largest thing carrying still cannot do. It brings torture, conversion and
   jail breaks with it, so it is a milestone rather than a commit.
2. **Kind 8**, carry a loose object to its room. The third carrying task, and
   the one that needs object instances that have somewhere to go - which is also
   what the Hatchery's chickens need.
3. **Q1 is still the blocker under `TicksPerSecond`.** Unmoved by either half of
   this sitting, and the way in is still whoever increments the counter
   `FUN_00517490` reads.
4. **Q159 is one look at the status bar** and the owner can settle it in a
   minute: slap an imp and watch Efficiency fall from 90% back to 80%. This
   project has built 25 seconds; the owner's stopwatch said 60.

## Twenty-sixth sitting: the prison, the last carrying task, and a merge

Four things were asked for and four went in: the prison and capture, kind 8, an
attempt at Q1, and Codex's KMF converter merged.

**The owner corrected the Hand of Evil, and half the correction contradicted
this project.** The line is: *"only an imp can drag an enemy creature to prison,
a hand of evil can then pick up that creature from prison and drop it around the
dungeon, back in prison, or onto a torture rack"* - and, before it, *"a corpse
can not be picked up by hand of evil ... also an injured creature, lying on the
floor bleeding out, can't be picked up by hand of evil"*.

**The hand never touches a body.** ADR-0062 had recorded the opposite, and it is
worth being precise about how: it read the pick-up handler, wrote the refusal
down as *"two creature states and one state flag, none of which this project has
yet"* - **without naming the flag** - and then concluded from that unread flag
that DK2 permits what it in fact refuses. Bit `0x200000` is bit 21 of the first
state-flag word and DK2's own creature dump calls bit 21 `CreatureDying`. The
rule was in the binary the whole time, one lookup away, and instead this project
wrote down a narrowing "of ours" to excuse a mechanic it had not built.

**Fourth instance, and the first where the unchecked claim was a piece of
reverse engineering rather than a rule.** The lesson generalises: *a partial
reading is a claim too.* "Three of these four tests name things we do not have"
should have been a task, not a conclusion.

### What went in

- **ADR-0064, the prison.** `PrisonRules`, `PrisonSystem`, and DK2's own
  `PrisonOwner` field (creature offset `0x3cb`) as a *second* owner rather than
  a change of owner - which is what makes the hand's one ownership exception a
  field test. A prisoner withers at -3 a game second and rises as a **Skeleton
  for the jailer**; selling the prison, digging it away and losing it are one
  rule, asked once a second from where the prisoner is standing.
- **ADR-0065, kind 8** - carry a loose object to its room, the last of the three
  carrying tasks. **Second in DK2's Tier A table**, above marked excavation and
  above claiming, which nobody would guess. It needed no new record: the
  room->object list has been parsed since Q120 and read by nothing.
- **Codex's `codex/kmf-converter` merged.** Reviewed for the two things that
  matter - no proprietary bytes, and it merges clean - and both held. Its
  output is confined to the gitignored `converted-assets/` by the tool itself.

**A latent bug from ADR-0063 fell out of kind 8**: an imp lifted by the keeper
while dragging a body kept hold of it, and since a held creature is skipped by
every system, the body stopped following and stopped being anybody's problem. A
worker now puts its whole load down where it stood.

**DK2's room debug dump is the find of this sitting**, the way the creature dump
was the find of the last. `FUN_004e70a0` names every room field and then
switches on the type: `TotalCapacity`, `UsedCreatureCapacity`,
`UsedDeadBodyCapacity`, `CreatureList`, `DeadBodyList` on every room, and for
the prison `SafeCreatureCapacity` (the manual's door bar), `PrisonJailBroken`,
`DoorTileX`/`DoorTileY`. **It also answers Q165 in passing**: the Graveyard
carries a `DecomposeValue` tally as a stored field, which this project had
recorded as unknown and had built an event to feed.

### Q1: narrowed, not closed

The global every "now" reads is **`0x006e5054`**, and it has **exactly two
writers in the whole executable** - a constructor and a teardown - so it is a
pointer to the game object and never the counter. The counter is
`*(*(game + 4) + 4)`.

**And it is not incremented.** There are thirteen `INC`/`ADD` writes to any
`[reg + 4]` in the binary; four are dword-sized and none of the four is the game
loop. So the tick is advanced by a *store*, which is what a loop computing
elapsed time from a wall clock looks like rather than one counting frames.

**Where the next attempt should go:** the sub-object at `game + 4`. Find its
constructor and its update - not the counter's writers.

### Where the next session should start

1. **Torture and conversion**, which the prison is the door to. The creature
   record's `TortureHpChange`, `TortureMoodChange` and `TortureTimeToConvert`
   have been parsed and unread for months, and the owner already named the
   third destination for a lifted prisoner: *"onto a torture rack in the torture
   chamber"*. The room itself keeps almost nothing - DK2's room dump gives the
   Torture Chamber two effect counters and nothing else - so the state lives on
   the creature, where its dump has `TorturedDuration` and `TortureVoluntary`
   waiting.
2. **The Hatchery's clock (Q132)**, which is now worth more than it was. It is
   the only thing in the project that would put a loose object on the floor, so
   it is what gives kind 8 something to carry *and* what lets a keeper feed a
   prisoner, which is the whole counterplay to the withering.
3. **Q1's sub-object**, as above.
4. **Three owner questions, all cheap.** Q166: build a 3x3 prison, capture until
   the imps stop, count them. Q171: count the petals on a healthy creature.
   And **Q159 is still unanswered from last sitting** - slap an imp and time
   Efficiency falling from 90% back to 80%; this project built 25 seconds and
   the owner's stopwatch said 60.

### Postscript, the same evening: Q167 answered before it was asked

The checklist went up and the owner answered its third entry within the hour,
unprompted:

> *"when it gets dragged to prison, its flower gets two petals, and then that
> creature starts loosing health at some standard rate, probably from hunger. If
> I drop a chicken in a prison with creatures in they rush toward that chicken,
> the first one to it gets it, eats it, and its health replenishes by some
> amount, I think it gets 2 more petals."*

**This project had built full health**, on nothing better than the shape of the
mechanic, and the ADR said so. Two petals is
`CreatureCriticalHealthPercentageOfMax` = 20, so a Goblin arrives at 300 of 1500
and withers to a Skeleton in **100** game seconds rather than 500 - a prison you
have to answer rather than one you can forget. `CriticalHealth` is a new caller
of a variable nothing read.

**Two more questions fell out of one sentence, neither of them asked:**

- **Q108 answered.** Petals empty with health and fill when a creature eats, so
  the flower is a **health** readout and not the anger meter. Q106 stays a
  stopwatch question.
- **Q172 opened.** A prisoner *walks about its cell*, prisoners *race* for a
  dropped chicken, and *eating* is the counterplay the withering exists for.
  None of it is built - a chicken is an object nothing can pick up - so this
  project's prisoners stand perfectly still, which is now a recorded narrowing
  rather than a rule.

**The methodological point, since this is the third time:** the guess was
flagged, the experiment was named, and the number was still wrong by a factor of
five for an afternoon. **Flagging a guess is not the same as not making one.**
When a mechanic's whole feel depends on an invented constant, the honest move is
to ask before building, not to build and annotate - the owner answers in
minutes.

It also moves the Hatchery up the queue. It was already the only thing that
would put a loose object on the floor for kind 8; it is now also the only route
to the prison's counterplay, and Q172 cannot be built without it.

## Twenty-seventh sitting: two corrections of our own work, and a room that lays

The owner was away and asked for whatever could be done without them. What got
done was mostly **finding this project wrong**, twice, in ways that had each
been sitting there for a while.

### The variable map was wrong by a byte, and the byte mattered

The owner corrected yesterday's note first: *"probably from hunger"* had been
their speculation, not an observation, and **the decompiled source is the top
authority**. Asking the binary meant computing a global address from
`balance-variables-in-the-binary.md`'s rule that the per-variable globals are
*"four bytes apart"*.

They are not. The dispatch stubs store `AL`, `AX` or `EAX` and the globals are
**packed** — measured across all 181 cached slots the stride is 1 byte 42 times,
2 bytes 54 times, 4 bytes 72 times. The rule had been generalised from three
consecutive `u32` variables.

**What that would have cost.** `base + (id - 18) * 4` puts
`PrisonModifyCreatureHealthPerSecond` one byte low, and **a reference search on
the wrong address comes back empty**. Empty reads exactly like *"nothing in the
game uses this variable"* — a confident, publishable, wrong finding about the
original, which would then have been used to tear out a working rule. It was
caught only because the stub disassembly happened to show the wrong operand
widths.

The map is now **read** from the dispatch table at `0x0051D740` rather than
computed, and all 240 rows are in the document so nobody computes one again.

**What it then bought, on the question actually asked:** the variable is read;
the drain is `variable55 * 4` on a `variable19 * 4` tick cadence, so -12 every
four game seconds, averaging exactly the -3 a second the name promises; the
general health routine recognises the drain **by its amount**; and the drain
comes with anger this project does not apply. **What was not established** is
that the routine is the *prison's* — no room-type test is visible in the part
read — so nothing was built on it (Q173). Two prison-adjacent variables turned
out to be cached and read nowhere, and one of them is the 20% behind yesterday's
two-petals number.

### The hatchery had been unblocked for a day

`HatcheryRules` knew the whole mechanism four sittings ago and could run none of
it, on a reason written into the class, a research document and the harness's
caveat list: the laying interval is multiplied by a variable *"no shipped file
supplies"* (Q132). **Q132 was answered on 2026-09-05.** Nobody re-read the notes
that named it.

That is a new variant of the standing failure: not a claim that was wrong, but
one that was **true when written and quietly expired**. Asking "what would fail
if this stopped being true" does not help — it already had. The fix is
mechanical and is now the sixth review clause: **when a question is answered,
grep for everything that cites it as a blocker, the same day.**

**And the first draft of that paragraph was itself wrong**, which is the best
argument for the clause there could be: it claimed the grep found three stale
hits, written from memory of the three the build had touched. Actually run, it
found **nine**. Guessing the output of the command you are recommending is the
same error one level up.

Built: `LayIntervalSeconds` and `HatcherySystem` (ADR-0066). Two things worth
carrying forward:

- **A fixed-point table rather than `Math.Pow`.** The interval decides when an
  egg appears and eggs are hashed; a 3x3 computes **7.99977** and IEEE-754 `pow`
  is not bit-identical across runtimes, so drift in the eighth decimal place
  would hand one machine 8 and another 7 and diverge the run. A test regenerates
  the table from `Math.Pow` and compares. **Any constant derived from a
  transcendental and then truncated is this hazard** and this is the first one.
- **`hatchery.md`'s own table was wrong** where its own header said "truncated":
  `pow(9, e)` is 2.0000566, a hair *above* two, so a 3x3 lays every **7**
  seconds and not 8. The exponent was typed rather than derived and typed just
  high enough to miss — one second off the most common hatchery in the game.

### Also

**Codex's `codex/kmf-animation` merged** — every frame of an animated KMF now
becomes a glTF morph track, with the 30fps output rate named as an assumption.
One fix on merge: the attribution notice covered only the *frame-zero*
`ITAB`/`VGEO` lookup and the branch goes past frame zero, so the notice now
names the per-128-frame indexing. Codex was given `codex/sdt-audio` next — the
sound banks, the last unread asset family.

### Where the next session should start

1. **Eating**, and everything now points at it. The hatchery makes birds, kind 8
   can carry them, hunger exists and does nothing, and it is the counterplay the
   prison's withering exists for. **The owner has already answered the hard
   part**: *"they rush toward that chicken, the first one to it gets it"* — a
   race, no reservation — and the original's own search is `FUN_00473550`,
   nearest food object by straight-line distance within the room. It is a
   creature-side mechanic and it is the last thing between this project and a
   creature that can look after itself.
2. **Q173** — force a function around `0x00479f4a` (the region that loads
   `FUN_0047b7c0` as an immediate) and read what installs it. That says whether
   the drain routine is the prison's, and therefore whether to apply its anger.
3. **Q1's sub-object** at `game + 4`, unmoved.
4. **Three owner questions, all one glance**: Q159 (time a slap), Q166 (count a
   3x3 prison's capacity), Q171 (count the petals on a healthy creature — the
   prison's arrival health rests entirely on it now that the 20% variable turns
   out to be read by nothing).

## Twenty-eighth sitting: a creature eats, and the 239-state table

| | |
|---|---|
| (this commit) | **A goblin walks to the hatchery and eats a chicken** — ADR-0067; Q174–Q176 opened; HashVersion and snapshot 26 → 27 |

**969 tests green**, 21 scenarios, zero warnings, clean tree, quarantine holding.

### What happened

The oldest "cannot" in the project closed. A creature has had a hunger clock
since ADR-0048 and there has never been anything it could do about it — the
reason moved from *"chickens are objects and OBJECTS is unparsed"* to *"the
Hatchery has no clock"* to, since ADR-0066, nothing at all except that no
creature walked anywhere for food.

Read out of `HungerState.cpp`, whose name DK2 ships. The write-up is
`docs/research/eating.md`.

### The find that will keep paying: the creature state table

DK2's creature states are **281 records of 25 bytes at `0x006DA8B0`**, filled at
run time by a long straight run of `MOV`s between `0x0047BBE3` and `0x004805E4`.
(This paragraph originally said 239 records at `0x006DA8C0`; both numbers were
wrong, independently, and `creature-state-table.md` says how.)
No function contains that run, which is why a caller search on any state handler
comes back with a single DATA reference and nothing else — the same shape Q173
hit and could not explain.

Recovering it is two steps and now scripted: list every write into the table's
address range (`TableFill.py`), then walk the installer's linear disassembly
tracking register values, because the handlers are stored from `EBP`/`EDI`/`ECX`
rather than as immediates. **All 281 resolved**, and the `+0x14` word turns out
to be a `Text.str` id, so every state now has the label the game itself shows on
the flower. Record layout is four function pointers at `+0x00`/`+0x04`/`+0x08`/
`+0x0c`, the id at `+0x10`, the label id at `+0x14`, a group tag at `+0x16`, a
category at `+0x17` and a flag byte at `+0x18`.

`FUN_00476d30` is **SetState**, and `SetStateSites.py` lists every call to it
with a constant argument — 329 of them — which is a state-transition graph for
whatever mechanic comes next.

**And `FUN_00479200` is the creature state object's own debug dump**, naming
`HungerNeed`, `LastAttemptedHunger`, `LastAteTime`, `SleepNeeded`,
`CurrentlyDoingJobId`, the whole `ImpJobData` block and the lair and prison
sub-blocks. Third dump function this project has found, after the creature dump
and the room dump. **Look for the dump before reading the offsets.**

### What the mechanic turned out to be

- **The hunger clock accumulates rather than saturates.** `HungerNeed +=
  HungerFill` every period, capped by nothing. Ours *set* it. A creature with
  nowhere to eat owes every helping it missed — observable, and now tested.
- **Two tests, not one**: setting out needs `HungerNeed >= HungerFill`, carrying
  on eating needs only `> 0`. That hysteresis is what makes `HungerFill` mean
  *chickens needed to be full* when each bird pays off one.
- **One bird fills a prisoner** where five fill a free Bile Demon: the prison
  path passes the level-scaled `HungerFill` to the same subtract the hatchery
  path passes a literal 1 to.
- **Nothing is reserved.** The chosen bird's id lives on the creature and nowhere
  else, which is the owner's *"the first one to it gets it"* exactly.
- **Hunger is asked before sleep**, because the needs routine asks it first and
  returns early on success.
- Three fields pinned through the loader rather than by eye: `HungerFill` at
  `def+0x66d`, `Hp` at `+0x692`, `HpFromChicken` at `+0x694` — and the loader's
  own `× V19` and `>> 12` corroborate Q132 for free.

### The stale-claim instance, and it is a new shape

`HatcheryRules` said *"only the Chicken carries `ObjectFlags.TypeFood`"*.
`objects.md` had said the opposite for weeks — the **Torture Pendulum** carries
it too — and had added *"nothing depends on it: a pendulum is not on a hatchery
tile"*.

Both halves matter, and the research note was half wrong in a way worth naming.
The original has **two** food searches with **different predicates**: the
hatchery matches the object id 9, the prison matches the flag. So the prediction
held for the search it had thought about and failed for the one it had not.
**"Nothing depends on this" is a claim about code nobody has read yet**, and the
honest form of it names the search it was checked against.

### An interaction nobody wrote

A hatchery stops laying at one bird per tile. A creature eating takes a bird out.
So a full hatchery **starts laying again** — visible in the new scenario at
t3919, after two goblins ate at t3782 and t3811. First place in this project
where two mechanics feed each other without being told to.

### And then the table paid for itself twice

**Q173 answered** (ADR-0068). `FUN_0047b7c0`, the routine that applies the
prison drain and had **no callers anywhere**, is **category 14's update hook**,
and category 14 is exactly the six states of `PrisonState.cpp`. The "long run of
stores with small integers" that Q173 had recorded was this table being filled
and the integers were category indices. It needs no room-type test because it
only runs on a creature that is in a prison state.

**And the gate on it is `HungerFill`. The prison drain is hunger.** So the four
creatures that never eat never wither — including the **Skeleton**, which is
what a prison *makes*, so the room's own product is immune to the room. The
owner guessed exactly this on 2026-09-05 and then said, explicitly, that it was
speculation and that the decompiled source was the authority. It was right.

**Q176 answered too, from the same table.** The needs routine refuses outright
when the state's category byte is 7, and the sleep states carry 7: **a hungry
creature does not get up to eat.** It went onto the observation checklist in the
morning and came off it in the afternoon without anybody playing anything.

### Where the next session should start

1. **Prisoners moving** (Q172). Everything else it waited on is gone: the
   chicken exists, an imp can carry one, a free creature eats one, and the
   prison's own eating number is already written down in
   `EatingRules.EatInPrison`. What is missing is a wander and a search, and
   `PrisonState.cpp`'s states are 0x72–0x77 in the table above with their
   handlers already named.
2. **Q174**, and it is the only thing eating left open. Two hypotheses are now
   dead rather than untested: the eating category has **no enter hook**, and a
   state handler that returns 0 simply drops the creature to state 1 rather than
   stalling it. What is left unexplained is `FUN_004ded00`'s discarded
   out-parameter in the hunger attempt — it finds the room and throws it away.
3. ~~**Q173**~~ — answered, see above.
4. **Four owner questions, all one glance**, at the top of
   `observation-checklist.md`: Q159 (time a slap), Q166 (count a 3x3 prison),
   Q171 (count the petals), Q162 (drop an imp two tiles out). Q176 went on that
   list and came off it the same day, answered from the binary.

---

## 2026-09-06, second sitting: five things the owner watched, two of them defects

The whole sitting came out of the owner playing and reporting. **Four
observations, two shipped defects, one withdrawn claim and one correction of a
brief this project wrote.** Nothing on the standing queue was touched; this was
better use of the time.

### What changed

| Commit | What |
|---|---|
| `ac0645b` | **The aimed work bonus was reading the wrong field.** ADR-0069 |
| `e3691bd` | **A 3x3 prison holds two, not nine.** ADR-0070 |
| `8551d82` | **A flower has six petals, not ten** — and the number that rested on ten survives |
| `014effc` | Codex's `.str` string-table reader, merged |
| `78012d1` | Brief 5 corrected: its header reading was wrong and Codex's was right |

### The aimed bonus (ADR-0069)

The owner watched a dropped imp keep 90% all the way to the wall and through the
digging, lose it only when the mark was removed before it arrived, and take the
bonus from **three tiles** rather than one.

All of that is in the binary, and none of it was here. The Efficiency arithmetic
reads `ImpJobData.DroppedTask` — a task **kind**, zero when the drop found
nothing. `JustDropped`, which this project was reading, is four bytes earlier and
is a one-shot ticket the next evaluation spends. **The state object is pinned at
`creature + 0x24E`** by the creature dump's own `LEA`, so this was a plausible
name four bytes from the right one with nothing checking it.

Three consequences, all built:

- the drop buys a **look**, not a bonus, and the look has **its own priority
  order** — digging first, and claim-the-just-dug-tile absent entirely;
- **three tiles, measured to the tile the work is on.** DK2's scan is nominally
  *two* and reaches three because its ring loop tests the bound after scanning a
  ring, with a strict greater-than, from a counter starting at 1. Reproduced, not
  fixed;
- **`DroppedTask` has exactly two writers in the whole executable.** The clear
  runs only when a worker still on that kind of work can find no more of it
  anywhere — so the bonus is **not worth one job**, which is what this
  project built the day before.

**Q159 closed with no code change** (25 s was right, against the owner's own
earlier stopwatch). **Q162 closed as a defect.** **Q177 opened**: the one glance
that would separate "lasts the kind of work" from "lasts one job".

### The prison (ADR-0070)

`Captives held: 0 / 2` on a 3x3, off the game's status bar. This project held
one per tile, which is nine.

DK2's room-capacity routine has a case per room type. The prison's walks its own
tiles, counts the ones a general *"nothing is standing here"* predicate accepts,
and **adds one when that count is non-zero**. The bars and the gate take the rim,
so a 3x3 has one tile left and a capacity of two — and below 3x3 the count
is zero, the "+1" never fires, and the room does nothing, which is the owner's
*other* sentence arriving from the same arithmetic rather than being bolted on.

`TotalCapacity` is left at **-1** for a prison, which is why manual overcrowding
is possible at all — a rule this project already had, now a consequence.

**Opens Q179**: the Prison's `RecommendedSize` is 5x5 and a 3x3 one works, which
is the first room to contradict Q45's reading of that field.

### Three things worth carrying

**1. The state table is at `0x006DA8B0` and this project had it one record
late.** Record N is `0x006DA8B0 + 25N` with the **update handler at +0**, the
state id at +0x10 and the category bytes at +0x16. The earlier anchoring at
`0x006DA8C0` put the id at +0, so every handler it named was off by one state.
Pin it from the dispatcher — the creature update calls
`(*(code**)(0x006da8b0 + stateId * 0x19))()` and SetState indexes
`(&DAT_006da8c6)[stateId * 0x19]` — never from literal stores. **Flame had
this right and this project had it wrong**, which resolves the conflict recorded
on 2026-09-06 in Flame's favour and is worth remembering when weighing it.

**And the count was wrong too, for a different reason.** The table has **281**
records, not 239 — Flame's number again. 239 was what fitted inside a scan
window that stopped at `0x006DC000` while the table runs to `0x006DC420`. Fixing
the frame did not touch it, which is why it survived a sitting that had already
caught the other error. Re-derived and settled 2026-09-06:
`docs/research/creature-state-table.md`.

**2. The repo contradicted itself for five days and nothing could fail.**
`WorldPathGrid`'s comment said imps wade across water, mine an isolated gem and
carry the gold home. `imp-tasks.md` said, of the same experiment, that none of
the gold ever reached a treasury. The owner has now withdrawn the second. No test
could have caught it, because neither claim was executable. **When the same
experiment is written down twice, one of them is already drifting.**

**3. This project wrote a brief containing a wrong reading, and the agent
following it checked.** Brief 5 said the `.str` header's `+4` is a base string
id, from three files. It is a **file id**: the 92 run 1000, 1001, 1002 … 1091,
and as bases the briefing tables would overlap six ids at a time. Reading all 92
took the same minute as reading three.

### What the owner is holding

Three one-glance questions, top of `observation-checklist.md`:

1. **Q177** — mark two blocks far apart, drop an imp on one, watch
   Efficiency after the first falls.
2. **Q167** — and it stopped being a counting problem: the status bar prints
   `Health: NN%`, so read a fresh prisoner's arrival health as a number.
3. **Q178** — time a chicken nobody eats.

**Q178 is also a missing mechanic**: an uneaten chicken dies and is replaced, so
a hatchery churns where this one fills up and stops. Every bird has an age and
`HatcherySystem` does not know it.

### The queue, unchanged

1. **Prisoners moving (Q172)** — still the biggest thing blocked on one
   missing piece, and now more so: a prison holds two rather than nine, so the
   room is small enough that a wander and a search are cheap.
2. **Q174** — how the original walks a hungry creature to the hatchery.
3. **Training and payday** — the two M4 items with nothing blocking them.

### Later the same day: two more, and one of them undoes a number this project trusted

**`97e225c` — a knocked-out creature has exactly one sixth of its health**
(ADR-0071, Q167). The owner answered Q167 by counting petals again — *"a fresh
prisoner has 2 petals"* — which cannot settle it, because two of six is a band
from 16.7% to 33.3%. So it went to the binary. DK2's `ModifyHealth`, on taking a
creature below zero when it is not already dying, writes `maxHealth / 6` and
raises the `CreatureDying` flag; it computes the sixth as `100 * max / 6 / 100`,
and a flower has six petals. Imprisonment touches no health at all — the prison's
category-enter hook sets the drain countdown and nothing else.

**That replaced a 20% this project had invented twice over.** It came from
`CreatureCriticalHealthPercentageOfMax` read as *"two petals of ten"* — and the
flower has six petals, and **a raw scan of `.text` finds exactly one reference to
that variable's cached global: the loader writing it.** Nothing in DK2 reads it.
Two independent errors that cancelled into something plausible, looking confirmed
from both sides and resting on nothing. A Goblin's prison clock has now been 500
seconds, then 100, and is 83.

**One loose end, and it is a display question.** A sixth is exactly *one* petal
of six and the owner counts two. Either the flower is not a plain proportion, or
something adds a little between the knockout and the cell. It does not move the
health number, which is read. When something eventually draws a flower, that is
where the question belongs.

**Q178 withdrawn within the hour of being recorded.** The owner reported that an
uneaten chicken dies and is replaced; on looking again, *"the flying feathers"*
are the **coop laying**, not a bird dying — the two animations share them — and a
chicken followed for four minutes did not die. Nothing had been built on it, and
what was described is what `HatcherySystem` already does (ADR-0066). **A
retraction that arrives before the code does is worth as much as an answer**, and
it is the reason the observation checklist exists in the shape it does.

### One thing left open that a future sitting should not assume away

**The state table's record COUNT is not settled.** The base is: `0x006DA8B0`,
25-byte records, handler at `+0`, pinned from the dispatcher
(`(*(code**)(0x006DA8B0 + stateId * 0x19))()`). Flame says 281 records; this
project's fill-run count said **239**, made under the shifted frame and never
re-derived. Do not quote 239 without checking it.

**And the method lesson is worth more than the fix.** The old frame was 16 bytes
early, which on a fixed-stride table is invisible: every field still lands at a
constant offset, every record still parses, the ids still count 0, 1, 2. The id,
word, category and flag offsets were all *right*; only the four function pointers
were read from past the record's end, which at a 25-byte stride is the **next
record's**. So every handler was filed one state early, and nothing could catch
it. **Pin a table's frame from the code that indexes it, never from the data that
fills it.**

---

# Sitting of 2026-09-06 (later): the state table settled, Brief 6 merged, prisoners walk

Five commits: `5cdd08e`, `6be8a85`, `73fb2e8`, `b0c0af1`, `0549079`.
**1020 tests, 21 scenarios, 73 ADRs, zero warnings, hash unchanged.**

## 1. The state table has 281 records, and every state has a name (ADR-0072)

**The previous sitting's open item is closed, and 239 was wrong for a reason
nobody had guessed.** It was not the frame shift. It was a **hand-chosen scan
window** `[0x006DA000, 0x006DC000)`; the table ends at `0x006DC420`. Record 238
starts inside the window, record 239 at `0x006DC017` — outside. The sitting then
recorded *"all 239 resolve, none unknown"*, which was true and worthless: every
record the scan **saw** resolved, because resolving is what the scan did.

**Two independent errors in one artefact**, and fixing the frame did not touch
the count — which is exactly why it survived a sitting that had already caught
the other one.

Settled five ways, no two sharing a failure mode: the reference histogram lands
on exactly nine offsets mod 25 with no strays; 2,529 writes = **281 × 9**, every
field written once; all nine fields span records 0–280 with no gaps; 277 records
write their id as a literal and each equals its index; and all 281 labels resolve
inside `Text.str`.

**The `u16` at `+0x14` is a `Text.str` string id** — the activity line the game
prints on the flower. All 281 resolve into 70 labels. Brief 5 posed this as a
long shot and it paid.

Also corrected: `+0x16` is a **group tag** (equality only), `+0x17` is the
**category** (0–31, indexes the 32-entry table at `0x00699688`) — they had been
recorded as two of a kind. And **Q174 was answered by the same off-by-one**: the
original *does* walk a hungry creature to the hatchery, in state `0x5B` resuming
in `0x5C`. The old frame filed that handler under `0x5A`, which is genuinely
near-unreferenced, so a working walk looked orphaned.

Everything is in `docs/research/creature-state-table.md`, all 281 rows.

**Method rule this leaves:** get a table's **extent** from something other than
the scan. Three cheap independent checks were available and none had been used —
where the next referenced global starts, an exact `writes == records × fields`
identity, and a field that must be valid somewhere else.

## 2. Brief 6 merged after an independent re-derivation

`Doors.kwd` (8 × 616) and `Traps.kwd` (16 × 579) are typed definitions loaded
into `GameDatabase`. Reviewed by re-deriving every counted claim rather than
reading the branch's tests back to itself: an independent parser reproduced every
value, **the manual was decompressed and searched directly** (all 17 printed
prices match, zero mismatches), and OpenKeeper's reader settled the 516→579
correction — it calls `checkOffset` against the header's item size, so the number
*this project* had published was the wrong one.

**One real trap found and fixed** (`b0c0af1`): `SHOTS` carries its **own 1-based
id at `+0xBE`** — the only offset whose value is row+1 for all 65 rows — so a
trap's `ShotTypeId` is a record id, not a row index. Index by row and Gas Trap
fires *Lightning Trap Shot*: plausible, wrong, and nothing would have failed.
Now asserted by a test, because SHOTS has no parser until M6.

Also: the data README still said 239/63 labels, and `THIRD-PARTY-NOTICES.md` was
missing the attribution the branch owed for OpenKeeper's field order.

## 3. Prisoners walk (ADR-0073) — Q172's first third

DK2 gives a prisoner a three-state loop, all category 14: **114** arrives,
**115** decides, **116** walks and pauses. 115 picks the step through
`Room.cpp:1717`, which draws `random(4)` as a starting index and scans
`(start + i) % 4` forwards through the **four orthogonal** offsets at
`0x006BC350` — north, east, south, west — taking the first candidate that is a
tile of the **same room instance**. 116 rolls `random(100) < 20` each tick to go
back to 115. **None of this was readable before ADR-0072.**

`Room.cpp:1717` had to be read from **instructions**, not decompilation — Ghidra
types the random result as a struct pointer.

Two things shaped the code. **The room-instance test is the containment**, so
`CreatureMovementSystem`'s refusal to walk a prisoner is *deleted* rather than
narrowed and there is no "prisoners may not leave" rule anywhere. And **the table
order is behaviour** — it decides which way a prisoner goes when its drawn
direction is a wall.

**`MotionlessInPrisonSeconds` = 8 is dead**, and this project had built a guess
on it. A raw `.text` scan of `0x006F25E9` returns one hit, `MOV [...],EAX`, the
setter stub. Third dead balance variable found here, second to have had a
mechanic hung on it. **A named variable in the balance table is not evidence the
engine consults it** — scan the global and count the hits first.

## 4. What to pick up next

1. **Q187 — the bird.** The rest of Q172. Nothing drops a chicken into a cell and
   nothing races for it. It used to wait on movement and no longer does; every
   other piece exists, including `EatingRules.EatInPrison` (one bird pays off the
   whole of `HungerFill`) and the original's search, state **117**'s
   `FUN_00473550` — walks the prison's slabs for the nearest object with flag
   `0x80`, applies `AngerNoFood` when there is none.
2. **Training and payday**, the two M4 items with nothing blocking them.
3. **Q184/Q186** — the three unread function pointers in every state record, and
   the 32 category records at `0x00699688` that `+0x17` indexes. Transition
   legality lives there, and nothing currently tests it.

## 5. The coordination item, now resolved

A **second Claude session** was working in this repo throughout — save-game
parsing and Unity rendering. **It has since landed, in `6581ebe`.** Every
commit above this one was checked to contain only this sitting's files, and
that still holds.

**The `open-questions.md` question is settled.** That file held both sessions'
edits, and the save session committed it whole as part of `6581ebe` — so this
sitting's Q172, Q174 and **Q184–Q188 are in the repo**, alongside their
Q180–Q183, Q187 and Q189. The worry recorded here earlier — that committing it
would leave a dangling reference to an untracked `sav-format.md` — went away
when that file was committed in the same change. **ADR-0072, ADR-0073 and
`prison.md` no longer cite anything that exists only on disk.**

Two of their findings are worth knowing about here, because they correct
assumptions rather than just adding facts:

- **There is no 189-byte entity record** in a save. Entities are written as
  typed sections whose members each serialise themselves, so records are
  variable-size and per-class.
- **A save's tile record carries the room instance id and the tile's health**,
  both confirmed against sources outside the file. Their lanes 1, 3 and 9 move
  across most of the map between any two saves and are unidentified — which is
  **directly relevant to Brief 7**, since a save series is a cheap way to watch
  fog state change under a known action.

See `docs/research/sav-format.md`.

## 6. Fog of war opened, and it is further along than "not started"

**2026-09-06, this sitting.** Fog had **no research note and no open question**
anywhere in the repo — a sweep for "fog", "sight", "vision" and "visibility"
across `docs/`, `src/` and `unity/` returned five incidental hits and no
mechanic. It now has `docs/research/fog-of-war.md`, **Q190–Q194**, and two
briefs. Nothing is implemented.

**The finding that matters most is a correction that has not been made yet.**
The owner CONFIRMED that a revealed region stays revealed — and `Text.str`
348–353 turn out to read `Fog of War` / `Creatures Reveal` / `All Owned Reveal` /
`Stay Revealed` / `Re-shroud` / `Re-fog`, inside a run of skirmish-setup labels.
So **"stays revealed" is the name of option 351 of five**, and the campaign's
behaviour is probably one mode of an engine that ships several. Writing it down
as *the* rule would have been the characteristic defect again: consistent with
everything available, right about the campaign, wrong about the engine. Q192
carries it.

**Confirmed on the way through:** an unseen tile really is *presented as terrain
27*. Record 27 is `Fog Of War`, flags `Solid, Impenetrable, Taggable`, and its
`NameStringId` 2472 / `TooltipStringId` 2473 resolve to "Fog of War" and "Fog of
War: this area has not yet been explored" — which is exactly the caption in the
owner's screenshot of the running game, with the blue selection quad sitting on a
fog tile in open blackness. DK2 does not hide a tile, it substitutes one. Read
via `Dk2StringTable`, which Brief 5 built for something else.

**A near-miss checked and rejected, recorded so nobody re-runs it.** The owner
estimates the reveal radius at ~6 tiles and `DefaultTorchLightRadiusTiles` is
6144 — but `Fp32.One` is 4096, so that variable is **1.5 tiles**, and
`DefaultTorchLightHeightTiles` carries the same number. There is **no sight
radius anywhere in the 214-variable balance table**, so if it is 6 it is an
immediate in the image. Q191.

### The architectural fact this turned up, which is not about fog

**The presentation boundary has no viewer.** `WorldView.TileAt(world, x, y)`
takes no player, `ScenePresenter.Present(world)` takes no player, and
`TileView.Tagged` is `IsTaggedByAnyone` — every renderer is omniscient by
construction, both of them depend on it, and `ARCHITECTURE.md` lists four
decisions at that boundary without mentioning this one. It is the characteristic
defect sitting in the architecture rather than in a rule.

Fog is not the only customer for a fix: `Text.str` 2471 is *"This room is owned
by another power. Its details are unknown."* — a **second partial-knowledge rule,
on already-revealed ground**. The boundary needs a viewing player whether or not
fog is ever built.

### Where it stands, and the one thing not decided

**Q190 is the stop rule: is fog simulation state or presentation state?** In
`WorldState` it enters `HashInto`, `WorldSnapshot` and the pinned literal and
costs a `HashVersion` bump; beside `WorldView` it costs nothing.

**The save-format session left the cheap way to settle it, without knowing it
had.** Lanes 1, 3 and 9 of the runtime tile move across most of the map between
any two saves — *"consistent with visibility or fog state"*. **If fog is in the
save it is game state, because a save persists the simulation.** Two saves either
side of walking one creature into unexplored ground would answer it in minutes,
using a method already built and validated by a control. Both briefs now say so.

**Brief 7** (read-only research) and **Brief 8** (build it, own worktree and
branch `codex/fog-of-war`) are in `CODEX_TASKS.md`; Brief 8 absorbs Brief 7, so
run one or the other. Brief 8 is shaped around the stop rule — presentation-side
by default, and a finding of "simulation state" is a **stop and report**, not a
licence to bump the hash, since the determinism hash is not Codex's. Its
definition of done leans on one property worth keeping: **an unchanged pinned
hash is the proof that fog stayed out of the simulation.**

The test to want back is two keepers viewing the same world and seeing different
things. Nothing in this project can express that today.

---

# Sitting of 2026-09-06 (afternoon): Q172 closed at both ends, and fog merged

Three commits: `84c5add`, `8c238e2`, `d02d18f` (a merge).
**1042 tests, 23 scenarios, 76 ADRs, zero warnings.**
**`HashVersion` and `FormatVersion` are now 30**, pin `0x2E7E116CA573398B`.

## 1. A prisoner eats, and 117 was never "eat where it stands" (ADR-0074)

The owner's third sentence about a prison — *"they rush toward that chicken, the
first one to reach it eats it"* — needs **no racing mechanism at all**. Nothing
in DK2 reserves the chosen bird, so the loser arrives at an empty tile and looks
again. Absence, not machinery.

Reading the handler also **corrected ADR-0073**, which had recorded state 117 as
*"eat where it stands"* from the transition alone. It is a search of the prison's
own tile chain, a ranking by straight-line squared distance, and a **move order**
resuming in 118 — which is the meal. Opening the function was the whole fix.

Three numbers, none chosen:

- **One bird fills a prisoner.** 118 passes the level-scaled `HungerFill` where
  the hatchery state passes a literal 1. `EatingRules.EatInPrison` had written
  this down two sittings early and was right.
- **The anger is a QUARTER of the tabled figure.** The creature loader divides
  every `angerNo*` field by DK2's four ticks a game second — pinned by the same
  register multiplying `TimeAwake`/`TimeSleep` into ticks two dozen instructions
  earlier — so the engine holds **anger per tick**, and 117 adds one per
  *attempt*. `SimulationTiming.OriginalTicksPerGameSecond` now carries that 4, so
  the figure is a fact about DK2 and not about our placeholder rate.
- **A prisoner does get hungry.** The needs routine suppresses the hunger clock
  on a flag bit whose fifteen states are exactly drinking, gambling and dancing.

**Q185 is therefore half answered and it is the Casino.** Bit 3 of the state
record's `+0x18` suppresses hunger, bit 4 suppresses tiredness, and the witness
set has no strays. First thing anything has been read to do with that byte.

**Two older errors fell out of the same read.** `eating.md` §2.2 was still on the
pre-ADR-0072 frame and every state id in it was one too low. And the needs
routine's first gate is the **group tag**, not the category: group 7 is exactly
the fourteen states that *are* a need, so the rule is *a creature already
attending to a need does not reconsider its needs*. **Q176's answer survives and
its explanation did not.**

## 2. The Hand of Evil holds a thing (ADR-0075)

ADR-0074 left the mechanic complete and **unreachable**: a chicken only reaches a
cell because a keeper puts it there. The owner settled it the same day, with a
screenshot of **four chickens held at once**, and confirmed **LIFO** — which
upgrades an inference to an observation, and it holds *across kinds* because DK2
keeps one array of things.

**The near miss is the finding.** `CanBeDroppedOnAnyLand` is set on exactly one
record — Call To Arms, precisely the object you would expect to be exempt from an
owned-ground rule — and its name fits the owner's account perfectly. **It is
tested nowhere in the executable.** Fourth dead field found here, and the first
that is not a balance variable.

**So the rule widens: before building on ANY named field in the shipped data —
balance variable, object or terrain flag, record column — scan for its reader and
count the hits.** A name that fits *too well* is a reason to check harder, since
it is exactly the case where nobody looks.

Read and deliberately **not** built, both needing a target our drop cannot name:
a chicken dropped into an **enemy Hatchery** becomes theirs, and a chicken or
gold dropped **onto a creature** is earmarked for it — which is the one case
where a bird is not up for grabs.

## 3. Fog of war merged (ADR-0076)

Brief 8's Phase 0 stop rule **fired**, and the owner overrode it. The stop commit
stays in the branch history rather than being smoothed out.

Phase 0's answer was **simulation**, by the exact route Q190 predicted from
another mechanic: DK2's world save walks every tile and writes ten bytes of the
live 16-byte record, **including the byte whose bit 6 gates fog**. Those are the
moving lanes `sav-format.md` had flagged as *"consistent with visibility or fog
state"*.

**Two counted claims re-derived on merge**, with a tool written for the purpose
rather than by reading the branch's tests back to themselves:

- `PerceptionRange` is `0x6000` — six tiles — for **all 46** creatures, and the
  useful half is the contrast: `DistanceCanSee`/`Hear` are `0x4000` on the same
  46. Two genuinely different ranges, and the right one taken.
- **Six** terrains carry `RevealThroughFogOfWar`: Impenetrable Rock, Water, Lava,
  Gold, Gems, Edge of Map. Which is why an unexplored map still shows a keeper
  where the gems are.

**Fog defaults off**, so nothing that existed before behaves differently. Q192's
dangerous reading is ruled out on the way: the configuration value is **boolean**,
so the five setup labels are not one enum behind it.

**ANSWERED LATER THE SAME DAY — see the sitting at the foot of this file.** The
paragraph below was true when written and expired within the hour: a three-save
series from the owner settled it. Persistence proves the state
belongs to the simulation; it does **not** prove any computer keeper reads it,
and no AI reader was found.

Two things changed on merge: the branch report arrived as `README.md` (master had
none, so a branch report would have become the project's front page) and is now
`fog-of-war.md` §8 plus ADR-0076; and the version numbers resolved to **30**.

## 4. Numbers collide across worktrees, twice in one day

**Q187 was taken twice.** "The bird" sat as Q187 in an uncommitted working copy
while the save-format session allocated Q187 from the committed file and landed
first. Renumbered to **Q195** across an ADR, a research doc, a scenario, a test
and a commit message. The hand is **Q196**.

**`HashVersion` 29 was taken twice**, by Codex's fog branch and by the hand work,
in different worktrees. Resolved by the hand going to **30** rather than fighting
for 29, so master goes 28 → 30 across two commits with 29 spent on a branch. The
pin comment says so, in case the gap ever looks like an error.

**Before taking either kind of number: `git worktree list`, then check the
branches.** There are three worktrees, not two — `codex/fog-of-war` and
`claude/keeper-ai` beside master.

## 5. One file left uncommitted on purpose

~~`docs/research/creature-state-table.md` is modified in the working tree and was
**excluded from all three commits**.~~ **Committed as `b9b33f3` on the owner's
instruction once that session had ended**; the tree is clean. Its one counted
claim was verified first — §5.4 leans on *"264 distinct non-null handlers"* and
§4's own table gives 265 distinct values of which one is state 0's null. It was not touched by this session: its
mtime moved mid-sitting while the owner had said no other session was running.
The change is good — it adds a sixth, external check to §5, in which Flame's map
independently encodes 58 of the state ids and agrees with our numbering on every
one — and it respects the Flame boundary (our own words, not one symbol
reproduced). **It is somebody's in-flight work and it is theirs to commit.**

---

# Sitting of 2026-09-06 (evening): the documents reconciled with the tree

No simulation code changed; one Unity doc comment did. Tree at `47ce875`
(another session's fog-research commit landed at 15:55 while this ran;
`claude/keeper-ai` is one commit ahead of master and was not touched).
**1042 tests, 23 scenarios, 76 ADRs**, `HashVersion` 30, solution and the Unity
compile shim both build clean. **Nothing here is committed** — the owner did not
ask for a commit, and the tree is theirs to land.

## 1. What was found

A status recap against `AGENTS.md` turned up eleven places across six documents
where a statement about *this project* had stopped being true and nothing
re-read it. None was a wrong fact about DK2. Every one was a true sentence on
the day it was written — "there is no converter", "creatures have no health",
"rooms cannot be sold", "holds all 76 questions", "five record types parse" —
that the tree then moved past. `GOAL_ALIGNMENT.md` has spent the week
collecting instances of *a claim nothing checks may already be false*; this is
the same pattern turned on the project's description of itself, and the
cheapest kind, because the check is `git log`.

## 2. What was fixed

The rule followed: **where a file is a log, strike the old text and put the
correction beside it; where it is a status page, rewrite the sentence and say
when.** Dated sittings were not touched.

- **`HANDOFF.md`** — header re-dated and re-counted (834→1042 tests, 16→23
  scenarios, three→four M4 items, nine record types, what is absent, the two
  standing caveats). In the 2026-09-04 narrative, five dead sentences struck:
  no experience / pick-up / eating; "there is no converter"; "START HERE: what
  rooms actually do"; "hatchery blocked on objects (M6)"; "creatures currently
  have no health, hunger, mood or experience". Conventions: ADR-0020's "five
  times a second" now carries the Q47/Q1 re-derivation. *Live research
  questions* marked as the 2026-08-29 snapshot it is (76→202 questions), Q1's
  duplicated line and the second Q7 entry removed, Q2/Q7/Q40/Q59 annotated with
  where they went.
- **`DK2_REIMPLEMENTATION_PLAN.md`** — revision-2 counts refreshed (they were
  a morning old and every one had moved); §1.2 and §2.2 say 281 states, not
  239 (ADR-0072); §3 moves moods to Partial/built and splits job selection
  into workers (built) and non-workers (unknown); §8 lists the nine parsed
  record types and the nine unread ones; §8's "the converter does not exist"
  corrected; §11 marked as revision-1 text with a per-item status.
- **`RESEARCH_STATUS.md`** — the *What creatures then do* bullet rewritten as
  one statement (it had been patched three times without its opening clause);
  anger removed from the unknown list; prisoners no longer "cannot move".
- **`GOAL_ALIGNMENT.md`** — the review log gains the eight entries that had
  been appended as prose without a row, and a **re-run twelve-row table dated
  today** beside the 2026-08-29 one, which stands as a dated artefact.
- **`ARCHITECTURE.md`** — rooms are sold (ADR-0029); mined gold reaches a
  treasury; hunger is an accumulator that eating satisfies; five rooms with
  behaviour, not four; a new section for what M4 grew after the file last
  moved (hand, carrying, prison, fog, save reader, converter); and two owed
  things written down for the first time here (below).
- **`unity/README.md`, `DungeonScene.cs`** — the converter exists; the adapter
  has no branch that loads its output. **`CODEX_TASKS.md`** — 21→23 scenarios.

## 3. Two gaps that no document named, now named

1. **The install-backed tests pass vacuously in CI.** Every real-data test
   returns early when `Dk2Install.Find()` is null, `ci.yml` sets
   `DK2_DIR: /nonexistent` on purpose, and a returned test is a green test.
   So "1042 green on CI" overstates what CI checked by well over a hundred
   tests (the Behavior, Convert and Data suites that need shipped data). The
   fix is a skip that shows in runner output, not a silent return. Not done
   here — it is a test-infrastructure change, not a documentation one.
2. **The presentation view has no viewer.** `WorldView.TileAt(world, x, y)`
   takes no player and `TileView.Tagged` is any-player, so every renderer is
   omniscient while fog is simulation state (ADR-0076). The handoff had said
   this; `ARCHITECTURE.md` had not. Now it does.

## 4. What was deliberately left alone

`EXPERIMENTS.md` is accurate (no oracle runs; Q1 cannot be measured).
`DECISIONS.md` is a record and was not edited. The 2026-08-29 goal table
stands where it is. The Q1/Q47 conflict is recorded, not smoothed:
`TicksPerSecond` is still 20 and every timing figure in every report is in
that unit.

## 5. The rule this adds

**A status page is a claim with a date, and the date is the check.** The
handoff header, the plan's status block, the goal table and the architecture
file all carry an *as of* date; when the sittings at the bottom of this file
outrun it by more than a day, the header is wrong somewhere. Re-run the count
(`dotnet test`, `ls scenarios/*/ | wc -l`, `grep -c '^## ADR' DECISIONS.md`,
`grep -c '^| Q' open-questions.md`) before quoting any figure from the top of
a file.

---

# Sitting of 2026-09-06 (evening): Q190 answered from three saves

Two commits: `47ce875`, `8b13dc6`. No code changed; this was all research.

## 1. The owner watched fog and confirmed two inferred readings

Two screenshots from a skirmish (`fog-of-war.md` §9):

- **A gem seam renders in full inside unexplored blackness**, with no explored
  ground near it. So `RevealThroughFogOfWar` does not merely exist in the binary
  — it produces the affordance its name claims, and Gems is one of its six
  terrains. STRONGLY INFERRED → **CONFIRMED**.
- **An unexplored tile prints `Fog of War` in its own tooltip**, like any other
  terrain. So `WorldView` substituting the shipped fog terrain (ADR-0076) is
  DK2's own model rather than a convenience of ours. The tile is markable in the
  same shot, which is Q193's premise seen directly.

**Q197 is new and unexplained:** two portals show on the minimap, dimmed, on
ground nothing has explored. It cannot be `RevealThroughFogOfWar` — that is a
*terrain* flag and a Portal is a **room** — and the minimap colour routine tests
exactly one room type in its fogged branch, the Dungeon Heart. Second mechanism,
unread.

## 2. Q190 is answered: ONE bit, ONE viewpoint

The owner's objection was the strongest available — *two humans cannot share
fog, so it must be per keeper* — and it **reverses** (`fog-of-war.md` §10, §11).

A three-save series (control + one straight tunnel) on a four-keeper skirmish
map, diffed across **all 80 bit positions** of the save's tile record:

- **Lane 1, bit 6 is discovery.** 36 tiles changed in the control, 306 in the
  signal, and the changed set is **one** component **13 tiles tall** around a
  tunnel **1 tile tall** — a six-tile radius, which is the `PerceptionRange` of
  `0x6000` every creature record carries, arriving by an independent route.
- **It is the local player's alone**: set on **100%** of their territory and
  **0%** of the other three keepers'. An **exhaustive** scan found no mirror bit
  — and the same scan *did* recover the owner id's own bits, which is what makes
  the negative trustworthy rather than a failure to look.
- **342 tiles went 0→1 and none went 1→0.** *Stay Revealed* confirmed from data.

The binary had already said the same thing from the other end: the renderer's
gate is `tile + 0xB` bit `0x40`, hardcoded, **no player index**. Two independent
routes, same bit.

**So DK2 stores no per-keeper fog, and the multiplayer objection resolves as
per-MACHINE copies** — each machine's copy of that one bit means *"fogged for
whoever is sitting here"*. Strong but not absolute on the AI half: its fog could
live in memory unsaved, but then a computer keeper's knowledge would visibly
change across a save/load.

**What this means for ADR-0076:** the per-keeper mask is **ours**, as the branch
honestly said. Keep it — ours is derived deterministically from creature
positions, so it is per-keeper for free and cannot diverge — but **do not
describe it as DK2's model**.

## 3. Two corrections to the save format, worth more than the answer

**The tile grid is NOT at a fixed `0x8861`.** It is at **`0x14880`** on
TrapTime. The "fixed" reading came from two saves **of the same level**, which
is two samples of one thing — the project's standing generalisation trap in its
purest form. **Locate it by correlation instead**: lane 0 against the level's
design map (`<Level>Map.kld`, chunk 102, byte 0 of each 4-byte tile) scores
**99.7%** at the right offset on a start-of-game save.

**The three "churns everywhere, UNKNOWN" lanes churn because they were read as
whole bytes.** Bit by bit they are quiet, which is how bit 1.6 sat in plain
sight through the whole format investigation. Lane 3's high nibble resets to
zero wherever *any* keeper is active, in every territory at once, so it is a
property of the tile and not of a viewer — still UNKNOWN, no longer a fog
candidate.

## 4. Method note worth keeping

The save series answered in twenty minutes what two rounds of Ghidra could not,
and the reason is the control. Without the do-nothing save there is no way to
tell *"this bit changed because I explored"* from *"this bit changes
constantly"* — and the lanes in question were dismissed as noise for exactly
that reason. **A differential experiment needs its null.**

