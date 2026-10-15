// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Doors met, fought and lost (door-blocking.md, Q453): a wall down column 10 of an open
/// map with one door in it at (10,10), its walls north and south.</summary>
public class DoorCombatTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte WoodenDoor = 1;
    private const byte Barricade = 6;
    private static readonly TilePos DoorTile = new(10, 10);
    private static readonly TilePos West = new(6, 10);
    private static readonly TilePos East = new(14, 10);

    private static (GameDatabase db, WorldState world) Walled(byte doorOwner, byte flags = 0, byte doorId = WoodenDoor)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = x == DoorTile.X ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath;
            }
        }
        ref TileState door = ref world.Tile(DoorTile);
        door.TerrainId = Dk2TerrainFixture.ClaimedPath;
        door.OwnerPlayerId = doorOwner;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = DoorTile.X, Y = DoorTile.Y, PlayerId = doorOwner, Flags = flags, DoorId = doorId, Unknown_0011 = new byte[3] }]);
        return (db, world);
    }

    private static WorldSteering Routes(GameDatabase db, WorldState world) => new(db, world);

    private static GameSimulation Sim(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, 1,
        [
            new DoorSystem(), new CreatureStatusSystem(), new CombatSystem(), new CreatureMovementSystem(),
            new DoorHealthSystem(), new ShotSystem(), new DoorRemovalSystem(),
        ]);
        sim.ObserveWith(sink);
        return sim;
    }

    [Fact]
    public void AHostileGoblinMeetsALockedRivalDoor_FightsItDown_ThenWalksThrough()
    {
        (GameDatabase db, WorldState world) = Walled(Rival, flags: 0x01);
        DoorState door = world.Doors[0];
        Assert.Equal(3, door.LockMode);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, West);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);
        var steering = new WorldSteering(db, world);
        var routes = new WorldSteering(db, world);

        // A foreign door is routed through by a word without the door rule (0x80), refused by one with it.
        Assert.False(steering.TryRoute(world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, West), East, MoveOptions.Loose));
        world.RemoveCreature(world.Creatures[^1].Id);
        Assert.True(routes.TryRoute(goblin, East, MoveOptions.Plain));

        for (int i = 0; i < 400 && world.Doors.Count > 0; i++) sim.Run(1);

        Assert.Contains(sink.Events, e => e.Kind == "meets_door" && e.Detail.Contains($"door={door.Id}"));
        Assert.Contains(sink.Events, e => e.Kind == "door_hit");
        Assert.Contains(sink.Events, e => e.Kind == "door_destroyed" && e.Detail.Contains($"door={door.Id}"));
        Assert.Empty(world.Doors);
        Assert.True(goblin.Tile.X < DoorTile.X, "it fought from its own side");

        // Gone, the doorway is floor: the next tick lets the fight go, and the door rule no longer refuses.
        sim.Run(2);
        Assert.False(goblin.AttackAbnormalTargets);
        Assert.NotEqual(CreatureActivity.Fighting, goblin.Activity);
        routes.Refresh();
        Assert.True(routes.TryRoute(goblin, East, MoveOptions.Loose));
        for (int i = 0; i < 200 && goblin.Tile != East; i++) sim.Run(1);
        Assert.Equal(East, goblin.Tile);
    }

    /// <summary>A caster forced on a door casts at it from range, as the forced arm never takes the
    /// within-a-tile melee rule, and its shots strike the door (the heart's stand-in, ours).</summary>
    [Fact]
    public void AWarlockMeetingARivalDoorCastsAtIt_AndItsShotsBreakTheDoor()
    {
        (GameDatabase db, WorldState world) = Walled(Rival);
        CreatureState warlock = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, West);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);
        Assert.True(Routes(db, world).TryRoute(warlock, East, MoveOptions.Plain));

        for (int i = 0; i < 1200 && world.Doors.Count > 0; i++) sim.Run(1);

        Assert.Contains(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains($"creature={warlock.Id}")
            && e.Detail.Contains($"attack={(byte)CombatRules.AttackMode.SpellAtThing}"));
        Assert.Contains(sink.Events, e => e.Kind == "shot_door_hit");
        Assert.Empty(world.Doors);
    }

    /// <summary>State 89 clears <c>RebelActive</c> as it clears the heeding bit (<c>0x0047792A</c>).</summary>
    [Fact]
    public void TheIdleChooserClearsRebelActive()
    {
        (GameDatabase db, WorldState world) = Walled(Keeper);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, West);
        goblin.RebelActive = true;
        new GameSimulation(db, world, 1, [new CreatureJobSystem()]).Run(1);
        Assert.False(goblin.RebelActive);
    }

    [Fact]
    public void AnOwnCreatureRoutesThroughItsOwnDoor_ItOpens_AndNothingFightsIt()
    {
        (GameDatabase db, WorldState world) = Walled(Keeper);
        DoorState door = world.Doors[0];
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, West);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);
        Assert.True(Routes(db, world).TryRoute(goblin, East, MoveOptions.Loose));

        bool opened = false;
        for (int i = 0; i < 200 && goblin.Tile != East; i++)
        {
            sim.Run(1);
            opened |= door.Open;
        }

        Assert.Equal(East, goblin.Tile);
        Assert.True(opened);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "meets_door");
        Assert.Equal(db.Doors[WoodenDoor].Health, door.Health);
    }

    /// <summary>The hostile arm has no closed test (Q454: the owner confirms an enemy attacks an open
    /// door): a rival's ally standing in the doorway holds it open, and the goblin still stops.</summary>
    [Fact]
    public void AnOpenHostileDoorIsMetAsAShutOneIs()
    {
        (GameDatabase db, WorldState world) = Walled(Rival);
        DoorState door = world.Doors[0];
        CreatureState holder = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Rival, DoorTile);
        holder.FrozenTicks = 10000;
        StatePlacement.As(holder, CreatureActivity.Frozen);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, West);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);
        sim.Run(1);
        Assert.True(door.Open);
        Assert.True(Routes(db, world).TryRoute(goblin, East, MoveOptions.Plain));

        for (int i = 0; i < 60 && !sink.Events.Any(e => e.Kind == "meets_door"); i++) sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "meets_door");
    }

    /// <summary>A rebel attacks its own keeper's shut door, as <c>RebelActive</c> admits a door that
    /// is not hostile (<c>0x0048A36C</c>); a loyal creature with the same word walks into it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARebelMeetsItsOwnKeepersShutDoor_ALoyalCreatureDoesNot(bool rebel)
    {
        (GameDatabase db, WorldState world) = Walled(Keeper, flags: 0x01);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, West);
        goblin.RebelActive = rebel;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);
        Assert.True(Routes(db, world).TryRoute(goblin, East, MoveOptions.Leaving));

        for (int i = 0; i < 60; i++) sim.Run(1);
        Assert.Equal(rebel, sink.Events.Any(e => e.Kind == "meets_door"));
    }

    [Fact]
    public void AnImpNeverMeetsADoor()
    {
        (GameDatabase db, WorldState world) = Walled(Rival);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, West);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);
        StatePlacement.Place(imp, 1); // idle: outside the states the mover holds an imp still in
        Assert.True(Routes(db, world).TryRoute(imp, East, MoveOptions.Plain));
        for (int i = 0; i < 120 && imp.Tile != East; i++) sim.Run(1);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "meets_door");
        Assert.True(imp.Tile.X > DoorTile.X, "the imp walked through");
    }

    /// <summary>The recovery (<c>0x004A55F0</c>): past the deadline a live door gains its rate capped by
    /// what it is missing, and the deadline moves one game second whenever the rate is not zero.</summary>
    [Fact]
    public void ADoorRecoversOnceAGameSecond_CappedAtItsHealth_AndALateDoorCatchesUpOnConsecutiveTurns()
    {
        (GameDatabase db, WorldState world) = Walled(Keeper);
        DoorState door = world.Doors[0];
        door.Health = 395;
        var sim = new GameSimulation(db, world, 1, [new DoorHealthSystem()]);

        var healths = new List<int>();
        for (int i = 0; i < 12; i++)
        {
            sim.Run(1);
            healths.Add(door.Health);
        }
        // The first run is turn 0, and the deadline starts at 0: turns 1, 5 and 9 gain the
        // shipped 2, 2 and then the last 1 to the cap.
        Assert.Equal([395, 397, 397, 397, 397, 399, 399, 399, 399, 400, 400, 400], healths);
        Assert.Equal(12, door.RecoveryDeadline);

        // A deadline left behind: every turn gains until it is ahead again.
        door.Health = 300;
        door.RecoveryDeadline = 0;
        int before = door.Health;
        sim.Run(3);
        Assert.Equal(before + 6, door.Health);
    }

    [Fact]
    public void ADoorWhoseTileChangesOwnerDiesThatTurn_AndAnUnbuiltOneLeavesTheBlueprintList()
    {
        (GameDatabase db, WorldState world) = Walled(Keeper, flags: 0x02);
        DoorState door = world.Doors[0];
        Assert.True(door.UnderConstruction);
        CrateRules.Register(world, Keeper, new BlueprintRef(CrateRules.DoorKind, door.Id));
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        world.Tile(DoorTile).OwnerPlayerId = Rival;
        sim.Run(1);

        Assert.Empty(world.Doors);
        Assert.Empty(world.Player(Keeper).Blueprints);
        Assert.Contains(sink.Events, e => e.Kind == "door_owner_lost");
    }

    /// <summary>Level 4's hero door in miniature (Q600): a Good door on water, a tile the map gives
    /// the Neutral player, dies on its first update, and its removal leaves floor where the water's
    /// byte was (<c>0x004A66F0(0)</c>), as DK2's cache held at tick 500. A save keeps it; a change
    /// of terrain gives the tile its own byte again, and changing it back does not bring the floor
    /// back.</summary>
    [Fact]
    public void ADoorDyingOnWaterLeavesFloor_UntilTheTerrainChanges_AsTheZeroArmReads()
    {
        (GameDatabase db, WorldState world) = Walled(PlayerIds.Good);
        ref TileState tile = ref world.Tile(DoorTile);
        tile.TerrainId = Dk2TerrainFixture.Water;
        tile.OwnerPlayerId = PlayerIds.Neutral;
        GameSimulation sim = Sim(db, world, new ListEventSink());
        var steering = new WorldSteering(db, world);

        sim.Run(1);
        steering.Refresh();
        Assert.Empty(world.Doors);
        Assert.Equal(NavBytes.Floor, steering.NavByte(DoorTile.X, DoorTile.Y));

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new DoorRemovalSystem()]);
        Assert.Equal(world.NavStamps, copy.World.NavStamps);
        Assert.Equal(sim.StateHash(), copy.StateHash());

        world.Tile(DoorTile).TerrainId = Dk2TerrainFixture.Lava;
        sim.Run(1);
        steering.Refresh();
        Assert.Empty(world.NavStamps);
        Assert.Equal(NavBytes.Lava, steering.NavByte(DoorTile.X, DoorTile.Y));
        world.Tile(DoorTile).TerrainId = Dk2TerrainFixture.Water;
        steering.Refresh();
        Assert.Equal(NavBytes.Water, steering.NavByte(DoorTile.X, DoorTile.Y));
    }

    /// <summary>The support test (<c>0x004A5A41</c>): a wall gone from either side kills an ordinary
    /// door (the owner confirms digging one out destroys it); a barricade returns before the test.</summary>
    [Theory]
    [InlineData(WoodenDoor, true)]
    [InlineData(Barricade, false)]
    public void ADoorLosingASupportWallIsDestroyed_ABarricadeIsNot(byte doorId, bool destroyed)
    {
        (GameDatabase db, WorldState world) = Walled(Keeper, doorId: doorId);
        GameSimulation sim = Sim(db, world, new ListEventSink());
        sim.Run(1);
        Assert.Single(world.Doors);

        world.Tile(new TilePos(DoorTile.X, DoorTile.Y - 1)).TerrainId = Dk2TerrainFixture.DirtPath;
        sim.Run(1);
        Assert.Equal(destroyed, world.Doors.Count == 0);
    }

    /// <summary>The per-tile sight rule (<c>0x004511A0</c>): a door thing stops sight open or shut;
    /// with a door on the target's tile no door does, only rock.</summary>
    [Fact]
    public void ADoorStopsSightOpenOrShut_ButNotTowardATargetOnADoorTile()
    {
        (GameDatabase db, WorldState world) = Walled(Rival);
        var steering = new WorldSteering(db, world);
        steering.Refresh();
        var sight = new WorldSight(steering, world, db);
        int fromX = CreatureState.CentreOf(8), toX = CreatureState.CentreOf(12), y = CreatureState.CentreOf(10);

        Assert.False(LineOfSight.Clear(sight, fromX, y, toX, y));
        world.Doors[0].Open = true;
        Assert.False(LineOfSight.Clear(sight, fromX, y, toX, y));
        Assert.True(sight.DoorOn(DoorTile.X, DoorTile.Y));
        Assert.True(LineOfSight.Clear(sight.SolidOnly, fromX, y, CreatureState.CentreOf(10), y));
    }

    /// <summary>A door's removal leaves every other blueprint naming its own door: the list holds
    /// ids, not places in the door list.</summary>
    [Fact]
    public void AFallenDoorLeavesTheOtherBlueprintsNamingTheirOwnDoors()
    {
        (GameDatabase db, WorldState world) = Walled(Keeper, flags: 0x02);
        ref TileState second = ref world.Tile(new TilePos(10, 14));
        second.TerrainId = Dk2TerrainFixture.ClaimedPath;
        second.OwnerPlayerId = Keeper;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = 10, Y = 14, PlayerId = Keeper, Flags = 0x02, DoorId = WoodenDoor, Unknown_0011 = new byte[3] }]);
        DoorState first = world.Doors[0], other = world.Doors[1];
        CrateRules.Register(world, Keeper, new BlueprintRef(CrateRules.DoorKind, first.Id));
        CrateRules.Register(world, Keeper, new BlueprintRef(CrateRules.DoorKind, other.Id));

        first.Health = 0;
        new GameSimulation(db, world, 1, [new DoorRemovalSystem()]).Run(1);

        BlueprintRef left = Assert.Single(world.Player(Keeper).Blueprints);
        Assert.Equal(other.Id, left.Id);
        Assert.Equal((14 * world.Width) + 10, CrateRules.TileOf(world, left));
    }

    [Fact]
    public void DoorIdentityHealthAndAForcedDoorSurviveASnapshot()
    {
        (GameDatabase db, WorldState world) = Walled(Rival);
        DoorState door = world.Doors[0];
        door.Health = 123;
        door.RecoveryDeadline = 44;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, West);
        goblin.AttackAbnormalTargets = true;
        goblin.AttackingId = door.Id;
        goblin.AttackingKind = FightTargetKind.Door;
        goblin.RebelActive = true;

        var before = new GameSimulation(db, world, 1, []);
        GameSimulation after = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(before), db, []);

        DoorState restored = Assert.Single(after.World.Doors);
        Assert.Equal((door.Id, 123, 44L), (restored.Id, restored.Health, restored.RecoveryDeadline));
        CreatureState g = after.World.CreatureById(goblin.Id)!;
        Assert.Equal((door.Id, FightTargetKind.Door, true), (g.AttackingId, g.AttackingKind, g.RebelActive));
        Assert.Equal(before.StateHash(), after.StateHash());
    }

}
