// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Data;
using DK2.Data.Text;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class WorldHoverTextTests
{
    [InstallFact]
    public void LibraryStatusSeparatesResearchersFromBooksAndWorkshopCountsStoredCrates()
    {
        string install = Dk2InstallGate.Path;
        var db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        for (int y = 10; y <= 12; y++)
        for (int x = 10; x <= 12; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Library), PlayerIds.FirstKeeper);
        for (int y = 10; y <= 12; y++)
        for (int x = 20; x <= 22; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), PlayerIds.FirstKeeper);
        int libraryTile = world.TileIndex(new TilePos(11, 11));
        world.SpawnObject(db, 64, PlayerIds.FirstKeeper, libraryTile).AutoPlaced = true;
        world.SpawnObject(db, 4, PlayerIds.FirstKeeper, libraryTile);
        CreatureDef workerType = db.CreatureByName("Warlock")!;
        CreatureState researcher = world.SpawnCreature(db, workerType.CreatureId, PlayerIds.FirstKeeper, new TilePos(11, 11));
        researcher.HeldPlace = new RoomPlace(libraryTile, 1);
        byte crate = db.Traps.Values.First(t => t.CrateObjectId != 0).CrateObjectId;
        world.SpawnObject(db, crate, PlayerIds.FirstKeeper, world.TileIndex(new TilePos(21, 11)));
        var strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        var text = new WorldHoverText(db, strings.Resolve, PlayerIds.FirstKeeper);
        HoverTextSnapshot library = text.Capture(world, new(WorldHoverKind.Tile, new TilePos(11, 11)));
        Assert.Contains("Researchers: 1 / 2.", library.TopStatus);
        Assert.Contains("Shelf space filled: 1 / 2.", library.TopStatus);
        HoverTextSnapshot workshop = text.Capture(world, new(WorldHoverKind.Tile, new TilePos(21, 11)));
        Assert.Contains("Crate space filled: 1 / ", workshop.TopStatus);
        Assert.DoesNotContain("?", workshop.TopStatus);
    }

    [InstallFact]
    public void OwnedRoomStatusUsesLiveStorageAndNativeCapacityTemplates()
    {
        string install = Dk2InstallGate.Path;
        var db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        RoomRules.Place(db, ref world.Tile(10, 10), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        RoomRules.Place(db, ref world.Tile(11, 10), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        world.Tile(10, 10).GoldHeld = 321; world.Tile(11, 10).GoldHeld = 123;
        RoomRules.Place(db, ref world.Tile(15, 10), db.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        RoomRules.Place(db, ref world.Tile(16, 10), db.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        var strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        var text = new WorldHoverText(db, strings.Resolve, PlayerIds.FirstKeeper);
        var simulation = new GameSimulation(db, world, seed: 7);
        ulong before = simulation.StateHash();
        HoverTextSnapshot treasury = text.Capture(world, new(WorldHoverKind.Tile, new TilePos(10, 10)));
        Assert.Contains("Gold storage used: 444 / ", treasury.TopStatus);
        HoverTextSnapshot lair = text.Capture(world, new(WorldHoverKind.Tile, new TilePos(15, 10)));
        Assert.Contains("Capacity used: 0 / 2 Bed spaces.", lair.TopStatus);
        Assert.Equal(before, simulation.StateHash());
        Assert.DoesNotContain("?", lair.TopStatus);
    }

    [InstallFact]
    public void ACreaturesPanelColumnShowsTheTypeDescriptionWithItsJobClassAndFightStyle_NoPopupInferredFromFrameOrder()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        Dk2StringTable strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        var capture = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        CreatureDef firefly = database.Creatures.Values.First(c => c.DescriptionStringId == 2349);
        Assert.Equal(("Firefly.  Scout.  Fighting Style: Flanker.", ""), (capture.CreatureType(firefly).TopStatus, capture.CreatureType(firefly).TooltipName));
        CreatureDef imp = database.Creatures.Values.First(c => c.DescriptionStringId == 2347);
        Assert.StartsWith("Imps perform such tasks", capture.CreatureType(imp).TopStatus);
    }

    [InstallFact]
    public void ShippedHeartAndPortalNamesAndCreatureStatusComeFromSettledDataWithoutChangingHash()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level1")!;
        WorldState world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        Dk2StringTable strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        var capture = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        RoomInstance heart = RoomIndex.Build(world, database).First(r => r.Definition.TypeId == RoomTypeId.DungeonHeart);
        var hit = new WorldHoverHit(WorldHoverKind.Tile, world.TilePosition(heart.TileIndices[0]));
        HoverTextSnapshot snapshot = capture.Capture(world, hit);
        Assert.Equal("Dungeon Heart", snapshot.TooltipName);
        Assert.StartsWith("Dungeon Heart.  Health:", snapshot.TopStatus);
        Assert.DoesNotContain("%37", snapshot.TopStatus);
        RoomInstance portal = RoomIndex.Build(world, database).First(r => r.Definition.TypeId == RoomTypeId.Portal);
        foreach (int tileIndex in portal.TileIndices) world.TileAtIndex(tileIndex).OwnerPlayerId = PlayerIds.FirstKeeper;
        var portalCapture = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        HoverTextSnapshot portalText = portalCapture.Capture(world, new(WorldHoverKind.Tile, world.TilePosition(portal.TileIndices[0])));
        Assert.Equal("Portal", portalText.TooltipName);
        Assert.Contains("Portals owned: 1.", portalText.TopStatus);
        Assert.DoesNotContain("%43", portalText.TopStatus);
        CreatureState imp = world.SpawnCreature(database, database.CreatureByName("Imp")!.CreatureId,
            PlayerIds.FirstKeeper, hit.Tile);
        imp.Dk2StateId = 8; imp.UtilityNewState = 14;
        var simulation = new GameSimulation(database, world, seed: 7);
        ulong before = simulation.StateHash();
        var creatureHit = new WorldHoverHit(WorldHoverKind.Creature, hit.Tile, imp.Id);
        HoverTextSnapshot creature = capture.Capture(world, creatureHit, "Delver");
        Assert.Contains("Name: Delver", creature.TopStatus);
        Assert.Contains("Job: " + strings.Resolve(2600), creature.TopStatus);
        Assert.Contains("Efficiency: 80%", creature.TopStatus);
        Assert.Contains("Mood: Happy", creature.TopStatus);
        Assert.Equal(before, simulation.StateHash());
        Assert.Same(creature.TopStatus, capture.Capture(world, creatureHit, "Delver").TopStatus);
    }

    [Fact]
    public void LocalizedTokensAreParsedWholeAndReplacementTextIsNeverReparsed()
    {
        Assert.Equal("Imp.  Name: %30.  Efficiency: 80%.", WorldHoverText.Substitute(
            "%30.  Name: %29.  Efficiency: %74%.",
            new Dictionary<int, string> { [30] = "Imp", [29] = "%30", [74] = "80" }));
    }

    [InstallFact]
    public void UnknownTilesNeverRevealCreatureStatusAndNonOwnedRoomsUseTheOriginalWarning()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        WorldState world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: true);
        Dk2StringTable strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        RoomInstance portal = RoomIndex.Build(world, database).First(r => r.Definition.TypeId == RoomTypeId.Portal);
        TilePos tile = world.TilePosition(portal.TileIndices[0]);
        world.Tile(tile).ClearExploredBy(PlayerIds.FirstKeeper);
        var fog = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        HoverTextSnapshot hidden = fog.Capture(world, new(WorldHoverKind.Creature, tile, 999));
        Assert.Equal("Fog of War", hidden.TooltipName);
        Assert.Equal(strings.Resolve(2473), hidden.TopStatus);
        world.Tile(tile).SetExploredBy(PlayerIds.FirstKeeper);
        var visible = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        HoverTextSnapshot neutral = visible.Capture(world, new(WorldHoverKind.Tile, tile));
        Assert.Equal("Portal", neutral.TooltipName);
        Assert.Equal(strings.Resolve(2471), neutral.TopStatus);
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 0, 200)]
    [InlineData(0, 500, 0)]
    [InlineData(-5, 100, -4)]
    public void StructureHealthIsTheFixedPointRatioTruncatedTowardZero(int health, int max, int expected)
    {
        // 0x004A68C0/0x004A9B70: (100<<12)/(max<<12) in 12.12, times health, >>12 rounding toward zero.
        // -5 * floor(409600/100) = -20480 is exactly -5 in 12.12; truncation shows on -4.something only.
        Assert.Equal(expected == -4 ? -5 : expected, WorldHoverText.HealthPercent(health, max));
    }

    [InstallFact]
    public void DoorAndTrapStatusFollowTheirOwnFormatters_99PercentAndDoubledFullStopUnconfirmedOnScreenQ624()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        PlayerId me = PlayerIds.FirstKeeper;
        byte wall = db.Terrain.Values.Single(t => t.Name == "Reinforced Wall").TerrainId;
        byte path = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        for (int x = 9; x <= 19; x++)
            for (int y = 9; y <= 11; y++)
            {
                world.Tile(x, y).TerrainId = y == 10 ? path : wall;
                world.Tile(x, y).OwnerPlayerId = me.Value;
            }
        RoomRules.Place(db, ref world.Tile(20, 10), db.Room(RoomTypeId.Treasury), me);
        world.Tile(20, 10).GoldHeld = 1_000_000;
        byte wooden = db.Doors.Values.Single(d => d.Name == "Wooden Door").DoorId;
        byte barricade = db.Doors.Values.First(d => d.Flags.HasFlag(DoorFlags.Barricade) && !d.Flags.HasFlag(DoorFlags.Hero)).DoorId;
        TrapDef sentry = db.Traps.Values.Single(t => t.Name == "Sentry Trap");
        byte guardPost = db.Traps.Values.Single(t => (t.Flags & TrapRules.GuardPostBranch) != 0).TrapId;
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceDoor(world, db, new TilePos(10, 10), me, wooden));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceDoor(world, db, new TilePos(12, 10), me, barricade));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(14, 10), me, sentry.TrapId));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(16, 10), me, guardPost));
        DoorState door = world.Doors[0], bar = world.Doors[1];
        TrapState trap = world.Traps[0], post = world.Traps[1];
        var strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        HoverTextSnapshot Status(PlayerId viewer, WorldHoverKind kind, int id, int x) =>
            new WorldHoverText(db, strings.Resolve, viewer).Capture(world, new(kind, new TilePos(x, 10), id));

        HoverTextSnapshot blueprint = Status(me, WorldHoverKind.Door, door.Id, 10);
        Assert.Equal(("Wooden Door Blueprint.  Awaiting construction.", "Wooden Door"), (blueprint.TopStatus, blueprint.TooltipName));
        Assert.Equal("Sentry Trap Blueprint.  Awaiting construction.", Status(me, WorldHoverKind.Trap, trap.Id, 14).TopStatus);

        door.UnderConstruction = bar.UnderConstruction = trap.UnderConstruction = post.UnderConstruction = false;
        // Full health is not always 100%: the 12.12 ratio truncates, so 2000-health doors read 99%
        // (IDIV at 0x004419B7). Read from the binary; not yet compared with the original on screen.
        // Templates 2532/2527 end "%72." / "%71." and the inserted sentences end in "." too: "..".
        // Both the 99% and the ".." are read from the code and pinned here unobserved (Q624).
        Assert.Equal(2000, db.Doors[wooden].Health);
        Assert.Equal("Wooden Door.  Health: 99%.  Opens for your creatures only..", Status(me, WorldHoverKind.Door, door.Id, 10).TopStatus);
        door.LockMode = 3;
        door.Health = db.Doors[wooden].Health / 3;
        Assert.Equal($"Wooden Door.  Health: {WorldHoverText.HealthPercent(door.Health, db.Doors[wooden].Health)}%.  Locked to all creatures..",
            Status(me, WorldHoverKind.Door, door.Id, 10).TopStatus);
        Assert.Equal($"Barricade.  Will block this space until destroyed.  Health: {WorldHoverText.HealthPercent(bar.Health, db.Doors[barricade].Health)}%.",
            Status(me, WorldHoverKind.Door, bar.Id, 12).TopStatus);
        Assert.Equal($"Sentry Trap.  Health: {WorldHoverText.HealthPercent(trap.Health, sentry.Health)}%.  Mana Drain: {sentry.ManaUpkeepPerSecond}.  Mana To Fire: {sentry.ManaCostToFire}.  Concealed until triggered..",
            Status(me, WorldHoverKind.Trap, trap.Id, 14).TopStatus);
        Assert.Equal(strings.Resolve(2538), Status(me, WorldHoverKind.Trap, post.Id, 16).TopStatus);

        // Another keeper's door or trap reads only its name (the %s arm), never 2540/2542. The Hand's
        // picker never reaches this arm: 0x005992B0 gives hit kind 5/7 only to the viewer's own door
        // or trap (thing +0x28), so this pins the formatter alone.
        HoverTextSnapshot enemyDoor = Status(PlayerIds.Good, WorldHoverKind.Door, door.Id, 10);
        Assert.Equal(("Wooden Door", "Wooden Door"), (enemyDoor.TopStatus, enemyDoor.TooltipName));
        Assert.Equal("Sentry Trap", Status(PlayerIds.Good, WorldHoverKind.Trap, trap.Id, 14).TopStatus);
    }

    [InstallFact]
    public void ADeadBodyFormatsItsLevelAndType_UnreachableByTheHandAndPercent29GuessedEmptyQ634_AndTheCasinoLeverReadsItsRoomsPayout()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        var strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        CreatureState body = world.SpawnCreature(db, db.CreatureByName("Warlock")!.CreatureId, PlayerIds.FirstKeeper, new TilePos(11, 11));
        body.Level = 3;
        body.Dk2StateId = -1;
        Assert.True(CreatureDeathRules.IsCorpse(body));
        HoverTextSnapshot dead = new WorldHoverText(db, strings.Resolve, PlayerIds.FirstKeeper)
            .Capture(world, new(WorldHoverKind.Creature, body.Tile, body.Id));
        // GUESS (Q634): for an ordinary creature 0x004B1740 never writes the %29 buffer, so the two
        // spaces after "of" are ours, not observed; and no Hand pick reaches a dead body (0x005992B0).
        Assert.Equal(("It's the dead body of  a level 3 Warlock.", "Warlock"), (dead.TopStatus, dead.TooltipName));

        for (int x = 20; x <= 22; x++)
            RoomRules.Place(db, ref world.Tile(x, 20), db.Room(RoomTypeId.Casino), PlayerIds.FirstKeeper);
        ObjectState lever = world.SpawnObject(db, AutoFurnitureRules.CasinoLeverObjectId, PlayerIds.FirstKeeper, world.TileIndex(new TilePos(21, 20)));
        var capture = new WorldHoverText(db, strings.Resolve, PlayerIds.FirstKeeper);
        Assert.Equal(strings.Resolve(2508), capture.Capture(world, new(WorldHoverKind.Object, new TilePos(21, 20), lever.Id)).TopStatus);
        lever.Setting = 1;
        Assert.Equal(strings.Resolve(2509), new WorldHoverText(db, strings.Resolve, PlayerIds.FirstKeeper)
            .Capture(world, new(WorldHoverKind.Object, new TilePos(21, 20), lever.Id)).TopStatus);
    }

    [Theory]
    [InlineData(1, 0, 2599)]
    [InlineData(8, 14, 2600)]
    [InlineData(-1, 0, 2599)]
    public void WalkingDisplaysTheDestinationStatesJob(int state, int resume, int expected)
    {
        Assert.Equal(expected, WorldHoverText.ActionStringId(state, resume));
    }

    [Fact]
    public void WorldTooltipWaitsForTheSameTargetAndCanHideOnlyCreatureNames()
    {
        var delay = new HoverTooltipDelay();
        var creature = new WorldHoverHit(WorldHoverKind.Creature, new TilePos(2, 3), 4);
        var snapshot = new HoverTextSnapshot("status", "Imp", creature);
        Assert.False(delay.Visible(snapshot, 100));
        Assert.False(delay.Visible(snapshot, 1600));
        Assert.True(delay.Visible(snapshot, 1601));
        Assert.False(delay.Visible(snapshot, 1700, showCreatureTooltip: false));
        Assert.Equal("status", snapshot.TopStatus);
        Assert.False(delay.Visible(snapshot with { Hit = creature with { EntityId = 5 } }, 1800));
        Assert.False(delay.Visible(HoverTextSnapshot.Empty, 5000));
    }
}
