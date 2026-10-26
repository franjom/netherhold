# Research status

What is known against what is not. Three neighbours own the things this page
deliberately does not:

| for | go to |
|---|---|
| what the code currently does | `docs/HANDOFF.md` §*Current state*, and `dk2tools coverage` |
| every open unknown, and how to close it | `docs/research/open-questions.md` |
| built against the long-term goals | `docs/GOAL_ALIGNMENT.md` |
| the system-by-system table | `DK2_REIMPLEMENTATION_PLAN.md` §3 *Reverse-engineering status* |

Confidence tags are mandatory in every note: CONFIRMED / STRONGLY INFERRED /
HYPOTHESIS / UNKNOWN (AGENTS.md). **CONFIRMED is reserved for the running game**
— owner observation or a stopwatch. A binary read is STRONGLY INFERRED.

## Counts — run the command, never hand-maintain

| Figure | Command |
|---|---|
| ADRs | `grep -c '^## ADR-' docs/DECISIONS.md` |
| Questions (highest id) | `grep -oE '^\| \*{0,2}Q[0-9]+' docs/research/open-questions.md \| grep -oE '[0-9]+' \| sort -n \| tail -1` |
| Research notes | `ls docs/research/*.md \| wc -l` |
| Scenarios | `find scenarios -name '*.yaml' \| wc -l` |
| `HashVersion` / snapshot `FormatVersion` (kept equal) | `grep -n 'HashVersion = ' src/DK2.Simulation/GameSimulation.cs; grep -n 'FormatVersion = ' src/DK2.Simulation/WorldSnapshot.cs` |
| Tests, and how many skip without an install | `dotnet test DK2.slnx` |
| KWD record coverage | `dk2tools kwd corpus` |
| Trigger type coverage | `dk2tools kwd trigger-types` |
| Balance variables, terrains, rooms, creatures, objects, placed things | `dk2tools kwd vars \| terrain \| rooms \| creatures \| objects \| things` |

## Solved and built — each with its decision and its evidence

| What is settled | ADR | Research note |
|---|---|---|
| Gold lives in tiles; a player has no gold counter — only four per-place limits | ADR-0015 | `rooms-and-gold.md` |
| Where a room may go, from the ROOMS record's own placeable-on flags | ADR-0016 | `kwd-terrain-rooms.md` |
| Mana comes from owned land plus a flat 30 for a keeper's heart; three of 41 terrains carry a gain; the ceiling is a flat 200000 and never moves; a keeper has exactly one heart and its loss is defeat | ADR-0017 | `mana.md` |
| A tick is **250 ms** and a game second is **four ticks** — balance variable 19, written by the level loader from the LEVEL record and 4 in all 66 shipped levels, corroborated from inside the creature update and by the owner's portal stopwatch. `SimulationTiming.TicksPerSecond` is 4 | ADR-0089, ADR-0011 | `balance-variables-in-the-binary.md` |
| How a tile is worked: one data-driven rule for every work kind, landing **once a tick, `delta × efficiency ÷ 4`**. Q47's fifth was the *size* of an application, never its cadence. Efficiency is 80%, raised to 90% by a slap (25 s) or by dropping an imp onto marked work (a three-tile look, and the bonus lasts as long as that kind of work does). An upward job costs the terrain's health **deficit** | ADR-0020, ADR-0028, ADR-0069, ADR-0089 | `tile-work.md` |
| One measured behaviour may override one shipped number, under five admission conditions — `DigRockHealth` −5000 becomes −500 | ADR-0023 | `tile-work.md` |
| A room is a derived instance, claimed **whole** and never tile by tile, at `ConvertRoomHealth` −4000 | ADR-0027, ADR-0044 | `rooms-and-gold.md` |
| Not every player is a keeper: 0 unowned, 1 the hero side, **2 neutral — which is what an unclaimed room belongs to**, 3–7 keepers | ADR-0043, ADR-0052 | `kwd-terrain-rooms.md` |
| How an imp chooses a job, in **two tiers**: tier A walks a table of kinds whole-map one at a time; tier B, only when A is empty, ring-scans reinforce then gold, so a wall beats gold on the same tile and a nearer pile beats a farther wall | ADR-0058 | `imp-task-priority.md` |
| Where creatures come from: five gates, four in the data and one in the creature's own flags. A portal's arrival interval is **its own tally squared plus the level's base**, per room and never decremented except by an ownership change | ADR-0042, ADR-0047, ADR-0086 | `portals.md` |
| A portal's and a heart's centre tile is impassable, and dropping a creature into a portal removes it from the dungeon | ADR-0084 | `structure-blocks.md` |
| A creature has health, a hunger clock and regeneration; 32767 means "never" per *need* rather than per creature; own-land regeneration applies to two creatures of 46 | ADR-0048 | `creature-needs.md` |
| A creature walks to a lair, takes a tile on arrival and **heals only while asleep in its own bed** at 100 a second. A lair's capacity is its tile count, which is *not* the general rule for rooms | ADR-0049, ADR-0060 | `lair-acquisition.md` |
| A creature eats in its keeper's hatchery, nearest chicken by squared distance, `min(HpFromChicken, missing)`, nothing reserved | ADR-0067 | `eating.md` |
| Anger is **six counters** with a ceiling of **32767** (owner-measured twice by two methods) and the mood is the largest counter, not the sum; the ladder is content → unhappy → angry → leaves, or rebels if converted | ADR-0053, ADR-0081, ADR-0087 | `creature-anger.md` |
| Zero health is a **sixty-second window**, not death; a knocked-out creature holds exactly one sixth of its maximum; a corpse is not an object but DK2's `CDeadBody` thing, and holds no state; the dying states are 216 then 217 | ADR-0051, ADR-0071 | `creature-death.md` |
| What raises a creature's level, and the 32 × 9 per-level scaling table that had been parsed and read by nothing | ADR-0083 | `creature-levels.md` |
| Job selection for non-workers: needs first, then the mood's chooser building cumulative ranges from the record's chances, re-rolled every five game seconds; the room → job map is read from the room table | ADR-0082, ADR-0088, ADR-0061 | `job-selection.md` |
| The Prison: a 3x3 holds **two** (inner tiles plus one, CONFIRMED off the game's status bar), the drain is hunger, a Skeleton never withers, and a prisoner shuffles its cell and races the others to a chicken | ADR-0064, ADR-0068, ADR-0070, ADR-0073, ADR-0074 | `prison.md` |
| The Hatchery: coops are `min(tiles / 6, 36)`, never adjacent; the room lays **object 47, the Egg**, which hatches after about two seconds; one chicken per room tile | ADR-0066 | `hatchery.md` |
| The Hand of Evil holds a thing, not only a creature; a creature goes down on the keeper's own ground and only a worker may be dropped anywhere else | ADR-0062, ADR-0075, ADR-0526 (supersedes ADR-0077) | `hand-of-evil.md` |
| Payday is a journey and the money comes off the room underfoot, from the creature's tile; being owed is the whole trigger | ADR-0078, ADR-0079, ADR-0220 | `payday.md` |
| Fog of war is simulation state, per keeper | ADR-0076 | `fog-of-war.md` |
| An object is one type with an 18-byte tagged union keyed on its definition id; a bed is a thing standing on a tile that knows whose it is | ADR-0054 | `objects.md` |
| Gold on the floor is an object, and the limit named after it is not a limit | ADR-0055, ADR-0056 | `gold-on-the-floor.md` |
| A level plays on its **own** VARIABLES layered over the global table; `Availability` is decoded — kinds rooms/creatures/traps/doors/spells, states unavailable/researchable/available, per level and per player | ADR-0040 | `kwd-variables.md` |
| The Library accrues research to the keeper, and three of its bonuses are not what their names say; the research tree that spends it is ADR-0247 | ADR-0090 | `library-and-research.md` |
| Training pays, and the experience model under it was wrong in three places | ADR-0083 | `training.md` |
| **M1: all 18 KWD/KLD record-type ids parse and every record image round-trips byte for byte** (closed 2026-09-07) | ADR-0091 | `kwd-record-fields.md` |
| A level's placed THINGS become world contents, and three transcribed field meanings were wrong | ADR-0101 | `placed-things.md` |
| Trigger parameter views follow DKII.EXE's reads, not the editor's labels | ADR-0092 | `trigger-semantics.md` |
| Trigger flags and timers are level state, and the byte called `Flag` is an opcode | ADR-0102 | `trigger-state.md` |
| **Action points are ordered world state**: all 1,152 shipped points enter `WorldState` in THINGS-file order with the rectangle, ids/link, the five loader-copied flag bits and the saved runtime words. There is **no** availability field — the earlier shared `Available` interpretation is refuted | ADR-0103 | `action-points.md` |
| **Doors and traps retain authored state**; construction, opening and route costs are built. Door contact and the door fight, hero gates, every shipped trap's firing, the Boulder, the Jack In The Box, the reveal, trap upkeep and damage, the Guard Post and creatures fighting a revealed trap are built; disarming is deferred (ADR-0329, Q455) | ADR-0106, ADR-0159, ADR-0250/0251, ADR-0318, ADR-0320, ADR-0321, ADR-0326, ADR-0330, ADR-0331, ADR-0333, ADR-0337, ADR-0338, ADR-0340 | `doors-and-traps.md`, `door-blocking.md`, `traps.md` |
| The keeper spells and their research upgrades: Create Imp, Thunderbolt, Heal, Call to Arms, Inferno, Chicken, Create Gold, Tremor, Turncoat, Summon Horny, OneShotReaper and Possession's simulation side are built; Sight of Evil casts with an empty effect | ADR-0279, ADR-0287, ADR-0302, ADR-0350, ADR-0352, ADR-0247 | `keeper-spell-*.md`, `library-and-research.md` |
| DK2's computer keeper, read from the binary and built as an input source: task machine, messages, threat response, battle step, retreat, attack planner, bridges, training moves, room needs and spell casts | ADR-0278, ADR-0283, ADR-0284, ADR-0298, ADR-0304, ADR-0317, ADR-0327, ADR-0353, ADR-0354 | `computer-keeper-*.md`, `docs/KEEPER_AI.md` |
| The Temple keeps the newest three offerings per room anchor and scans the 44 recipes for an exact ordered suffix | ADR-0149 | `sacrifice.md` |
| Every creature holds one of DK2's 281 state ids, its activity is derived from the state, and `SetState` dispatches the category hooks from one table | ADR-0209, ADR-0210, ADR-0308 | `creature-state-table.md`, `CategoryHooks.Slots` |
| **Hero parties and effect generators are ordered world state without behaviour**: all 220 parties from the 48-level campaign, every nested member field and the one generator enter `WorldState` verbatim; party members are not placed creatures | ADR-0108 | `hero-parties-and-effect-generators.md` |
| The 1999 manual is a corroborating source, never an overriding one | ADR-0037 | `manual-1999.md` |
| DKII.EXE may be decompiled and its output may not enter the repository; binary facts rank **below** owner observation and never reach CONFIRMED alone | ADR-0046 | `binary-entrance.md` |
| A Flame name may be cited in a document and never appears in code | ADR-0080 | `wiki-as-a-source.md` |
| Ten mechanics DK2 built and switched off — and a live reader with a zero value is **disabled**, not cut | ADR-0085 | `balance-variables-in-the-binary.md` |

## Read, with no decision to make

Facts established and recorded; nothing in `src/` turns on them yet.

| What | Research note |
|---|---|
| **Level scripting.** The shipped corpus holds **3,753 generic trigger records** (tag 213, **46** types) and **5,408 action records** (tag 214, **38** types). *51 is the highest action type **id**, not a count of types.* Semantics are read for the shipped types; four unshipped action types have no body read, and the transcribed selectors and unread fields are Q225–Q227 | `trigger-semantics.md`, `dk2tools kwd trigger-types` |
| **The balance variable → global map.** The engine caches 181 of 240 variables in dedicated globals through a jump table indexed by `id − 3`, which makes *"what code reads variable N"* an answerable question | `balance-variables-in-the-binary.md` |
| **The original's own source-file names.** Every assert carries its `__FILE__`, so the executable lists DK2's 72 translation units — and its creature AI is one file per state (`LairState.cpp`, `HungerState.cpp`, …) | `source-file-map.md`, `executable-strings.md` |
| **The saved-game format.** A `.SAV`/`.QSAV` opens with a 0x134-byte header (level name at `0xC2`, game tick at `0x102`), then a fixed block, then the tile grid at `0x8861` — row-major, 10 bytes a tile, with terrain, owner, room instance id and health identified. Player gold is at `0x2dd4`. Entities are typed sections that serialise themselves; there is **no** 189-byte entity record | `sav-format.md` |
| **File formats:** WAD, KWD/KLD, KMF, STR, textures, video. `.str` string tables, all 92, decode through their `MBToUni.dat` code page (`src/DK2.Data/Text/Dk2StringTable.cs`); ids are table-local and zero-based and the header's second word is a file id running 1000..1091 | `wad-format.md`, `kwd-container.md` |
| **The Mentor's speech and the notification system.** One entry point turns an event into a spoken line plus subtitle, addressed as **(type, variant)** through a 100-entry table; a speech reference is stored **negated with a 10000 bias**, which is why no line's id appears as a constant. Type 89 is the taunt pool (27 of 33 authored `silly` lines); the idle taunt is keyed to **cursor stillness in real milliseconds**, not game time. Six recorded lines have no code path at all (Q282) | `mentor-speech-and-notifications.md` |
| **What each room is for**, in prose, from the shipped manual, paired with the balance variable that quantifies it | `manual-1999.md` |
| **How DK2 draws its walls** — solid mass with intersections cut into it, pieces chosen by the sides a room runs along, damage as a texture swap, and runtime vertex deformation (inferred by its own source, not proven). The room wall chooser, the wall torches, plain terrain, room floors and liquids are built in the presentation layer and drawn in Unity from the install (ADR-0392, ADR-0391, ADR-0408, `docs/plans/2026-10-09-classic-look.md`). **Rendering only; no rule may depend on it** | `wall-rendering.md`, `wall-pieces.md`, `wall-torches.md`, `floor-and-terrain-pieces.md` |
| **Which fields never vary and which the engine never reads.** Balance variables and CREATURES are swept; OBJECTS is swept on the data side only. `dk2tools kwd fieldsweep` | `record-field-sweep.md`, `variables-corpus-sweep.md` |

## The largest unknowns

| What | Where it stands |
|---|---|
| **Pathfinding** (Q5) | The biggest risk, and now a measured one. DK2 routes over a **triangulated navigation mesh** and follows it with a **steering system** (ADR-0155, [navigation-and-steering.md](research/navigation-and-steering.md)); the algorithms are read (Q341–Q349) and the rebuild landed in three phases (ADR-0157), so every creature routes over the mesh and moves by the steering tick and the grid A* survives only behind `TryFindNearest`. DK2's own route log measures it: 21,288 of 21,298 requests on Level 7, and all but three of 86,140 on a played Level 10 |
| **Combat** (Q8) | Melee is read and built end to end (ADR-0163/0164/0165): acquisition, approach, the swing's two clocks, the blow, retaliation, the heart as a target, fear — threat radiated, the back-off and the withdrawal — and the NonFighter arm with the Scout's alarm; the attack spells, their shots and the status effects they leave are built (ADR-0167/0168, `combat-spells.md`, `shots.md`, `creature-status-effects.md`); the approach as DK2's state 8, a walk resuming 199, and the victory pause as one play of the creature's clip are built too (ADR-0169, ADR-0483, ADR-0368, `combat-approach-and-victory.md`); the friendly arm — Heal, Haste, Armour, Invulnerable and Invisible on the caster's own hurt fighters — is built (ADR-0170, `spell-friendly-arm.md`). M7 |
| **Trigger execution** | The world, player, action-point, party and creature roots walk once per game turn (ADR-0152, `trigger-walkers.md`). The creature per-turn dispatcher evaluates its three measurable conditions and preserves DK2's constant-false event arms; which action effects are carried out is in GOAL_ALIGNMENT row 2 and `trigger-walkers.md`; the owner's recorded games of Levels 1–4 replay headlessly over seeds and win on some of them (Q444, `docs/experiments/`) |
| **Torture and conversion** | The three torture fields are read (ADR-0193, 0196, 0200; `TortureRules`, `TortureSystem`); DK2 keeps torture's state on the creature, not on the room |
| **Why arrivals slow as a dungeon fills** (Q97) | The symptom is CONFIRMED. Two mechanisms produce it — the implemented interval scaling, and a draining eligibility weight the binary suggests — and they differ in whether lair size matters |
| **What a room does** | Rooms with behaviour are listed by `dk2tools coverage`. The ROOMS record carries art, size and cost and says nothing about behaviour, so for a room not yet built what is missing is the *shape*: capacity, per-creature eligibility, and what a room does with the state it changes. Do not assume capacity equals tile count — a temple's is its rim, `2(w+h) − 4` (Q60) |
| **Which rooms a keeper may build on a level** | `Availability` kind 1 says so; the computer keeper's need rules and a creature's idle job read it (`RoomNeedRules`, `CreatureJobSystem`), `RoomRules` does not, and nor does DK2's build path (`room-slab-chain.md` §2): the gate belongs to the build menu |
| **Destroying a room somebody else holds** | Read and built (ADR-0342, `room-health.md` §4–5): heroes knock a keeper's buildable rooms down tile by tile as they walk across them. Job 11's room attack (Q523) is not built; the room messages are (ADR-0412–0414) |
| **What defeat does** (Q40) | Built (ADR-0162, ADR-0207, `DefeatRules`, Q358): creatures leave by the nearest portal, rooms and gold go neutral, the heart stays as a ruin. Left: the Destructor (object 100) |
| **The computer keeper** (Q509, Q555, Q563) | Built and compared with DK2's own keepers on two maps; every verb agrees in kind and the open differences are timing (`computer-keeper-observations.md`). The research move and prisoner decisions are unbuilt, the trap and door step is built on a parked branch and not on master, and the nine personalities load (ADR-0410) (`docs/KEEPER_AI.md` §12) |
| **Front end, campaign map and progression** | F1 storage/image readers, F2 scenery/camera sessions, F3 menu shells and F4 campaign map/briefing are built; session-loop integration and deferred page operations remain. Evidence: `front-end-menus.md`, `campaign-map-and-progression.md`, `front-end-assets.md`. SDT/MPEG and bank-map readers remain unbuilt; TGQ has a playback proof of concept. Plan: `plans/2026-10-23-front-end.md` |
| **Audio event semantics** | Menu sound-request hooks are built in F3; playback remains unbuilt. Audio container formats are inventoried in `front-end-assets.md`; movie playback (container, TQI video, EA ADPCM) is read and built as a decoder (`movie-format.md`, `src/DK2.Data/Media`) |

## Per-mechanic notes

One file per mechanic in `docs/research/`, on the Appendix B template
(`DK2_REIMPLEMENTATION_PLAN.md`). Three are the current model of the form —
tabular, with a provenance label on every row: `placed-things.md`,
`trigger-semantics.md`, `trigger-state.md`.

## Answerable by playing the game

`docs/research/observation-checklist.md` — the open questions a person in front
of DK2 can settle by looking, as concrete experiments with what each outcome
would mean, plus §7, the ones playing cannot settle. `docs/RESEARCH-QUEUE.md` is
the other half: what the binary and the shipped files can settle without the
game running.

Two routes into that list have both paid for themselves and both are worth
reaching for again. **Asking the owner for a batch of remembered answers** is
cheaper than designing a batch of experiments. **A recording of play turned into
a numbered question list** (`docs/CODEX_TASKS.md` Brief 4) reversed shipped
behaviour three weeks old that every test agreed with (Q56, the free-imp clock).
