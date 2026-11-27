// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The door update's last arm (<c>0x004A64A3</c>): an open Secret Door is seen by an enemy
/// creature within four tiles that may look for a fight and has a clear line, which makes the door
/// its forced target and clears the tile's secret-door marker.</summary>
public sealed class SecretDoorSeenTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = Keeper + 1;
    private static readonly TilePos DoorTile = new TilePos(4, 2);
    private static readonly TilePos Beside = new TilePos(3, 2);
    private static readonly TilePos CorridorEnd = new TilePos(9, 2);
    private const byte SecretDoorId = 5;
    private const byte PlainDoorId = 6;
    private const int SeeingRangeTiles = 4;

    private static (GameSimulation Sim, WorldState World, DoorState Door, ListEventSink Events) Start()
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var db = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values, fixture.Creatures.Values,
            fixture.Variables, objects: fixture.Objects.Values,
            doors:
            [
                new DoorDef { DoorId = SecretDoorId, Name = "test secret door", Flags = DoorFlags.Secret,
                    Health = 400, GoldCost = 0, ManufactureRequired = 0 },
                new DoorDef { DoorId = PlainDoorId, Name = "test plain door",
                    Health = 400, GoldCost = 0, ManufactureRequired = 0 },
            ]);
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper, Rival]);
        // The door, and a corridor running east from it for the cases that need distance.
        for (int x = DoorTile.X; x <= CorridorEnd.X; x++)
        {
            ref TileState tile = ref world.Tile(x, DoorTile.Y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Rival;
        }
        PlaceDoor(world, db, DoorTile, SecretDoorId);
        var sim = new GameSimulation(db, world, 99, [new DoorSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, world, world.Doors[0], events);
    }

    private static void PlaceDoor(WorldState world, GameDatabase db, TilePos at, byte doorId)
    {
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        {
            X = at.X, Y = at.Y, PlayerId = Rival, DoorId = doorId, Unknown_0011 = new byte[3],
        }]);
    }

    private static CreatureState Goblin(GameSimulation sim, byte owner, TilePos at)
    {
        return sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, owner, at).Unstaggered();
    }

    [Fact]
    public void AnEnemyBesideAnOpenSecretDoorSeesItAndIsForcedOntoIt()
    {
        (GameSimulation? sim, WorldState? world, DoorState? door, ListEventSink? events) = Start();
        Goblin(sim, Rival, DoorTile); // the owner's creature on the tile holds the door open
        CreatureState enemy = Goblin(sim, Keeper, Beside);
        Assert.True(world.Tile(DoorTile).SecretDoor);

        sim.Tick();

        Assert.True(door.Open);
        Assert.False(world.Tile(DoorTile).SecretDoor);
        Assert.True(enemy.AttackAbnormalTargets);
        Assert.Equal((door.Id, FightTargetKind.Door), (enemy.AttackingId, enemy.AttackingKind));
        Assert.Single(events.Events, e => e.Kind == "secret_door_seen");
    }

    [Fact]
    public void AShutSecretDoorStaysHidden()
    {
        (GameSimulation? sim, WorldState? world, DoorState? door, ListEventSink? events) = Start();
        CreatureState enemy = Goblin(sim, Keeper, Beside);

        sim.Tick();

        Assert.False(door.Open);
        Assert.True(world.Tile(DoorTile).SecretDoor);
        Assert.False(enemy.AttackAbnormalTargets);
        Assert.DoesNotContain(events.Events, e => e.Kind == "secret_door_seen");
    }

    [Fact]
    public void TheOwnersOwnCreatureRevealsNothingAndTakesNoTarget()
    {
        (GameSimulation? sim, WorldState? world, DoorState? door, ListEventSink _) = Start();
        CreatureState own = Goblin(sim, Rival, DoorTile);

        sim.Tick();

        Assert.True(door.Open);
        Assert.True(world.Tile(DoorTile).SecretDoor);
        Assert.False(own.AttackAbnormalTargets);
    }

    /// <summary>The gate is <c>0x0045A6D0(0)</c>, unforced, so a creature in the hand sees nothing;
    /// the same gate is what keeps a possessed creature from revealing a door.</summary>
    [Fact]
    public void AnEnemyThatMayNotLookForAFightSeesNothing()
    {
        (GameSimulation? sim, WorldState? world, DoorState _, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        CreatureState enemy = Goblin(sim, Keeper, Beside);
        enemy.InHand = true;

        sim.Tick();

        Assert.True(world.Tile(DoorTile).SecretDoor);
        Assert.False(enemy.AttackAbnormalTargets);
    }

    /// <summary>A body stays in the creature list with no state, where DK2 keeps it as another class
    /// of thing: it sees nothing, and the arm must not ask its state for a category.</summary>
    [Fact]
    public void AnEnemyBodyBesideTheDoorSeesNothing()
    {
        (GameSimulation? sim, WorldState? world, DoorState _, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        CreatureState body = Goblin(sim, Keeper, Beside);
        CreatureDeathRules.BecomeCorpse(world, sim.Database, body);

        sim.Tick();

        Assert.True(world.Tile(DoorTile).SecretDoor);
        Assert.False(body.AttackAbnormalTargets);
    }

    /// <summary>The arm skips a creature whose state resolves to the combat category
    /// (<c>0x00478050</c> giving <c>0x16</c>), so one already fighting is not pulled onto the door.</summary>
    [Fact]
    public void AnEnemyAlreadyInCombatIsLeftToItsFight()
    {
        (GameSimulation? sim, WorldState? world, DoorState _, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        CreatureState enemy = Goblin(sim, Keeper, Beside);
        enemy.Dk2StateId = CreatureStateTable.InCategory(CreatureStateCategory.Combat).First();

        sim.Tick();

        Assert.True(world.Tile(DoorTile).SecretDoor);
        Assert.False(enemy.AttackAbnormalTargets);
    }

    /// <summary>The range is strict (<c>0x004A6567</c>, <c>JGE</c>): a creature exactly four tiles
    /// from the door's centre sees nothing, and one position unit nearer sees it.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void TheDoorIsSeenStrictlyInsideFourTiles(int unitsInsideTheRange, bool seen)
    {
        (GameSimulation? sim, WorldState? world, DoorState _, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        var fourTilesEast = new TilePos(DoorTile.X + SeeingRangeTiles, DoorTile.Y);
        CreatureState enemy = Goblin(sim, Keeper, fourTilesEast);
        enemy.PosX -= unitsInsideTheRange;

        sim.Tick();

        Assert.Equal(seen, enemy.AttackAbnormalTargets);
        Assert.Equal(!seen, world.Tile(DoorTile).SecretDoor);
    }

    /// <summary>The arm asks for its sight line with both flags set (<c>0x004A659D</c>), and then only
    /// a solid tile stops it: a shut door standing between the creature and the Secret Door does not.</summary>
    [Fact]
    public void AShutDoorInBetweenDoesNotHideTheSecretDoor()
    {
        (GameSimulation? sim, WorldState? world, DoorState? secret, ListEventSink _) = Start();
        var between = new TilePos(DoorTile.X + 2, DoorTile.Y);
        var beyond = new TilePos(DoorTile.X + 3, DoorTile.Y);
        PlaceDoor(world, sim.Database, between, PlainDoorId);
        Goblin(sim, Rival, DoorTile);
        CreatureState enemy = Goblin(sim, Keeper, beyond);

        sim.Tick();

        Assert.False(world.DoorAt(between.X, between.Y)!.Open);
        Assert.False(world.Tile(DoorTile).SecretDoor);
        Assert.Equal((secret.Id, FightTargetKind.Door), (enemy.AttackingId, enemy.AttackingKind));
    }

    private static byte NavByteOfTheDoor(GameSimulation sim)
    {
        var navigation = new WorldSteering(sim.Database, sim.World);
        navigation.Refresh();
        return navigation.NavByte(DoorTile.X, DoorTile.Y);
    }

    private const byte Floor = 0;
    private const byte ShutDoorOfTheRival = Rival << 4;

    /// <summary>Seeing the door writes its navigation byte as a shut door's although it stands open
    /// (<c>0x004A66F0(1)</c> at <c>0x004A6625</c>), with the secret bit already cleared; an open door
    /// nobody has seen is plain floor.</summary>
    [Fact]
    public void ASeenDoorReadsAsShutInTheNavigationBytesWhileItStandsOpen()
    {
        (GameSimulation? sim, WorldState _, DoorState? door, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        sim.Tick();
        Assert.True(door.Open);
        Assert.Equal(Floor, NavByteOfTheDoor(sim));

        Goblin(sim, Keeper, Beside);
        sim.Tick();

        Assert.True(door.Open);
        Assert.Equal(ShutDoorOfTheRival, NavByteOfTheDoor(sim));
    }

    /// <summary>Only a change of the open state writes the byte afresh (<c>0x004A6081</c>): the door
    /// shuts and opens again, with nobody left to see it, and its tile is floor once more.</summary>
    [Fact]
    public void ASeenDoorIsFloorAgainOnceItHasShutAndOpened()
    {
        (GameSimulation? sim, WorldState? world, DoorState? door, ListEventSink _) = Start();
        CreatureState owner = Goblin(sim, Rival, DoorTile);
        CreatureState enemy = Goblin(sim, Keeper, Beside);
        sim.Tick();
        Assert.True(door.SeenWhileOpen);

        // The enemy leaves for the far end of the corridor and the owner steps away: the door shuts.
        enemy.PosX = CreatureState.CentreOf(CorridorEnd.X);
        owner.PosX = CreatureState.CentreOf(CorridorEnd.X - 1);
        sim.Tick();
        Assert.False(door.Open);

        owner.PosX = CreatureState.CentreOf(DoorTile.X);
        sim.Tick();

        Assert.True(door.Open);
        Assert.False(door.SeenWhileOpen);
        Assert.Equal(Floor, NavByteOfTheDoor(sim));
    }

    /// <summary>Sight is stopped by what the navigation byte says, not by the door's open state
    /// (<c>0x004511A0</c> through <c>0x004528E0</c>): a seen door stops the ordinary sight line though
    /// it stands open, and still not the line that only solid tiles stop.</summary>
    [Fact]
    public void ASeenOpenDoorStopsOrdinarySight()
    {
        (GameSimulation? sim, WorldState? world, DoorState? door, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        var navigation = new WorldSteering(sim.Database, world);
        var sight = new WorldSight(navigation, world, sim.Database);
        sim.Tick();
        navigation.Refresh();
        Assert.False(sight.Blocks(DoorTile.X, DoorTile.Y));

        Goblin(sim, Keeper, Beside);
        sim.Tick();
        navigation.Refresh();

        Assert.True(door.Open);
        Assert.True(sight.Blocks(DoorTile.X, DoorTile.Y));
        Assert.False(sight.SolidOnly.Blocks(DoorTile.X, DoorTile.Y));
    }

    /// <summary>Nothing writes the byte without a change of the open state, so the door stays seen
    /// after the enemy has gone, for as long as it stands open.</summary>
    [Fact]
    public void ASeenDoorStaysSeenAfterTheEnemyLeavesWhileItStandsOpen()
    {
        (GameSimulation? sim, WorldState _, DoorState? door, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        CreatureState enemy = Goblin(sim, Keeper, Beside);
        sim.Tick();

        enemy.PosX = CreatureState.CentreOf(CorridorEnd.X);
        sim.Tick();

        Assert.True(door.Open);
        Assert.True(door.SeenWhileOpen);
        Assert.Equal(ShutDoorOfTheRival, NavByteOfTheDoor(sim));
    }

    [Fact]
    public void TheSeenMarkSurvivesASnapshot()
    {
        (GameSimulation? sim, WorldState _, DoorState? door, ListEventSink _) = Start();
        Goblin(sim, Rival, DoorTile);
        Goblin(sim, Keeper, Beside);
        sim.Tick();
        Assert.True(door.SeenWhileOpen);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database);

        Assert.True(restored.World.Doors[0].SeenWhileOpen);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    /// <summary>The door is forced again every tick, marker or no marker (<c>0x004A65BF</c> is
    /// unconditional), so an enemy arriving after the door was seen is still drawn to it.</summary>
    [Fact]
    public void ADoorAlreadySeenStillForcesALaterEnemy()
    {
        (GameSimulation? sim, WorldState? world, DoorState? door, ListEventSink? events) = Start();
        Goblin(sim, Rival, DoorTile);
        world.Tile(DoorTile).SecretDoor = false;
        CreatureState enemy = Goblin(sim, Keeper, Beside);

        sim.Tick();

        Assert.Equal((door.Id, FightTargetKind.Door), (enemy.AttackingId, enemy.AttackingKind));
        Assert.DoesNotContain(events.Events, e => e.Kind == "secret_door_seen");
    }
}
