// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>An imp fetching gold off the floor — the half of carrying ADR-0055
/// left open. Floor gold is sixth of nine kinds: DK2 tries digging and mining
/// first (docs/research/imp-task-priority.md), contradicting the owner's report
/// that an imp takes from a floor pile first (Q141), which was watched with a
/// marked gem tile beside the gold and nobody mining it and was doubted by the
/// owner before the binary settled it — see
/// <see cref="MiningComesBeforeFetching"/>.</summary>
/// <remarks>Gold is not a rung on that ladder (Q146, Q147, ADR-0058): the original
/// searches its whole-map kinds first, then sweeps outwards from the imp trying
/// reinforce then gold at each tile, so a wall beats gold on the same tile and a
/// nearer pile beats a farther wall. The older tests here pre-reinforce the room's
/// walls so that they measure fetching and not that competition.</remarks>
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

    /// <summary>
    /// Turns every rock tile orthogonally beside the keeper's floor into a
    /// finished, owned reinforced wall, so that Tier B has no wall to prefer
    /// and a test can look at gold on its own. Call it after the terrain is
    /// set up and before the first tick.
    /// </summary>
    private static void WallsAlreadyReinforced(Run run)
    {
        WorldState world = run.World;
        TerrainDef wall = run.Db.TerrainById(Dk2TerrainFixture.ReinforcedWall);
        int[] dx = [0, -1, 1, 0];
        int[] dy = [-1, 0, 0, 1];

        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                ref TileState floor = ref world.Tile(x, y);
                if (floor.OwnerPlayerId != Keeper) continue;
                if (run.Db.TerrainById(floor.TerrainId).IsSolid) continue;

                for (int i = 0; i < dx.Length; i++)
                {
                    var at = new TilePos(x + dx[i], y + dy[i]);
                    if (!world.Contains(at)) continue;
                    ref TileState rock = ref world.Tile(at);
                    if (rock.TerrainId != Dk2TerrainFixture.Rock) continue;

                    rock.TerrainId = wall.TerrainId;
                    rock.Health = wall.MaxHealth;
                    rock.OwnerPlayerId = Keeper;
                }
            }
        }
    }

    // ---- the answer itself -------------------------------------------------

    /// <summary>
    /// A handful of gold on the floor of the keeper's room, an imp standing in it,
    /// and a treasury tile beside it: the imp picks the gold up and keeps it. State
    /// 27 (<c>0x00466850</c>) calls the store chooser only when the hands are full;
    /// a part-load sets 10, and nothing in 10 deposits it (ADR-0288).
    /// </summary>
    [Fact]
    public void AnImpKeepsAPartLoadOfFloorGoldInItsHands()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 900);

        run.Ticks(400);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(0, GoldStore.Total(run.World, Keeper));
        Assert.Equal(900, run.Imp.GoldHeld);
        Assert.Contains(run.Of("gold_picked_up"), e => e.Detail.Contains("amount=900"));
        Assert.Empty(run.Of("gold_delivered"));
    }

    /// <summary>
    /// A full load goes to the treasury: the same room with a pile the imp's hands
    /// hold exactly, and the gold ends up in the treasury and the floor is bare.
    /// </summary>
    [Fact]
    public void AnImpCarriesAFullLoadOfFloorGoldToATreasury()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));
        int hands = run.Db.CreatureById(Dk2TerrainFixture.ImpCreatureId).MaxGoldHeld;

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), hands);

        run.Ticks(400);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(hands, GoldStore.Total(run.World, Keeper));
        Assert.Equal(0, run.Imp.GoldHeld);

        Assert.Contains(run.Of("fetch_taken"), e => e.Detail.Contains($"gold={hands}"));
        Assert.Contains(run.Of("gold_picked_up"), e => e.Detail.Contains($"amount={hands}"));
        Assert.Contains(run.Of("gold_delivered"), e => e.Detail.Contains($"amount={hands}"));
    }

    /// <summary>A marked wall and a pile of gold, both a step away and both there
    /// before the imp looks: it goes for the wall. STRONGLY INFERRED from the
    /// binary — DK2 keeps a keeper-wide task list whose entries carry a kind, and
    /// one function tries the kinds in a fixed order, returning on the first that
    /// takes; digging and mining a marked tile are its first two kinds and a floor
    /// object flagged <c>TypeGold</c> its sixth of nine.</summary>
    /// <remarks>The mark is set on the tile directly rather than through a
    /// command: a command applies in the tile system's slot in the tick, after the
    /// imp has chosen a job, so a test that depended on it would be testing the
    /// system order and not the priority.</remarks>
    [Fact]
    public void MiningComesBeforeFetching()
    {
        Run run = Start(new TilePos(1, 1));
        Treasury(run, new TilePos(3, 3));

        // Both available before the first tick.
        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);
        run.World.Tile(4, 2).SetTaggedBy(Keeper, true);

        run.Ticks(1);
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

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
        WallsAlreadyReinforced(run);

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);
        run.World.Tile(4, 2).SetTaggedBy(Keeper, true);

        // Long enough on purpose. Digging (4,2) opens three faces of bare rock
        // around the new floor, and the imp -- standing on it to claim it --
        // finds them at distance zero and reinforces all three before it
        // crosses the room for the pile. That is the second tier working as the
        // original does, not a slow test.
        run.Ticks(2400);

        Assert.NotEmpty(run.Of("job_taken"));
        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(400, run.Imp.GoldHeld); // a part-load, kept (ADR-0288)
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
    /// is taken up — here into the imp's hands, a part-load it keeps (ADR-0288).
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
        Assert.Equal(600, run.Imp.GoldHeld);
    }

    // ---- the second tier (Q146, Q147, ADR-0058) ----------------------------

    /// <summary>
    /// <b>Claiming is a whole-map kind and gold is not.</b> Gold under the imp's
    /// feet, unowned floor two tiles off: it goes and claims. This is the line
    /// that moved on 2026-09-05 — the day before, gold sat above claiming.
    /// </summary>
    [Fact]
    public void ClaimingComesBeforeFetching()
    {
        Run run = Start(new TilePos(2, 2));
        Treasury(run, new TilePos(1, 1));

        ref TileState outside = ref run.World.Tile(4, 2);
        outside.TerrainId = Dk2TerrainFixture.DirtPath;
        outside.Health = run.Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        outside.OwnerPlayerId = PlayerIds.Unowned;

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);

        run.Ticks(1);
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        SimEvent taken = Assert.Single(run.Of("job_taken"));
        Assert.Contains("priority=Claim", taken.Detail);
        Assert.Contains("(4,2)", taken.Detail);
        Assert.Empty(run.Of("fetch_taken"));
    }

    /// <summary>
    /// <b>Inside the sweep, distance is the key.</b> Gold under the imp's feet
    /// in the middle of the room; bare rock one step away on every side. The
    /// pile is nearer, so the pile wins — which is what makes gold collectable
    /// at all in a dungeon that always has a wall somewhere.
    /// </summary>
    [Fact]
    public void ANearerPileBeatsAFartherWall()
    {
        Run run = Start(new TilePos(2, 2));
        Treasury(run, new TilePos(1, 1));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(2, 2)), 400);

        run.Ticks(1);
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        Assert.Single(run.Of("fetch_taken"));
        Assert.Empty(run.Of("job_taken"));
        Assert.Equal(CreatureActivity.FetchingGold, run.Imp.Activity);
    }

    /// <summary>
    /// <b>And at the same distance, the wall wins.</b> The imp stands on gold at
    /// the room's edge with bare rock beside it: both jobs are at distance zero,
    /// and the original's per-tile order puts reinforcing before gold.
    /// </summary>
    [Fact]
    public void AWallBeatsGoldOnTheSameTile()
    {
        Run run = Start(new TilePos(3, 2));
        Treasury(run, new TilePos(1, 1));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(3, 2)), 400);

        run.Ticks(1);
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        SimEvent taken = Assert.Single(run.Of("job_taken"));
        Assert.Contains("priority=Reinforce", taken.Detail);
        Assert.Contains("(4,2)", taken.Detail);
        Assert.Empty(run.Of("fetch_taken"));
    }

    /// <summary>
    /// The mirror of <see cref="ANearerPileBeatsAFartherWall"/>: bare rock beside
    /// the imp and the pile across the room. The wall is nearer, so the wall wins.
    /// </summary>
    [Fact]
    public void ANearerWallBeatsAFartherPile()
    {
        Run run = Start(new TilePos(3, 1));
        Treasury(run, new TilePos(2, 2));

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(1, 3)), 400);

        run.Ticks(1);
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        SimEvent taken = Assert.Single(run.Of("job_taken"));
        Assert.Contains("priority=Reinforce", taken.Detail);
        Assert.Empty(run.Of("fetch_taken"));
    }

    // ---- the owner's correction -------------------------------------------

    /// <summary>The owner marked a gem tile neither bridged to their dungeon nor
    /// surrounded by claimed tiles; imps mined it and never carried any gold home
    /// (CONFIRMED, owner 2026-09-05). The rule behind it, from the binary: the
    /// original's gold task refuses a tile that is neither water nor owned by the
    /// worker's own player, before it looks at what is standing on it — unclaimed
    /// ground is enough on its own and the bridge never came into it.</summary>
    /// <remarks>So the gold goes one step away on ground the keeper does not own,
    /// with a treasury in easy reach and nothing else to do. Reproducing this by
    /// walkability alone — a stranded imp cannot reach a store — would collect
    /// it.</remarks>
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
        WallsAlreadyReinforced(run);

        FloorGold.Drop(run.World, run.Db, Index(run.World, new TilePos(4, 3)), 500);

        run.Ticks(200);

        Assert.Equal(0, FloorGold.Total(run.World, run.Db));
        Assert.Equal(500, run.Imp.GoldHeld);
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

        // State 1's tick, then one in 10: every imp looks, and only one may have the tile.
        run.Ticks(2);

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
        WallsAlreadyReinforced(run);

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
