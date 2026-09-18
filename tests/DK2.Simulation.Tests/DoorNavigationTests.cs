// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Doors in the navigation byte (ADR-0158; navigation-mesh-and-search.md §4,
/// doors-and-traps.md): a corridor east of the fixture's room with a door at (5,2), walls north
/// and south of it, and a target beyond.</summary>
public class DoorNavigationTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private static readonly TilePos Beyond = new(7, 2);

    private static (WorldState world, GameDatabase db, DoorState door) Build(byte doorOwner, byte flags, byte doorId = 1)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        foreach (int x in new[] { 4, 5, 6, 7 })
        {
            ref TileState tile = ref world.Tile(new TilePos(x, 2));
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.Health = db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }

        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 5, Y = 2, PlayerId = doorOwner, Flags = flags, DoorId = doorId, Unknown_0011 = new byte[3] }]);
        return (world, db, world.Doors[0]);
    }

    private static bool Routes(WorldState world, GameDatabase db, byte owner, int options)
    {
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, owner, new TilePos(2, 2));
        return new WorldSteering(db, world).TryRoute(imp, Beyond, options);
    }

    [Fact]
    public void ItsOwnerRoutesThroughAClosedUnlockedDoor()
    {
        (WorldState world, GameDatabase db, _) = Build(Keeper, 0);
        Assert.True(Routes(world, db, Keeper, MoveOptions.Loose));
    }

    [Fact]
    public void ARivalRespectingDoorsIsStoppedAndOneAttackingIsNot()
    {
        (WorldState world, GameDatabase db, _) = Build(Keeper, 0);
        Assert.False(Routes(world, db, Rival, MoveOptions.Loose));
        Assert.True(Routes(world, db, Rival, MoveOptions.Plain));
    }

    /// <summary>The byte writer sets the lock bit for a definition with flag bit 6 whatever the
    /// lock mode (`0x004A66F0`), and the Barricade record alone among the keeper's doors carries
    /// it: unlocked, it still stops its owner, and a rival attacking it (word 0) still passes.</summary>
    [Fact]
    public void ABarricadeIsLockedForItsOwnerWhateverItsLockMode()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, 0, doorId: 6);
        Assert.Equal(0, door.LockMode);
        Assert.False(Routes(world, db, Keeper, MoveOptions.Loose));
        Assert.True(Routes(world, db, Keeper, MoveOptions.Leaving));
        Assert.True(Routes(world, db, Rival, MoveOptions.Plain));
    }

    [Fact]
    public void AnOpenDoorIsFloorForEveryone()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, 0);
        door.Open = true;
        Assert.True(Routes(world, db, Rival, MoveOptions.Loose));
    }

    [Fact]
    public void ALockedDoorStopsItsOwnerUnlessTheWordPassesOwnLockedDoors()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, flags: 0x01);
        Assert.Equal(3, door.LockMode);
        Assert.False(Routes(world, db, Keeper, MoveOptions.Loose));
        Assert.True(Routes(world, db, Keeper, MoveOptions.Leaving));
    }

    [Fact]
    public void TheDoorOpensForAnAlliedCreatureBesideItAndNotForARival()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, 0);
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Rival, new TilePos(4, 2));
        var sim = new GameSimulation(db, world, 7, Dk2TerrainFixture.DefaultSystems());
        sim.Tick(InputFrame.Empty);
        Assert.False(door.Open);

        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(6, 2));
        sim.Tick(InputFrame.Empty);
        Assert.True(door.Open);
    }

    [Fact]
    public void ALockedDoorNeverOpens()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, flags: 0x01);
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(5, 2));
        var sim = new GameSimulation(db, world, 7, Dk2TerrainFixture.DefaultSystems());
        sim.Tick(InputFrame.Empty);
        Assert.False(door.Open);
    }
}
