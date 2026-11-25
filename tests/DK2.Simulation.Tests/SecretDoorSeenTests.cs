// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The door update's last arm (<c>0x004A64A3</c>): an open Secret Door is seen by an enemy
/// creature within four tiles that may look for a fight and has a clear line, which makes the door
/// its forced target and clears the tile's secret-door marker.</summary>
public sealed class SecretDoorSeenTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = Keeper + 1;
    private static readonly TilePos DoorTile = new(4, 2);
    private static readonly TilePos Beside = new(3, 2);

    private static (GameSimulation Sim, WorldState World, DoorState Door, ListEventSink Events) Start()
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var db = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values, fixture.Creatures.Values,
            fixture.Variables, objects: fixture.Objects.Values,
            doors: [new DoorDef { DoorId = 5, Name = "test secret door", Flags = DoorFlags.Secret,
                Health = 400, GoldCost = 0, ManufactureRequired = 0 }]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper, Rival]);
        ref TileState tile = ref world.Tile(DoorTile);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        {
            X = DoorTile.X, Y = DoorTile.Y, PlayerId = Rival, DoorId = 5, Unknown_0011 = new byte[3],
        }]);
        var sim = new GameSimulation(db, world, 99, [new DoorSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, world, world.Doors[0], events);
    }

    private static CreatureState Goblin(GameSimulation sim, byte owner, TilePos at) =>
        sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, owner, at).Unstaggered();

    [Fact]
    public void AnEnemyBesideAnOpenSecretDoorSeesItAndIsForcedOntoIt()
    {
        var (sim, world, door, events) = Start();
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
        var (sim, world, door, events) = Start();
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
        var (sim, world, door, _) = Start();
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
        var (sim, world, _, _) = Start();
        Goblin(sim, Rival, DoorTile);
        CreatureState enemy = Goblin(sim, Keeper, Beside);
        enemy.InHand = true;

        sim.Tick();

        Assert.True(world.Tile(DoorTile).SecretDoor);
        Assert.False(enemy.AttackAbnormalTargets);
    }

    /// <summary>The door is forced again every tick, marker or no marker (<c>0x004A65BF</c> is
    /// unconditional), so an enemy arriving after the door was seen is still drawn to it.</summary>
    [Fact]
    public void ADoorAlreadySeenStillForcesALaterEnemy()
    {
        var (sim, world, door, events) = Start();
        Goblin(sim, Rival, DoorTile);
        world.Tile(DoorTile).SecretDoor = false;
        CreatureState enemy = Goblin(sim, Keeper, Beside);

        sim.Tick();

        Assert.Equal((door.Id, FightTargetKind.Door), (enemy.AttackingId, enemy.AttackingKind));
        Assert.DoesNotContain(events.Events, e => e.Kind == "secret_door_seen");
    }
}
