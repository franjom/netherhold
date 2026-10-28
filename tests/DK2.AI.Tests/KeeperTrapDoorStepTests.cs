// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using Xunit;

namespace DK2.AI.Tests;

/// <summary>Step 10's traps and doors (<c>0x005000A0</c>, computer-keeper-management.md §9): the
/// Workshop and usage gates, the corridor-tile test <c>0x004FFA50</c>, the spacing test
/// <c>0x004FFC40</c>, and <c>0x004FFD80</c>'s choice of a door or a trap.</summary>
public class KeeperTrapDoorStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database(workshop: true);
    private const byte Keeper = KeeperWorld.Keeper;
    private const ulong Seed = 7;

    /// <summary>The heart's 5×5 floor and a 3×3 Workshop north of it at (13..15, 9..11): stored
    /// positions (18,18) for the heart (first slab plus three) and (14,10) for the Workshop.</summary>
    private static WorldState World()
    {
        WorldState world = KeeperWorld.World(Db);
        for (int y = 9; y <= 11; y++)
            for (int x = 13; x <= 15; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.WorkshopFloor, Keeper);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        return world;
    }

    /// <summary>The test world's variables have no door or trap rows to set.</summary>
    private static void Available(WorldState world, AvailabilityKind kind, int typeId) =>
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(kind, typeId, 3));

    private static KeeperAIParameters Params(byte trapVsDoor = 0, byte doorPreference = 1, byte aggressive = 0, bool boulderOnLong = false) =>
        KeeperWorld.Parameters() with
        {
            TrapAndDoorUsage = 100, TrapVsDoorPreference = trapVsDoor, DoorPreference = doorPreference,
            AggressiveVsDefensiveTrapPreference = aggressive, BoulderTrapsOnLongCorridors = boulderOnLong,
        };

    /// <summary>The box's margin as the step draws it: the Workshop, the usage roll, then <c>random(10) - 2</c>.</summary>
    private static int Margin()
    {
        var twin = new SimRandom(Seed);
        twin.NextInt(1);
        twin.NextInt(100);
        return twin.NextInt(10) - 2;
    }

    /// <summary>One open tile of the keeper's between rock west and east, on the north edge.</summary>
    private static TilePos CorridorOnNorthEdge(WorldState world)
    {
        var at = new TilePos(16, 10 - Margin());
        KeeperWorld.Set(world, Db, at, KeeperWorld.ClaimedPath, Keeper);
        return at;
    }

    private static (int Left, List<PlayerCommand> Commands) Run(WorldState world, KeeperAIParameters p, int allowance = 1)
    {
        var commands = new List<PlayerCommand>();
        var step = new KeeperTrapDoorStep(new KeeperView(Db, world, Keeper), p, new SimRandom(Seed), commands);
        return (step.Run(allowance), commands);
    }

    [Fact]
    public void A_corridor_tile_lies_between_two_solid_tiles_neither_of_them_tagged()
    {
        WorldState world = World();
        KeeperWorld.Set(world, Db, new TilePos(5, 5), KeeperWorld.ClaimedPath, Keeper);
        var step = new KeeperTrapDoorStep(new KeeperView(Db, world, Keeper), Params(), new SimRandom(Seed), []);
        Assert.True(step.ValidSite(5, 5));
        Assert.False(step.ValidSite(15, 15));    // the heart's open floor
        Assert.False(step.ValidSite(4, 5));      // rock
        Assert.False(step.ValidSite(0, 5));      // the map's edge

        world.Tile(new TilePos(4, 5)).TaggedBy = PlayerMask.None.With(Keeper);
        step = new KeeperTrapDoorStep(new KeeperView(Db, world, Keeper), Params(), new SimRandom(Seed), []);
        Assert.False(step.ValidSite(5, 5));
    }

    [Fact]
    public void Without_a_Workshop_nothing_is_drawn()
    {
        WorldState world = KeeperWorld.World(Db);
        var rng = new SimRandom(Seed);
        var commands = new List<PlayerCommand>();
        int left = new KeeperTrapDoorStep(new KeeperView(Db, world, Keeper), Params(), rng, commands).Run(3);
        Assert.Equal(3, left);
        Assert.Empty(commands);
        Assert.Equal(new SimRandom(Seed).NextInt(1000), rng.NextInt(1000));
    }

    [Fact]
    public void A_door_goes_on_the_corridor_and_spends_one_allowance()
    {
        WorldState world = World();
        TilePos at = CorridorOnNorthEdge(world);
        Available(world, AvailabilityKind.Door, KeeperWorld.BarricadeDoorId);
        Available(world, AvailabilityKind.Door, KeeperWorld.WoodenDoorId);

        (int left, List<PlayerCommand> commands) = Run(world, Params(trapVsDoor: 4), allowance: 2);

        // The Barricade (+0x8B bit 0x40) is skipped; DoorPreference 1 stops at the first door.
        PlaceDoorCommand door = Assert.IsType<PlaceDoorCommand>(Assert.Single(commands));
        Assert.Equal(at, door.Tile);
        Assert.Equal(KeeperWorld.WoodenDoorId, door.DoorId);
        Assert.Equal(1, left);
    }

    [Fact]
    public void An_own_trap_within_three_tiles_keeps_the_site_empty()
    {
        WorldState world = World();
        TilePos at = CorridorOnNorthEdge(world);
        Available(world, AvailabilityKind.Trap, 1);
        KeeperWorld.Set(world, Db, new TilePos(at.X, at.Y - 2), KeeperWorld.ClaimedPath, Keeper);
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed,
            BlueprintPlacementRules.PlaceTrap(world, Db, new TilePos(at.X, at.Y - 2), Keeper, 1));

        (int left, List<PlayerCommand> commands) = Run(world, Params());

        Assert.Empty(commands);
        Assert.Equal(1, left);
    }

    [Fact]
    public void The_last_eligible_trap_is_the_fallback()
    {
        WorldState world = World();
        TilePos at = CorridorOnNorthEdge(world);
        Available(world, AvailabilityKind.Trap, 1);
        Available(world, AvailabilityKind.Trap, 3);

        // Preference 0: trap 1 (trigger 0) never stops on random(5) < 0, so trap 3 is taken whether
        // or not its random(5) > 0 stops the scan.
        (_, List<PlayerCommand> commands) = Run(world, Params(aggressive: 0));

        PlaceTrapCommand trap = Assert.IsType<PlaceTrapCommand>(Assert.Single(commands));
        Assert.Equal(at, trap.Tile);
        Assert.Equal((byte)3, trap.TrapId);
    }

    [Fact]
    public void With_BoulderTrapsOnLongCorridors_the_Boulder_needs_four_valid_tiles_beyond_the_site()
    {
        WorldState world = World();
        TilePos at = CorridorOnNorthEdge(world);
        Available(world, AvailabilityKind.Trap, KeeperWorld.BoulderTrapId);

        (int left, List<PlayerCommand> commands) = Run(world, Params(boulderOnLong: true));
        Assert.Empty(commands);
        Assert.Equal(1, left);

        for (int i = 1; i <= 4; i++)
            KeeperWorld.Set(world, Db, new TilePos(at.X, at.Y + i), KeeperWorld.ClaimedPath, Keeper);
        (left, commands) = Run(world, Params(boulderOnLong: true));
        PlaceTrapCommand boulder = Assert.IsType<PlaceTrapCommand>(Assert.Single(commands));
        Assert.Equal(KeeperWorld.BoulderTrapId, boulder.TrapId);
        Assert.Equal(0, left);
    }

    [Fact]
    public void The_observation_packet_keeps_the_doors()
    {
        WorldState world = World();
        KeeperWorld.Set(world, Db, new TilePos(5, 5), KeeperWorld.ClaimedPath, Keeper);
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed,
            BlueprintPlacementRules.PlaceDoor(world, Db, new TilePos(5, 5), Keeper, KeeperWorld.WoodenDoorId));
        var view = new KeeperView(Db, world, Keeper);
        KeeperView copy = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(view), Db);
        Assert.Equal(view.Doors, copy.Doors);
        Assert.Equal(new KeeperDoorView(KeeperWorld.WoodenDoorId, Keeper, 5, 5), Assert.Single(copy.Doors));
    }
}
