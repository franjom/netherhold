// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A Prison's door and a jail break: <c>0x004E8660</c>'s Prison arm, states 122 and 121,
/// and the release <c>0x004E2E70</c> (ADR-0229, <c>jail-break.md</c>).</summary>
public sealed class JailBreakTests
{
    private const byte Keeper = 4;
    private const byte Hero = PlayerIds.Good;
    private const byte Rival = 5;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;

    // A 5x5 prison at (4..8, 2..6): its floor is the 3x3 at (5..7, 3..5). Placed as the map places a room, its
    // slab chain is row-major, so its door is (5, 2), at the west end (room-slab-chain.md §6; SlabChainTests builds).
    private static readonly TilePos Door = new(5, 2);
    private static readonly TilePos Cell = new(6, 4);
    private static readonly TilePos Outside = new(10, 4);
    private static readonly TilePos Rim = new(8, 4);

    private static (GameDatabase, WorldState) World(int size = 5)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, Hero, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        int x0 = size == 5 ? 4 : 5, y0 = size == 5 ? 2 : 3;
        for (int y = y0; y < y0 + size; y++)
        for (int x = x0; x < x0 + size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
        return (db, world);
    }

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;

    private static CreatureState Prisoner(GameDatabase db, WorldState world, byte owner, TilePos at)
    {
        CreatureState c = world.SpawnCreature(db, GoblinId, owner, at);
        Assert.True(PrisonRules.Imprison(world, db, c, Keeper, Index(world, at)));
        return c;
    }

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new CreatureMovementSystem(), new PrisonSystem(), new PrisonerWanderSystem(), new RescueSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static CreatureState Rescuer(GameDatabase db, WorldState world, byte flags = RescueRules.FreesFriendsFlag)
    {
        CreatureState hero = world.SpawnCreature(db, GoblinId, Hero, Outside);
        hero.Flags = flags;
        return hero;
    }

    private static void StepOnto(CreatureState c, TilePos at)
    {
        c.PosX = CreatureState.CentreOf(at.X);
        c.PosY = CreatureState.CentreOf(at.Y);
    }

    private static RoomInstance Prison(GameDatabase db, WorldState world) => RoomIndex.RoomAt(world, db, Index(world, Cell))!;

    [Fact]
    public void TheDoorIsNorthOfTheFirstFloorTileWhoseThreeNorthernNeighboursAreNotFloor_WestEndForAMapRoomsRowMajorChain()
    {
        (GameDatabase db, WorldState world) = World();
        Assert.Equal(Index(world, Door), JailBreakRules.DoorTile(world, Prison(db, world)));

        (GameDatabase db3, WorldState world3) = World(3);   // 3x3 at (5..7, 3..5): one floor tile, (6, 4)
        Assert.Equal(Index(world3, new TilePos(6, 3)), JailBreakRules.DoorTile(world3, Prison(db3, world3)));
    }

    [Fact]
    public void AHeroInAnEnemyPrisonWalksToTheDoor_OpensIt_AndItsComradeWalksOut()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState comrade = Prisoner(db, world, Hero, Cell);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState hero = Rescuer(db, world);
        sim.Tick();

        StepOnto(hero, Rim);
        sim.Tick();
        Assert.Equal((CreatureActivity.Rescuing, JailBreakRules.BreakingOutState), (hero.Activity, hero.Dk2StateId));

        for (int i = 0; i < 300 && PrisonRules.IsImprisoned(comrade); i++) sim.Tick();

        Assert.False(PrisonRules.IsImprisoned(comrade));
        Assert.Contains(sink.Events, e => e.Kind == "rescuer_to_door");
        Assert.Contains(sink.Events, e => e.Kind == "prison_door_opened");
        Assert.Contains(sink.Events, e => e.Kind == "prisoner_released" && e.Detail.StartsWith($"creature={comrade.Id} ", StringComparison.Ordinal));
        Assert.Contains(sink.Events, e => e.Kind == "prisoner_freed" && e.Detail.Contains("reason=walked_out"));
        Assert.Equal(ImpStates.Wandering, hero.Dk2StateId);   // 1, then 87
        byte door = world.TileAtIndex(Prison(db, world).TileIndices[0]).PrisonDoor;
        Assert.Equal(JailBreakRules.Activated | JailBreakRules.Open | JailBreakRules.Broken, door);
    }

    /// <summary>The unlock goes through the lock toggle <c>0x004E3680</c>, which begins with
    /// <c>0x004E2D40(0)</c>: a stale <c>PrisonJailBroken</c> is clear on the turn the door opens, and
    /// set again only when the comrade is released (<c>0x004E2E70</c>, once per creature).</summary>
    [Fact]
    public void UnlockingClearsTheBrokenMark_AndReleasingSetsItAgain()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState comrade = Prisoner(db, world, Hero, Cell);
        JailBreakRules.DoorOf(world, Prison(db, world)) = JailBreakRules.Broken;
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState hero = Rescuer(db, world);
        sim.Tick();
        StepOnto(hero, Rim);

        for (int i = 0; i < 300 && !sink.Events.Any(e => e.Kind == "prison_door_opened"); i++) sim.Tick();

        Assert.Equal(JailBreakRules.Activated | JailBreakRules.Open, JailBreakRules.DoorOf(world, Prison(db, world)));
        for (int i = 0; i < 300 && PrisonRules.IsImprisoned(comrade); i++) sim.Tick();
        Assert.Equal(JailBreakRules.Activated | JailBreakRules.Open | JailBreakRules.Broken, JailBreakRules.DoorOf(world, Prison(db, world)));
    }

    /// <summary><c>0x004E2E70(1, owner, 0)</c> releases the rescuer's own side: another keeper's
    /// prisoner in the same cell stays.</summary>
    [Fact]
    public void OnlyTheRescuersSideWalksOut()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState comrade = Prisoner(db, world, Hero, Cell);
        CreatureState stranger = Prisoner(db, world, Rival, new TilePos(7, 5));
        (GameSimulation sim, _) = Simulation(db, world);
        CreatureState hero = Rescuer(db, world);
        sim.Tick();
        StepOnto(hero, Rim);

        for (int i = 0; i < 300 && PrisonRules.IsImprisoned(comrade); i++) sim.Tick();
        for (int i = 0; i < 40; i++) sim.Tick();

        Assert.False(PrisonRules.IsImprisoned(comrade));
        Assert.True(PrisonRules.IsImprisoned(stranger));
    }

    [Fact]
    public void WithNoAllyInTheCell_OrWithoutTheFlag_NobodyBreaksIn()
    {
        (GameDatabase db, WorldState world) = World();
        Prisoner(db, world, Rival, Cell);
        (GameSimulation sim, _) = Simulation(db, world);
        CreatureState hero = Rescuer(db, world);
        sim.Tick();
        StepOnto(hero, Rim);
        sim.Tick();
        Assert.NotEqual(JailBreakRules.BreakingOutState, hero.Dk2StateId);

        (GameDatabase db2, WorldState world2) = World();
        Prisoner(db2, world2, Hero, Cell);
        (GameSimulation sim2, _) = Simulation(db2, world2);
        CreatureState plain = Rescuer(db2, world2, flags: 0);
        sim2.Tick();
        StepOnto(plain, Rim);
        sim2.Tick();
        Assert.NotEqual(JailBreakRules.BreakingOutState, plain.Dk2StateId);
    }
}
