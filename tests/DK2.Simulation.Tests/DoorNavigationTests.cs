// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
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

    /// <summary>An alliance is what the door update asks about, not ownership: the same rival
    /// creature that a keeper's door ignores opens it once the two are allied, and is shut out
    /// again the moment the pact breaks (<c>0x004BF3D0</c>, doors-and-traps.md).</summary>
    [Fact]
    public void AnAllyOpensTheDoorAndBreakingThePactShutsIt()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, 0);
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Rival, new TilePos(5, 2));
        var sim = new GameSimulation(db, world, 7, Dk2TerrainFixture.DefaultSystems());

        sim.Tick(InputFrame.Empty);
        Assert.False(door.Open);

        sim.Tick(InputFrame.Of(new SetAllianceCommand(Keeper, Rival, true)));
        Assert.True(door.Open);

        sim.Tick(InputFrame.Of(new SetAllianceCommand(Rival, Keeper, false)));
        Assert.False(door.Open);
    }

    /// <summary>Both handlers write both players' arrays, so there is no one-sided alliance
    /// (<c>0x00513FF0</c>, <c>0x00513F60</c>).</summary>
    [Fact]
    public void AnAllianceIsSymmetricWhicheverKeeperAsks()
    {
        (WorldState world, _, _) = Build(Keeper, 0);
        Assert.False(world.AreAllied(Keeper, Rival));

        world.SetAllied(Rival, Keeper, true);

        Assert.True(world.AreAllied(Keeper, Rival));
        Assert.True(world.AreAllied(Rival, Keeper));
        Assert.True(world.AreAllied(Keeper, Keeper));
    }

    /// <summary>The lock command has no mode argument: it sends an unlocked door to 3 and a door
    /// at any other mode to 0 (<c>0x0051382D</c>), which is why 1 and 2 are unreachable.</summary>
    [Fact]
    public void TheOwnersLockCommandTogglesAndAnotherKeepersDoesNothing()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, 0);
        var sim = new GameSimulation(db, world, 7, Dk2TerrainFixture.DefaultSystems());
        Assert.Equal(0, door.LockMode);

        sim.Tick(InputFrame.Of(new LockDoorCommand(Keeper, new TilePos(5, 2))));
        Assert.Equal(3, door.LockMode);

        sim.Tick(InputFrame.Of(new LockDoorCommand(Rival, new TilePos(5, 2))));
        Assert.Equal(3, door.LockMode);

        sim.Tick(InputFrame.Of(new LockDoorCommand(Keeper, new TilePos(5, 2))));
        Assert.Equal(0, door.LockMode);
    }

    /// <summary>A picked lock is read before the lock mode and before anyone is looked for, so a
    /// door locked against everybody stands open until the timer runs out (<c>0x004A5D52</c>).
    /// Nothing in this project starts one — the only writer is an ability effect
    /// (doors-and-traps.md) — so the test sets it the way that effect would.</summary>
    [Fact]
    public void APickedLockHoldsALockedDoorOpenUntilItRunsOut()
    {
        (WorldState world, GameDatabase db, DoorState door) = Build(Keeper, flags: 0x01);
        var sim = new GameSimulation(db, world, 7, Dk2TerrainFixture.DefaultSystems());
        door.LockPickTicks = 2;

        sim.Tick(InputFrame.Empty);
        Assert.Equal(1, door.LockPickTicks);
        Assert.True(door.Open);

        sim.Tick(InputFrame.Empty);
        Assert.Equal(0, door.LockPickTicks);
        Assert.False(door.Open);
    }
}
