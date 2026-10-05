// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Where an idle imp claims, and the gold a store takes in off its own floor (ADR-0285).
/// Both READ from the binary and found by replaying the owner's Level 2 game (level2-live1):
/// the claim test <c>0x00453b30</c> counts only the keeper's non-solid ground, the claim search
/// is the ring scan <c>0x00468d10</c> started on the side <c>0x00469340</c> picks, and a store
/// sweeps loose gold in every 16th turn (<c>0x004F47E0</c>).</summary>
/// <remarks>Fixture map: a claimed square at (1..3, 1..3) in rock.</remarks>
public class ImpClaimSearchTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameSimulation Sim, ListEventSink Events) Start(TilePos? impAt)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        if (impAt is TilePos at) world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static void Set(GameSimulation sim, int x, int y, byte terrain, byte owner)
    {
        ref TileState tile = ref sim.World.Tile(x, y);
        tile.TerrainId = terrain;
        tile.OwnerPlayerId = owner;
        tile.Health = sim.Database.TerrainById(terrain).StartingHealth;
    }

    private static SimEvent FirstJob(ListEventSink events, GameSimulation sim)
    {
        for (int i = 0; i < 4 && !events.Events.Any(e => e.Kind == "job_taken"); i++) sim.Tick(InputFrame.Empty);
        return events.Events.First(e => e.Kind == "job_taken");
    }

    /// <summary>(5,2) touches the keeper's own wall at (5,1) and nothing else the keeper holds. A
    /// wall is Solid, so it does not start a claim: the imp standing on (5,2) walks to (4,2),
    /// which touches the claimed square. Ours used to take its own tile.</summary>
    [Fact]
    public void AKeepersWallDoesNotStartAClaim_OnlyItsFloorDoes()
    {
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(5, 2));
        Set(sim, 4, 2, Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);
        Set(sim, 5, 2, Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);
        Set(sim, 5, 1, Dk2TerrainFixture.ReinforcedWall, Keeper);

        SimEvent job = FirstJob(events, sim);

        Assert.Contains("(4,2) Claim", job.Detail);
    }

    /// <summary>The imp stands on its own floor at (2,4) between two claimable tiles, (1,4) west
    /// and (3,4) east. The side chooser grades north, east, south, west and keeps the first of
    /// the best, so east beats west at a tie and the ring starts there. A route search trying
    /// up, left, right, down took the west one.</summary>
    [Fact]
    public void OfTwoClaimsBesideTheImp_TheEastOneComesFirst()
    {
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 4));
        Set(sim, 2, 4, Dk2TerrainFixture.ClaimedPath, Keeper);
        Set(sim, 1, 4, Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);
        Set(sim, 3, 4, Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);

        SimEvent job = FirstJob(events, sim);

        Assert.Contains("(3,4) Claim", job.Detail);
    }

    /// <summary>A pile lying on a Treasury goes in on a turn divisible by 16 and not before, and
    /// what the room cannot hold stays on the tile: nine tiles of 3000 take 27,000 of 30,000.</summary>
    [Fact]
    public void ATreasuryTakesInThePileOnItsFloorEverySixteenthTurn_AndLeavesWhatDoesNotFit()
    {
        (GameSimulation sim, ListEventSink events) = Start(null);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                Set(sim, x, y, Dk2TerrainFixture.TreasuryTerrain, Keeper);
        int centre = (2 * sim.World.Width) + 2;
        ObjectState pile = FloorGold.Drop(sim.World, sim.Database, centre, 30000)!;

        for (int i = 0; i < StoreSweepSystem.SweepTurns + 1; i++) sim.Tick(InputFrame.Empty);

        SimEvent swept = Assert.Single(events.Events, e => e.Kind == "swept");
        Assert.Equal(0, swept.Tick.Value % StoreSweepSystem.SweepTurns);
        Assert.Equal(27000, GoldStore.Total(sim.World, Keeper));
        // DK2 destroys the swept pile and puts what did not fit down as a new one (0x004F47E0).
        Assert.DoesNotContain(pile, sim.World.Objects);
        ObjectState left = Assert.Single(sim.World.Objects, o => o.ObjectTypeId == FloorGold.FloorGoldObjectId);
        Assert.Equal(3000, left.GoldHeld);
        Assert.Equal(centre, left.TileIndex);
    }
}
