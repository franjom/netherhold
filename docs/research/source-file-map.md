# The original's own source-file names, and where its code lives

**Source:** `DKII.EXE`, read on 2026-09-05. Facts in our own words; ADR-0046
governs the boundary. Nothing here is decompiler output.

**Why this exists.** Like `balance-variables-in-the-binary.md`, this is a
**research multiplier rather than a finding**. Every assert and diagnostic in the
original carries its own `__FILE__`, so the executable contains a list of the
game's translation units by name. Given a function, the assert it calls names the
file it was compiled from; given a file, its assert string names the functions
that use it. That turns "find the lair code" from a hunt into a lookup.

Two shapes of use, and both have already paid:

- **file → code.** The one reference to `LairState.cpp` landed inside the
  function that acquires a lair, which is what `lair-acquisition.md` is built on.
- **code → file.** The free-imp clock sits in a function whose asserts name
  `Player.cpp`, which is how it was recognised as the keeper's per-frame update
  rather than something imp-specific.

**A caution.** A string with **no** references is not evidence that its file's
code is absent. Ghidra's analysis only resolves a reference it can reach, and
code reached solely through a vtable often has none — `LairState.cpp` shows zero
callers by that measure and one by a raw scan for the address itself.

## The 72 names

Grouped by what they are rather than by address, because the grouping is the
useful part.

**Creature behaviour — one file per state.** This is the shape of DK2's creature
AI, and it is worth knowing before designing anything that touches it:

`State.cpp`, `StateGroup.cpp`, `StateUtils.cpp`, `WaitState.cpp`,
`ExploreState.cpp`, `PatrolState.cpp`, `GuardState.cpp`, `RunAway.cpp`,
`FightWithDrawState.cpp`, `ThrowingState.cpp`, `GoodState.cpp`,
`KeeperState.cpp`, `SulkState.cpp`, `HungerState.cpp`, **`LairState.cpp`**,
`PayState.cpp`, `PrayState.cpp`, `TrainState.cpp`, `ResearchState.cpp`,
`ManufactureState.cpp`, `PrisonState.cpp`, `TourtureState.cpp` (their spelling),
`CasinoState.cpp`, `CombatPitState.cpp`, `CallToArmsState.cpp`,
`DisArmTrapState.cpp`, `ReaperState.cpp`, **`ImpState.cpp`**, **`ImpUtils.cpp`**.

**Rooms — one file per room, plus the machinery:**
`Room.cpp`, `RoomManager.cpp`, `PlayerRooms.cpp`, `Treasure.cpp`, `Library.cpp`,
`Training.cpp`, `Workshop.cpp`, `Hatchery.cpp`, `Crypt.cpp`, `Graveyard.cpp`,
`prison.cpp`, `Tourture.cpp`, `Casino.cpp`, `CombatPit.cpp`, `DungeonHeart.cpp`,
`Entrance.cpp`, `HeroGateFrontEnd.cpp`.

**Things and the world:**
`Creature.cpp`, `CreatureInstance.cpp`, `Object.cpp`, `ObjectState.cpp`,
`ShotProcess.cpp`, `Specials.cpp`, `World.cpp`, `WorldTrigger.cpp`,
`WorldWDL.cpp`, `Camera.cpp`, `RenderInfo.cpp`, **`Player.cpp`**,
`ComputerPlayer.cpp`.

**Bridges get five files of their own**, which is more than any room and says
something about how much trouble they were: `Bridge.cpp`, `BridgeThing.cpp`,
`BridgeThing.h`, `BridgeRooms.cpp`, `BridgeLighting.cpp`.

**Effects and the interface:**
`Effect.cpp`, `EffectDescriptor.cpp`, `EffectElement.cpp`,
`EffectGeneration.cpp`, `EffectGenerator.cpp`, `DefaultPlayerInterface.cpp`,
`DefaultPlayerInterfaceCursor.cpp`, `DefaultPlayerInterfaceGUI.cpp`.

## Two things the list itself tells us

**`Entrance.cpp` and `HeroGateFrontEnd.cpp` are separate files**, which supports
this project's reading that a portal and a hero gate are different mechanisms
rather than one room with a flag (`portals.md`).

**There is no `Gold.cpp` and no `Treasury.cpp` beyond `Treasure.cpp`.** Gold's
rules live in the room, the object and the imp code, which is where this project
found them and is why `FloorGold` and `GoldStore` are separate from each other.
