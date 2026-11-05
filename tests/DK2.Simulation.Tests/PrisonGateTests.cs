// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>An imp takes a prisoner into the Prison through its gate (docs/research/prison-gate.md):
/// state 43 walks to the door tile with option 0xE0, then south one tile into the cell with 0xE4,
/// where state 44 admits it; state 46's walk (0xEC) takes the imp back out through the door.
/// The cell's floor is nav bit 2, which only a request with option bit 2 crosses.</summary>
public class PrisonGateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => Dk2TerrainFixture.Sellable(WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]));

    /// <summary>A 5x5 Prison at (3..7, 2..6) on paved ground, whose fenced cell is the 3x3 at (4..6, 3..5).</summary>
    private static void BigPrison(GameDatabase db, WorldState world)
    {
        for (int y = 1; y <= 8; y++)
        {
            for (int x = 1; x <= 8; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        }

        for (int y = 2; y <= 6; y++)
            for (int x = 3; x <= 7; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
            new PrisonSystem(),
            new PrisonerEatSystem(),
            new PrisonerWanderSystem(),
        ]);
        sim.ObserveWith(sink);
        return sim;
    }

    private sealed record Setup(
        GameDatabase Db, WorldState World, GameSimulation Sim, ListEventSink Sink,
        CreatureState Imp, CreatureState Body, RoomInstance Prison, int Door);

    private static Setup Arrange(TilePos impAt, TilePos bodyAt)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        BigPrison(db, world);
        CreatureState body = world.SpawnCreature(db, GoblinId, Rival, bodyAt);
        body.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), body));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, impAt);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        RoomInstance prison = RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Prison);
        return new Setup(db, world, sim, sink, imp, body, prison, JailBreakRules.DoorTile(world, prison));
    }

    private static int Index(WorldState world, TilePos tile) => (tile.Y * world.Width) + tile.X;

    [Fact]
    public void TheDoorTileIsARimTileAndTheCellIsAThreeByThree()
    {
        Setup s = Arrange(new TilePos(2, 3), new TilePos(1, 1));
        Assert.True(s.Door >= 0);
        Assert.False(s.Prison.IsInterior(s.Door));
        Assert.True(s.Prison.IsInterior(s.Door + s.World.Width));
        Assert.Equal(9, s.Prison.InteriorTileCount);
    }

    [Fact]
    public void AnImpCarriesAPrisonerIntoTheCellThroughTheDoorTileAlone()
    {
        // The imp starts west of the Prison: its nearest rim tile is not the door tile.
        Setup s = Arrange(new TilePos(2, 4), new TilePos(1, 1));
        WorldState world = s.World;
        int below = s.Door + world.Width;
        var visited = new List<int>();
        int? enteredFrom = null, enteredAt = null;
        int last = Index(world, s.Imp.Tile);

        for (int i = 0; i < 40 * Second && !s.Sink.Events.Any(e => e.Kind == "creature_imprisoned"); i++)
        {
            s.Sim.Tick();
            int here = Index(world, s.Imp.Tile);
            if (here == last) continue;
            if (s.Prison.IsInterior(here) && enteredAt is null)
            {
                enteredFrom = last;
                enteredAt = here;
            }
            visited.Add(here);
            last = here;
        }

        Assert.Contains(s.Sink.Events, e => e.Kind == "creature_imprisoned");
        Assert.Equal(CreatureActivity.Imprisoned, s.Body.Activity);
        Assert.Equal(s.Door, enteredFrom);
        Assert.Equal(below, enteredAt);
        // It was admitted where it stood, the tile south of the door.
        Assert.Equal(below, Index(world, s.Body.Tile));
    }

    [Fact]
    public void TheImpWalksOutThroughTheDoorAndIsNotStuckInTheCell()
    {
        Setup s = Arrange(new TilePos(2, 4), new TilePos(1, 1));
        WorldState world = s.World;
        bool imprisoned = false;
        bool leftByDoor = false;
        int last = Index(world, s.Imp.Tile);

        for (int i = 0; i < 90 * Second; i++)
        {
            s.Sim.Tick();
            imprisoned |= s.Sink.Events.Any(e => e.Kind == "creature_imprisoned");
            int here = Index(world, s.Imp.Tile);
            if (imprisoned && here != last && last == s.Door + world.Width && here == s.Door) leftByDoor = true;
            last = here;
        }

        Assert.True(imprisoned);
        Assert.True(leftByDoor, "the imp never stepped from the cell onto the door tile");
        Assert.False(s.Prison.IsInterior(Index(world, s.Imp.Tile)), "the imp is still inside the cell");
    }

    [Fact]
    public void APrisonFilledBeforeTheImpReachesTheDoorLeavesTheBodyOnTheDoorTile()
    {
        // State 43 asks again at the door (0x00467DD0): with no other Prison the body is put down
        // where the imp stands, which is the door tile, a rim tile.
        Setup s = Arrange(new TilePos(2, 4), new TilePos(1, 1));
        WorldState world = s.World;
        bool filled = false;
        for (int i = 0; i < 40 * Second && !s.Sink.Events.Any(e => e.Kind == "body_dropped"); i++)
        {
            s.Sim.Tick();
            if (filled || s.Imp.Activity != CreatureActivity.CarryingBody) continue;
            filled = true;
            // Capacity is nine interior tiles plus one: ten captives fill it.
            for (int k = 0; k < PrisonRules.Capacity(s.Prison); k++)
            {
                CreatureState captive = world.SpawnCreature(s.Db, GoblinId, Rival, new TilePos(5, 4));
                Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, s.Db), captive));
                Assert.True(PrisonRules.Imprison(new StateHookContext(world, s.Db), captive, Keeper, Index(world, new TilePos(5, 4))));
            }
        }

        Assert.Contains(s.Sink.Events, e => e.Kind == "body_dropped" && e.Detail.Contains("reason=not_admitted"));
        Assert.False(PrisonRules.IsImprisoned(s.Body));
        Assert.Equal(s.Door, Index(world, s.Body.Tile));
        Assert.False(s.Prison.IsInterior(Index(world, s.Imp.Tile)));
    }
}
