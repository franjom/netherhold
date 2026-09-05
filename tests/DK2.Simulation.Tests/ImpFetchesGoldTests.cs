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
/// open.
///
/// <para><b>These tests were written asserting the opposite of what they assert
/// now</b>, and the day between the two is the most useful thing in this file.
/// The owner reported <i>"if there is gold on the ground an imp will first take
/// from that gold pile and take it to a treasury"</i> (Q141), watched with a
/// marked gem tile beside the gold and nobody mining it; this project read that
/// as fetching outranking marked excavation and built it that way. The owner
/// then <b>doubted their own reading</b> and asked for the binary — and the
/// binary disagrees: DK2 tries digging and mining first and floor gold sixth of
/// nine. See <c>docs/research/imp-task-priority.md</c>.</para>
///
/// <para>So <see cref="MiningComesBeforeFetching"/> now holds the project to the
/// opposite of what its predecessor did, and
/// <see cref="GoldOnGroundTheKeeperDoesNotOwnIsNotFetched"/> holds it to a rule
/// that was found in the same read and explains the owner's own correction.</para>
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
    /// <b>The load-bearing test, and it asserts the reverse of what it used
    /// to.</b> A marked wall and a pile of gold, both a step away and both there
    /// before the imp ever looks: it goes for the wall.
    ///
    /// <para>Read out of the binary rather than watched: DK2 keeps a keeper-wide
    /// task list whose entries carry a kind, and one function tries the kinds in
    /// a fixed order and returns on the first that takes. Digging and mining a
    /// marked tile are its <b>first two</b> kinds; a floor object flagged
    /// <c>TypeGold</c> is its <b>sixth of nine</b>.</para>
    ///
    /// <para>The mark is set on the tile directly rather than through a command,
    /// deliberately: a command applies in the tile system's slot in the tick,
    /// which is <i>after</i> the imp has already chosen a job, and a test whose
    /// result depends on that is testing the system order and not the
    /// priority.</para>
    /// </summary>
    [Fact]
    public void MiningComesBeforeFetching()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        // Both available before the first tick.
        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);
        run.World.Tile(4, 2).SetTaggedBy(Keeper, true);

        run.Ticks(1);

        Assert.Equal(CreatureActivity.MovingToJob, run.Imp.Activity);
        Assert.Contains(run.Of("job_taken"), e => e.Detail.Contains("MarkedExcavation"));
        Assert.Empty(run.Of("fetch_taken"));
    }

    /// <summary>
    /// And the gold still gets fetched — the ordering is an ordering, not a
    /// refusal. Given long enough, the marked wall goes and so does the pile.
    /// </summary>
    [Fact]
    public void TheGoldIsStillFetchedOnceTheDiggingIsDone()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);
        run.World.Tile(4, 2).SetTaggedBy(Keeper, true);

        run.Ticks(400);

        Assert.NotEmpty(run.Of("job_taken"));
        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(400, GoldStore.Total(run.World, Keeper));
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
    /// <b>The owner's correction, and the rule behind it.</b> They marked a gem
    /// tile that was neither connected to their dungeon by bridge nor surrounded
    /// by claimed tiles; imps mined it and <b>never carried any gold home</b>
    /// (2026-09-05).
    ///
    /// <para>This project first reproduced that <i>by accident</i>: both
    /// searches have to walk, so a stranded imp could not reach a store. The
    /// binary says the real rule is simpler and quite different — <b>the
    /// original's gold task refuses a tile that is neither water nor owned by
    /// the worker's own player, before it looks at what is standing on
    /// it.</b> Unclaimed ground was enough on its own; the bridge never came
    /// into it.</para>
    ///
    /// <para>So this test puts the gold one step away on ground the keeper does
    /// not own, with a treasury in easy reach and nothing else to do. The
    /// accident would have collected it.</para>
    /// </summary>
    [Fact]
    public void GoldOnGroundTheKeeperDoesNotOwnIsNotFetched()
    {
        Run run = Start(new TilePos(3, 3));
        Treasury(run, new TilePos(2, 2));

        // (4,3) is rock; open it into unowned dirt path next to the keeper's
        // room, so the imp can walk onto it and simply will not take the gold.
        ref TileState outside = ref run.World.Tile(4, 3);
        outside.TerrainId = Dk2TerrainFixture.DirtPath;
        outside.Health = run.Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        outside.OwnerPlayerId = PlayerIds.Unowned;

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(4, 3)), 500);

        // A short window on purpose. Left to run, the imp CLAIMS that tile --
        // it is unowned ground touching the keeper's, which is a job of its own
        // -- and the gold becomes fetchable a moment later. That is worth
        // knowing and is probably what DK2 does too, but it would hide the rule
        // this test is about, which is what happens at the moment of choosing.
        run.Ticks(3);

        Assert.Equal(500, FloorGold.OnTile(run.World, run.Db, Index(run.World, new TilePos(4, 3))));
        Assert.NotEqual(CreatureActivity.FetchingGold, run.Imp.Activity);
        Assert.Empty(run.Of("fetch_taken"));
    }

    /// <summary>
    /// And the same tile, once the keeper owns it, is collected — so the refusal
    /// above is the ownership rule and not the imp being unable to get there.
    /// </summary>
    [Fact]
    public void TheSameGoldIsFetchedOnceTheKeeperOwnsTheGround()
    {
        Run run = Start(new TilePos(3, 3));
        Treasury(run, new TilePos(2, 2));

        ref TileState outside = ref run.World.Tile(4, 3);
        outside.TerrainId = Dk2TerrainFixture.DirtPath;
        outside.Health = run.Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        outside.OwnerPlayerId = Keeper;

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(4, 3)), 500);

        run.Ticks(200);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(500, GoldStore.Total(run.World, Keeper));
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
