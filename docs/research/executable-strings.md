# What DKII.EXE says about itself, without decompiling anything

Status: **facts only** — this page is what the shipped executable's *strings*
say, which is the cheapest evidence in the binary and the least ambiguous. No
instruction is disassembled here. Reading function bodies is a separate step and
it is now the project's default (Q96, ADR-0046); ADR-0003 governs the boundary
either way, and no decompiler output enters the repository.

**Every claim on this page reads out of `DKII.EXE` v1.70 with a string or
byte-range scan**, so it is re-verifiable in one pass over the file.

## The binary

| | |
|---|---|
| `DKII.EXE` | 2,987,568 bytes |
| `DKII-DX.exe` | 2,987,568 bytes, and **not the same file** — 1,126 bytes differ (0.038%). But **`.text` and `cseg` are byte-identical**: the differences are the PE header, 5 bytes of `.rdata`, and the ~10 KB appended after the last section. So a recorded address is valid in either. |
| Sections | **eleven**, not three — see below. **Not packed**, no UPX/ASPack/Petite signature, and every string and RTTI name below reads straight out of the file. |
| Timer imports | `QueryPerformanceCounter`, `QueryPerformanceFrequency`, `timeGetTime`, `GetTickCount` — **all four** |
| Balance variable names | **absent.** Searching for `EntranceGenerationSpeed`, `CreaturesSupportedByFirstPortal`, `MinimumImpThreshold`, `ConvertRoomHealth` finds nothing, so the community names come from somewhere else (the editor, or a template) and the VARIABLES table is indexed by number in here |

**There are two executable sections, and that is a trap for any scan.** From the
PE section table:

| section | RVA | virtual size | executable |
|---|---|---:|---|
| `.text` | `0x00001000` | 2,431,650 | **yes** |
| **`cseg`** | `0x00253000` | **101,914** | **yes** |
| `.rdata` | `0x0026C000` | 136,507 | |
| `.data` | `0x0028E000` | 1,150,416 | |
| `grpoly_d`, `uva_data`, `idct_dat`, `tqia_dat`, `dseg`, `lbmpeg_d` | `0x003A7000`+ | 34,392 total | |
| `.rsrc` | `0x003B3000` | 12,078 | |

**A scan restricted to `.text` misses `cseg`**, so "no references anywhere" has
to mean both blocks or it means nothing. The six small data sections are the
video codec's tables (`lbmpeg`, `idct`), not game logic.

The GOG install also ships `patch.dll`, `ddraw.dll` and `dinput.dll`, which are
compatibility wrappers rather than game logic.

**A missing tool and an absent string are indistinguishable unless the tool's
absence is checked.** `strings` is not installed on this machine, so a first
pass returned zero for everything and looked like definite absence; `grep -a`
over the file was the working route. The zero counts above are trustworthy only
because the searches beside them came back non-zero in the same pass.

## The 72 source filenames it still carries

The retail build left `__FILE__` strings in — from asserts, most likely — so the
executable names **72 of its own translation units**, plus one header
(`BridgeThing.h`) for 73 distinct filenames in all. **This is a map of the
game's subsystems, in EA's own words**, and it costs nothing to read. The
grouped list with what each is for is `source-file-map.md`; both pages carry the
same 73 names.

**Rooms — one file each, which is the shape "what does a room do" was missing:**
`Room.cpp`, `RoomManager.cpp`, `PlayerRooms.cpp`, `Entrance.cpp`,
`DungeonHeart.cpp`, `Treasure.cpp`, `Library.cpp`, `Hatchery.cpp`,
`Training.cpp`, `Workshop.cpp`, `Casino.cpp`, `CombatPit.cpp`, `Graveyard.cpp`,
`Crypt.cpp`, `Tourture.cpp` (sic), `prison.cpp` (sic), `Bridge.cpp`,
`BridgeRooms.cpp`, `BridgeThing.cpp`, `BridgeLighting.cpp`.

**The creature state machine — twenty-five `*State.cpp` files plus its own
scaffolding (`State`, `StateGroup`, `StateUtils`, `RunAway`, `ImpUtils`):**
`State.cpp`, `StateGroup.cpp`, `StateUtils.cpp`, `CallToArmsState.cpp`,
`CasinoState.cpp`, `CombatPitState.cpp`, `DisArmTrapState.cpp`,
`ExploreState.cpp`, `FightWithDrawState.cpp`, `GoodState.cpp`, `GuardState.cpp`,
`HungerState.cpp`, `ImpState.cpp`, `KeeperState.cpp`, `LairState.cpp`,
`ManufactureState.cpp`, `ObjectState.cpp`, `PatrolState.cpp`, `PayState.cpp`,
`PrayState.cpp`, `PrisonState.cpp`, `ReaperState.cpp`, `ResearchState.cpp`,
`RunAway.cpp`, `SulkState.cpp`, `ThrowingState.cpp`, `TourtureState.cpp`,
`TrainState.cpp`, `WaitState.cpp`, `ImpUtils.cpp`.

**Everything else:** `Creature.cpp`, `CreatureInstance.cpp`, `Player.cpp`,
`ComputerPlayer.cpp`, `World.cpp`, `WorldTrigger.cpp`, `WorldWDL.cpp`,
`Object.cpp`, `Effect.cpp`, `EffectDescriptor.cpp`, `EffectElement.cpp`,
`EffectGeneration.cpp`, `EffectGenerator.cpp`, `ShotProcess.cpp`,
`Specials.cpp`, `Camera.cpp`, `RenderInfo.cpp`, `ProceduralMesh.cpp`,
`DefaultPlayerInterface.cpp`, `DefaultPlayerInterfaceCursor.cpp`,
`DefaultPlayerInterfaceGUI.cpp`, `HeroGateFrontEnd.cpp`, and the header
`BridgeThing.h`.

## What that already tells us, for free

- **The portal's real name is Entrance.** There is no `Portal.cpp`; there is
  `Entrance.cpp`. That independently corroborates the note already in
  `RoomTypeId` — *"Portal = 3, Flame calls this Entrance"* — from a third
  direction, and it is the file to look in for Q86.
- **DK2's creature state machine has 25 named states in 25 files, and one
  class.** Not an inheritance tree with 25 leaves — a dispatch table; see the
  RTTI section below. The filenames map onto things already known:
  `HungerState` is the hatchery, `PayState` is payday, `SulkState` is the anger
  ladder's end (Q9), `ResearchState` and `ManufactureState` are the library and
  workshop, and `RunAway.cpp` is fleeing a fight. The table itself has since
  been read: `creature-state-table.md` §4 (ADR-0083).
- **Rooms are per-room code, not one parameterised room.** Twenty room-related
  files, of which **fourteen name an individual room** (Entrance, DungeonHeart,
  Treasure, Library, Hatchery, Training, Workshop, Casino, CombatPit,
  Graveyard, Crypt, Tourture, prison, Bridge) against 26 room types. So "what
  does a room do" is genuinely a dozen-odd behaviours and not one table, which
  is what the balance table's per-room variable clusters already suggested.
- **`ProceduralMesh.cpp` is the wall renderer** (Q71/Q72, wall-piece selection
  and runtime deformation), and it being *procedural* supports what
  `wall-rendering.md` inferred rather than proved.
- **`Specials.cpp`** is the level specials — the one route to imps the project
  records as unimplemented in `ImpSupplyRules`.
- **Nothing names the main loop.** No `Main.cpp`, `Game.cpp` or `Time.cpp`, so
  only files carrying asserts appear. Q1's route is the four timer imports, not
  a filename.

## The boundary this stays inside

What is above is **strings**: sizes, section names, imported Windows API names,
and 73 filenames. No instruction was disassembled and no logic was inspected on
this page. That is the same category as the format research credited in
`THIRD-PARTY-NOTICES.md` — facts with provenance — and a narrower step than
consuming Flame, which is somebody else's disassembly of this same executable.

Reading function bodies *is* now done, elsewhere and under ADR-0046 (Q96
answered by the owner, 2026-09-04): facts may cross into `docs/research/`,
decompiler output may never cross anywhere.

---

## The debug dumps name the structs, field by field (2026-09-04)

Found on the pass the owner asked for when they made the binary the primary
source (ADR-0046 amendment). The retail build kept its **printf-style debug dump
functions**, not just its `__FILE__` asserts — so `Creature.cpp` and `Object.cpp`
each carry a contiguous block of format strings, one per field, in EA's own
names. Reading them costs one string dump and no decompilation.

Method, repeatable: search defined strings for a needle, then dump every string
in the surrounding address range. Field labels sit in source order, and a
`Name:\n` label with no `%d` marks the start of a sub-struct.

### What a creature *is*, in DK2's own vocabulary

About 220 fields in the `Creature.cpp` dump. Nine of them land on open questions,
and this is why the pass was worth doing at all:

| Field | Lands on |
|---|---|
| `InWater` | **A creature tracks being in water as its own state** — separate from `IsSlowed` and `IsHasted`, which are the spell effects. So the water hindrance is not implemented through the generic slow (Q118) |
| `IsSlowed` / `IsHasted` | Two effects this project has no source of yet, and evidence that "slowed" is a *flag*, not a speed field |
| `Leaving` | Desertion is a **state a creature is in**, not an event that happens to it — corroborating the owner's answer that a deserter walks to a portal (Q116). Also `LeaveNoTrace` and `CannotLeaveHealthLoss`, neither understood |
| `TurncoatPlayer` / `TurncoatTimer` | Turning turncoat means **belonging to another player for a time**, which is the shape of the owner's *"angry converted good creature turns turncoat"* (Q116) |
| `RebelActive`, `RebelLeader`, `RebelPos`, `RebelTag`, `RebelDelay` | **Rebellion has a leader and a place.** It is a bigger mechanic than "becomes a hero", and five fields wide (Q122) |
| `LastGameTickSuccessfullyChoseAngryJob` | The angry job choice **can fail and is retried**, which pointed at Q122 — since answered from the binary and built: it **re-draws** (ADR-0061) |
| `HasBeenKnockedOut` | EA's own name for the collapse ADR-0051 models. `DieInstantly` sits nearby, which is the bodyless creatures' path |
| `PortalGeneratedFromId` | **A creature remembers which portal it came from.** Ours does not. Note it is *not* what a deserter uses — the owner says nearest, not origin |
| `ContributesToPortalLimit` | **Q98's shape, answered.** Portal capacity counts creatures that carry this flag, so "sums hero portals and counts portals that cannot produce" was the wrong end of the problem — the per-creature side is what we were missing |
| `Level`, `ExperiencePoints` | Levels are per-instance, as expected, and pair with the per-level stat table (Q119) |

Others worth knowing exist, unexplained and not guessed at: `SpeedChangeTimer`,
`WorkFasterTimer`, `AngrySpecialTimer`, `MoveFromWorkCounter`, `PaymentAttempts`,
`PaymentNeeded`, `FearedByCreatureId`/`FearedSeverity`/`FearedTimer`,
`PoisonedByPlayerId`/`PoisonedSeverity`/`PoisonedTimer`, `OnFirePlayerId`,
`NWaterFootprints`, `HypnotisedPrevOwner`, `SkeletonCreationTime`, `SneakingAs`,
`CasinoBigWinnerWinnings`, `KillsInCombatPit`, `CreatedAtMapX`/`CreatedAtMapY`.

### What an object *is*: a tagged union, and the kinds are named

`Object.cpp`'s dump prints a `Union Data:` block whose members name the object
kinds: `Lair`, `TombStone`, `BookCase`, `GuardPost`, `TrainingTarget`, `Boulder`,
`ManufactureCrate`, `TortureWheel`, `ResearchBook`, `Chicken`, `Coop`, `Rat`,
`Gold`, `Special`, `CallToArms`, `ManaVault`, `ClaimedManaVault`,
`TempleCandleStick`, `FECandleStick`, `Destructor`, `Spinning`, `ThreatBurn`,
`HeroGate2By2`.

Three things follow, and one of them corrected a claim this project had already
committed:

1. **There is no `Sarcophagus` and no `Coffin`** anywhere in the executable's
   strings. A bed is a `Lair` object whatever art it wears — which is what
   closed Q113 and retired a wrong reading of the owner's first wording.
2. **A `Lair` object's own dump includes a `CreatureId`**: the bed knows whose it
   is. `LairRules` puts that relation on the creature instead; same relation,
   opposite end.
3. **Objects carry per-kind state**, so the OBJECTS record (M6) is not one flat
   struct — `ThreatBurn` has `CurrentThreat`/`StartThreat`/`Timer`, a mana vault
   has an `EffectTriggerDelayGameTicks`, a torture wheel has an
   `InitialRevolutionSpeed`.

### The boundary, again

Everything above is a **name and a relation** — facts, in ADR-0046's terms,
written up here in our own words with the file that carries them. No decompiler
output crossed, and none is quoted: this whole pass read *strings*, which is the
cheapest evidence in the binary and the least ambiguous.

---

## The executable kept its RTTI, and it corrects us (2026-09-04)

**163 distinct mangled MSVC type names** (`.?AV…@@`), so the build had `/GR` on
and every class with a virtual function is named in `.rdata`. This is a read of
the *names* only, which is enough to settle a structural question.

**The state machine is one class, not twenty-five.** The `__FILE__` list above
names 25 `*State.cpp` translation units, and a class per state each with a
virtual `Update` would have produced 25 RTTI entries. **Exactly one of the 163
names contains "State": `.?AVCState@@`.** No `CImpState`, no `CHungerState`, no
`CLairState`. One entry against 25 files is the signature of a **single state
class driven by a table of per-state handler functions** — which is what a file
per state looks like when each file exports free functions registered by state
id.

So the thing worth reading is not 25 vtables. It is **the dispatch table**, and
what it gives is different and better:

| | |
|---|---|
| The **state id numbering** | Nothing in this project has DK2's state enum. A table indexed by state id *is* that enum, in order |
| Handler addresses per state | Turns "how does a creature choose a job?" from a hunt into a targeted read of two or three functions |
| Which states share a handler | Tells us where DK2 treats several states as one behaviour, which no filename can |

**What the class names give on their own.** A map of the engine's own
architecture, in EA's words: `CThing` and `CTag` (the umbrella types),
`CBridgeThing`, `CEffect`/`CEffectElement`, `CLight`, `CComponent`,
`CEngineInterface`, `CGameManagerInterface`, `CPlayerInterface` /
`CDefaultPlayerInterface`, and a `Tb`-prefixed layer (`TbWItemBase`,
`TbGraphicFileLoader`, `TbPNGLoader`, `TbSysCommand::*`, `TbVariableTag`) which
is Bullfrog's own toolbox library rather than DK2 code. `CThing` being the parent
of both creatures and objects matches what `Object.cpp`'s tagged union already
suggested.

**The dispatch table was then read**, which is why this section is history
rather than a plan: the state records and their 32 categories are decoded in
`creature-state-table.md` §4 and §6 (ADR-0083, Q184–Q186). Combat is
still `FightWithDraw`, `Throwing` and `RunAway`, three of the states that table
names.
