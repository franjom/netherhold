# What DKII.EXE says about itself, without decompiling anything

Status: **facts only** — strings read out of the shipped executable. No code has
been decompiled, disassembled or transcribed. See the boundary note at the end,
and ADR-0003.

Prompted by the owner offering Ghidra (2026-09-04). Before answering whether
decompiling would help, four cheap questions were put to the binary itself. The
answers changed the recommendation, so they are recorded.

## The binary

| | |
|---|---|
| `DKII.EXE` | 2,987,568 bytes |
| `DKII-DX.exe` | 2,987,568 bytes — **the same size, so almost certainly the same binary** |
| Sections | `.text`, `.rdata`, `.data` — **not packed**, no UPX/ASPack/Petite signature |
| Timer imports | `QueryPerformanceCounter`, `QueryPerformanceFrequency`, `timeGetTime`, `GetTickCount` — **all four** |
| Balance variable names | **absent.** Searching for `EntranceGenerationSpeed`, `CreaturesSupportedByFirstPortal`, `MinimumImpThreshold`, `ConvertRoomHealth` finds nothing, so the community names come from somewhere else (the editor, or a template) and the VARIABLES table is indexed by number in here |

The GOG install also ships `patch.dll`, `ddraw.dll` and `dinput.dll`, which are
compatibility wrappers rather than game logic.

> **A false negative worth recording.** The first pass at this used `strings`,
> which is **not installed on this machine**, so every search returned zero and
> looked like a definite absence. `grep -a` on the binary was the working
> route. A missing tool and an absent string are indistinguishable unless the
> tool's absence is checked — and the greps that came back zero above are only
> trustworthy because the ones beside them came back non-zero.

## The 72 source filenames it still carries

The retail build left `__FILE__` strings in — from asserts, most likely — so the
executable names 72 of its own translation units. **This is a map of the game's
subsystems, in EA's own words**, and it costs nothing to read.

**Rooms — one file each, which is the shape "what does a room do" was missing:**
`Room.cpp`, `RoomManager.cpp`, `PlayerRooms.cpp`, `Entrance.cpp`,
`DungeonHeart.cpp`, `Treasure.cpp`, `Library.cpp`, `Hatchery.cpp`,
`Training.cpp`, `Workshop.cpp`, `Casino.cpp`, `CombatPit.cpp`, `Graveyard.cpp`,
`Crypt.cpp`, `Tourture.cpp` (sic), `prison.cpp` (sic), `Bridge.cpp`,
`BridgeRooms.cpp`, `BridgeThing.cpp`, `BridgeLighting.cpp`.

**The creature state machine — twenty-five state files plus its own scaffolding:**
`State.cpp`, `StateGroup.cpp`, `StateUtils.cpp`, `CallToArmsState.cpp`,
`CasinoState.cpp`, `CombatPitState.cpp`, `DisArmTrapState.cpp`,
`ExploreState.cpp`, `FightWithDrawState.cpp`, `GoodState.cpp`, `GuardState.cpp`,
`HungerState.cpp`, `ImpState.cpp`, `KeeperState.cpp`, `LairState.cpp`,
`ManufactureState.cpp`, `ObjectState.cpp`, `PatrolState.cpp`, `PayState.cpp`,
`PrayState.cpp`, `PrisonState.cpp`, `ReaperState.cpp`, `ResearchState.cpp`,
`RunAway.cpp`, `SulkState.cpp`, `ThrowingState.cpp`, `TourtureState.cpp`,
`TrainState.cpp`, `WaitState.cpp`.

**Everything else:** `Creature.cpp`, `CreatureInstance.cpp`, `Player.cpp`,
`ComputerPlayer.cpp`, `World.cpp`, `WorldTrigger.cpp`, `WorldWDL.cpp`,
`Object.cpp`, `Effect.cpp`, `EffectDescriptor.cpp`, `EffectElement.cpp`,
`EffectGeneration.cpp`, `EffectGenerator.cpp`, `ShotProcess.cpp`,
`Specials.cpp`, `Camera.cpp`, `RenderInfo.cpp`, `ProceduralMesh.cpp`,
`DefaultPlayerInterface.cpp`, `DefaultPlayerInterfaceCursor.cpp`,
`DefaultPlayerInterfaceGUI.cpp`, `HeroGateFrontEnd.cpp`.

## What that already tells us, for free

- **The portal's real name is Entrance.** There is no `Portal.cpp`; there is
  `Entrance.cpp`. That independently corroborates the note already in
  `RoomTypeId` — *"Portal = 3, Flame calls this Entrance"* — from a third
  direction, and it is the file to look in for Q86.
- **DK2's creature state machine is a class hierarchy with about 25 leaves**, and
  they are named. `CreatureState` here carries four activities and its own
  comment says the real machine "is much larger — Flame names dozens of state
  constants — and reconstructing it is a later job (Q2, Q7)". This is that job's
  table of contents, and it maps onto things already known: `HungerState` is the
  hatchery, `PayState` is payday, `SulkState` is the anger ladder's end (Q9),
  `ResearchState` and `ManufactureState` are the library and workshop, and
  `RunAway.cpp` is fleeing a fight — which `ImpTaskSystem` lists as absent.
- **Rooms are per-room code, not one parameterised room.** Sixteen room files
  against 26 room types. So "what does a room do" is genuinely sixteen
  behaviours and not one table, which is what the balance table's per-room
  variable clusters already suggested.
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
and 72 filenames. No instruction was disassembled and no logic was inspected.
That is the same category as the format research already credited in
`THIRD-PARTY-NOTICES.md` — facts with provenance — and it is a narrower step
than consuming Flame, which is somebody else's disassembly of this same
executable and which the project has used since M1.

**Reading function bodies is a different step and is not taken here.** It needs
the owner's decision and its own ADR, because it changes what the project can
say about its own method. See the recommendation put to the owner on 2026-09-04
and Q96.

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
| `LastGameTickSuccessfullyChoseAngryJob` | The angry job choice **can fail and is retried** — which is evidence against ADR-0053's draw-once rule, though not enough to settle whether a retry re-draws (Q122) |
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
