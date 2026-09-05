// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// An imp fetching gold off the floor — the half of carrying that ADR-0055 left
/// open, and the one the owner settled by watching (Q141, 2026-09-05).
///
/// <para><i>"If there is gold on the ground an imp will first take from that
/// gold pile and take it to a treasury, until all of it is gone"</i> — reported
/// from a dungeon where a marked gem tile sat right beside the gold and
/// <b>nobody was mining it</b>. That is the strong form of the answer, and
/// <see cref="FetchingComesBeforeMining"/> is the test that holds this project
/// to it.</para>
/// </summary>
public class ImpFetchesGoldTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;
        public GameDatabase Db => Sim.Database;
        public CreatureState Imp => Sim.World.Creatures[0];

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++) Tick();
        }

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    private static Run Start(TilePos impAt, int imps = 1)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        for (int i = 0; i < imps; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);

        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    private static void Treasury(Run run, TilePos at)
    {
        ref TileState tile = ref run.World.Tile(at);
        RoomRules.Place(run.Db, ref tile, run.Db.Room(RoomTypeId.Treasury), Keeper);
    }

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;

    // ---- the answer itself -------------------------------------------------

    /// <summary>
    /// Gold on the floor of the keeper's room, an imp standing in it, and a
    /// treasury tile beside it: the gold ends up in the treasury and the floor
    /// is bare.
    /// </summary>
    [Fact]
    public void AnImpCarriesFloorGoldToATreasury()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 900);

        run.Ticks(400);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(900, GoldStore.Total(run.World, Keeper));
        Assert.Equal(0, run.Imp.GoldHeld);

        Assert.Contains(run.Of("fetch_taken"), e => e.Detail.Contains("gold=900"));
        Assert.Contains(run.Of("gold_picked_up"), e => e.Detail.Contains("amount=900"));
        Assert.Contains(run.Of("gold_delivered"), e => e.Detail.Contains("amount=900"));
    }

    /// <summary>
    /// <b>The load-bearing test.</b> A marked gold seam and a pile on the floor,
    /// both reachable: the imp goes for the gold and does not swing at the seam
    /// until the floor is clear.
    ///
    /// <para>Marked excavation had been the top of the priority order since
    /// ADR-0018 and is CONFIRMED (Q24). This is the observation that put
    /// something above it, so the test asserts the <i>ordering</i> — not that
    /// fetching happens, but that mining does not happen first.</para>
    /// </summary>
    [Fact]
    public void FetchingComesBeforeMining()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        // A pile the imp is standing next to, and a wall beside it to mine.
        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 2)));

        run.Ticks(60);

        // The ordering, asserted as an ordering: the fetch is taken before any
        // tile job is, and no swing lands before the gold is off the floor.
        var log = run.Events.Events.ToList();
        int fetch = log.FindIndex(e => e.Kind == "fetch_taken");
        int job = log.FindIndex(e => e.Kind == "job_taken");
        int pickedUp = log.FindIndex(e => e.Kind == "gold_picked_up");
        int swing = log.FindIndex(e => e.Kind == "work_applied");

        Assert.True(fetch >= 0, "the imp never went for the gold");
        Assert.True(job < 0 || fetch < job, "it took a tile job before the gold");
        Assert.True(pickedUp >= 0, "it never picked the gold up");
        Assert.True(swing < 0 || pickedUp < swing, "it swung at the wall before clearing the floor");
    }

    /// <summary>
    /// And once the floor is clear it goes back to the seam — the ordering is an
    /// ordering, not a permanent preference.
    /// </summary>
    [Fact]
    public void AndMinesAgainOnceTheFloorIsClear()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 2)));

        run.Ticks(400);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.NotEmpty(run.Of("job_taken"));
    }

    /// <summary>
    /// An imp takes what its hands hold and comes back for the rest, "until all
    /// of it is gone".
    /// </summary>
    [Fact]
    public void APileBiggerThanItsHandsTakesSeveralTrips()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));
        Treasury(run, new TilePos(3, 2));

        int hands = run.Db.CreatureById(Dk2TerrainFixture.ImpCreatureId).MaxGoldHeld;
        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), hands * 2);

        run.Ticks(1500);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(hands * 2, GoldStore.Total(run.World, Keeper));
        Assert.True(run.Of("gold_picked_up").Count() >= 2, "it should have made more than one trip");
    }

    /// <summary>
    /// A creature's death leaves up to three piles on one tile (ADR-0055), and
    /// an imp empties them oldest first until its hands are full. The order is
    /// ours and is marked as such; what is <i>not</i> ours is that all of it
    /// eventually goes.
    /// </summary>
    [Fact]
    public void SeveralPilesOnOneTileAreAllCollected()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        int tile = Index(run.World, new TilePos(2, 2));
        FloorGold.Drop(run.World, run.Db, tile, 100);
        FloorGold.Drop(run.World, run.Db, tile, 200);
        FloorGold.Drop(run.World, run.Db, tile, 300);

        run.Ticks(400);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(600, GoldStore.Total(run.World, Keeper));
    }

    // ---- the owner's correction -------------------------------------------

    /// <summary>
    /// <b>The correction, reproduced.</b> The owner marked a gem tile that was
    /// not connected to their dungeon and whose surrounding tiles were not
    /// claimed; imps mined it and <b>never carried any gold home</b>
    /// (2026-09-05).
    ///
    /// <para>Nothing in <c>ImpTaskSystem</c> codes that. It falls out of two
    /// searches that both walk — an imp cannot reach a pile it cannot walk to,
    /// and it cannot reach a store either — so gold piles up where it was mined.
    /// This test pins the outcome rather than the mechanism, because
    /// <b>which</b> of the two rules the original uses is Q144: connectivity and
    /// claimed ground were both absent in what was watched.</para>
    /// </summary>
    [Fact]
    public void AnImpThatCannotReachAStoreLeavesTheGoldWhereItIs()
    {
        // The imp is sealed in an unowned pocket with the gold; the keeper's
        // treasury is on the far side of solid rock.
        Run run = Start(new TilePos(5, 2));
        Treasury(run, new TilePos(3, 3));

        ref TileState pocket = ref run.World.Tile(5, 2);
        pocket.TerrainId = Dk2TerrainFixture.DirtPath;
        pocket.Health = run.Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;

        int tile = Index(run.World, new TilePos(5, 2));
        FloorGold.Drop(run.World, run.Db, tile, 500);

        run.Ticks(400);

        // It has the gold in its hands or the gold is still on the floor, but it
        // is emphatically not in the treasury.
        Assert.Equal(0, GoldStore.Total(run.World, Keeper));
        Assert.Equal(500, FloorGold.Total(run.World, run.Db) + run.Imp.GoldHeld);
        Assert.Contains(run.Of("delivery_blocked"), e => e.Detail.Contains("reason=no_room"));
    }

    // ---- crowding ----------------------------------------------------------

    /// <summary>
    /// One imp to a pile tile, the same cap a floor job gets — an imp stands on
    /// gold to pick it up, so the stance decides the number and nothing new is
    /// invented (<see cref="ImpCrowding"/>). The second imp finds other work
    /// rather than queueing, which is the half of the crowding rule that
    /// matters (Q43).
    /// </summary>
    [Fact]
    public void OnlyOneImpTakesAGivenPile()
    {
        Run run = Start(new TilePos(1, 1), imps: 3);
        Treasury(run, new TilePos(3, 3));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 300);

        // One tick: every imp looks, and only one may have the tile.
        run.Ticks(1);

        Assert.Equal(
            1,
            run.World.Creatures.Count(c => c.Activity == CreatureActivity.FetchingGold));
        Assert.Single(run.Of("fetch_taken"));
    }

    /// <summary>
    /// A pile somebody else emptied is not chased. The imp gives the job up and
    /// looks for another rather than standing on a bare tile.
    /// </summary>
    [Fact]
    public void APileThatVanishesMidWalkIsAbandoned()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        int tile = Index(run.World, new TilePos(3, 1));
        ObjectState pile = FloorGold.Drop(run.World, run.Db, tile, 300)!;

        run.Ticks(2);
        Assert.Equal(CreatureActivity.FetchingGold, run.Imp.Activity);

        run.World.RemoveObject(pile.Id);
        run.Ticks(3);

        Assert.Contains(run.Of("fetch_abandoned"), e => e.Detail.Contains("reason=gone"));
        Assert.NotEqual(CreatureActivity.FetchingGold, run.Imp.Activity);
    }

    /// <summary>
    /// Gold in a treasury is not gold on the floor. An imp does not pick its own
    /// keeper's stored gold back up, or a dungeon would shuffle forever.
    /// </summary>
    [Fact]
    public void StoredGoldIsNotFetched()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));
        GoldStore.Deposit(run.World, run.Db, Keeper, 500);

        Assert.Equal(500, GoldStore.Total(run.World, Keeper));

        run.Ticks(60);

        Assert.Equal(500, GoldStore.Total(run.World, Keeper));
        Assert.Empty(run.Of("fetch_taken"));
    }
}
