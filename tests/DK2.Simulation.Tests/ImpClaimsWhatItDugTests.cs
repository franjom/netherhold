// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>What becomes of the tile an imp has just dug. READ from the binary
/// (ADR-0286, superseding ADR-0058's reading of Q154): a dig or a mine that leaves
/// plain unowned path beside a keeper's floor raises kind 14 there, and the only
/// taker is a worker walking to deposit that steps onto it (<c>0x00464170</c>),
/// once none of its eight neighbours still carries a dig task. Choosing a job never
/// takes kind 14; with nothing else to do the tile is an ordinary claim.</summary>
/// <remarks>Fixture map: a claimed room at (1..3, 1..3) in rock; (4,2) is the wall
/// east of the room's middle row, (5,2) the one beyond it, (1,4) the wall south of
/// the room's first column. All three are diggable from the room and become
/// unowned dirt path when dug.</remarks>
public class ImpClaimsWhatItDugTests
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

        /// <summary>Runs until the tile is no longer rock, or gives up.</summary>
        public void UntilDug(TilePos at, int limit = 400)
        {
            for (int i = 0; i < limit && World.Tile(at).TerrainId == Dk2TerrainFixture.Rock; i++) Tick();
            Assert.NotEqual(Dk2TerrainFixture.Rock, World.Tile(at).TerrainId);
        }

        /// <summary>Runs until the digger's action ends and it raises kind 14 on the tile, or gives up.</summary>
        public void UntilRaised(TilePos at, int limit = 20)
        {
            for (int i = 0; i < limit && !World.Tile(at).FreshlyDug; i++) Tick();
            Assert.True(World.Tile(at).FreshlyDug);
        }
    }

    private static Run Start(TilePos impAt)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);

        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    private static void Mark(Run run, params TilePos[] tiles)
    {
        foreach (TilePos t in tiles) run.World.Tile(t).SetTaggedBy(Keeper, true);
    }

    /// <summary>
    /// One wall marked. It is dug, and with no digging left the imp's next job is
    /// to claim the floor it just made — as an <i>ordinary</i> claim. DK2's Tier A
    /// table opens with the just-dug kind 14, but its search has no arm for 14
    /// (jump table <c>0x00468770</c>), so choosing never takes it (ADR-0286).
    /// </summary>
    [Fact]
    public void WithNoDiggingLeft_TheTileJustDugIsClaimedAsAnOrdinaryClaim()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2));

        run.UntilDug(new TilePos(4, 2));
        run.UntilRaised(new TilePos(4, 2));   // when the dig action runs out (ADR-0305)

        run.Ticks(2);   // then 10 looks

        SimEvent next = run.Of("job_taken").Last();
        Assert.Contains("priority=Claim ", next.Detail);
        Assert.Contains("(4,2)", next.Detail);
        Assert.DoesNotContain(run.Of("job_taken"), e => e.Detail.Contains("ClaimFreshlyDug"));
        Assert.DoesNotContain(run.Of("job_taken"), e => e.Detail.Contains("Reinforce"));
    }

    /// <summary>
    /// And the claim finishes, and finishing it ends the tile's being fresh —
    /// so the flag is a one-shot and does not haunt the tile into the hash
    /// forever.
    /// </summary>
    [Fact]
    public void TheClaimCompletesAndTheTileIsNoLongerFresh()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2));

        run.UntilDug(new TilePos(4, 2));
        run.Ticks(SimulationTiming.TicksFromSeconds(5));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(Keeper, run.World.Tile(4, 2).OwnerPlayerId);
        Assert.False(run.World.Tile(4, 2).FreshlyDug);
    }

    /// <summary>
    /// <b>The claim waits while a neighbour is still marked.</b> Two walls in a
    /// row: when the first is dug the second, right beside it, is still marked,
    /// so the imp digs on rather than turning round to claim. Only when the
    /// second is down does it claim — and then it claims both.
    /// </summary>
    [Fact]
    public void TheClaimWaitsWhileANeighbourIsStillMarked()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2), new TilePos(5, 2));

        run.UntilDug(new TilePos(4, 2));
        run.Ticks(8);   // the dig action runs out (6 ticks at most), then 10 looks

        SimEvent next = run.Of("job_taken").Last();
        Assert.Contains("priority=MarkedExcavation", next.Detail);
        Assert.Contains("(5,2)", next.Detail);
        run.UntilDug(new TilePos(5, 2));
        run.Ticks(SimulationTiming.TicksFromSeconds(12));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(5, 2).TerrainId);
    }

    /// <summary>
    /// <b>A worker walking to deposit claims a just-dug tile it steps onto</b>
    /// (<c>0x00464170</c>, ADR-0286): (5,2) was dug and touches the keeper's floor
    /// at (4,2); the imp that dug (8,2) with gold in its hands has nothing more to
    /// dig, so it carries the gold from (7,2) (kind 15), walks over (5,2), more than
    /// two tiles from its last task, and takes the claim there.
    /// </summary>
    [Fact]
    public void AnImpGoingToDepositClaimsTheJustDugTileItStepsOnto()
    {
        Run run = Start(new TilePos(7, 2));
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                SetTile(run, x, y, Dk2TerrainFixture.TreasuryTerrain, Keeper);
        SetTile(run, 4, 2, Dk2TerrainFixture.ClaimedPath, Keeper);
        for (int x = 5; x <= 7; x++) SetTile(run, x, 2, Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);
        run.World.Tile(5, 2).FreshlyDug = true;
        run.Imp.GoldHeld = 500;
        run.Imp.LastTaskKind = ImpTaskKind.Excavate;
        run.Imp.LastTaskTileIndex = (2 * run.World.Width) + 8;

        run.Ticks(SimulationTiming.TicksFromSeconds(4));

        SimEvent claim = Assert.Single(run.Of("claimed_on_the_way"));
        Assert.Contains("at=(5,2)", claim.Detail);
    }

    /// <summary>
    /// <b>A worker walking to a dig claims a just-dug tile it steps onto too</b> (<c>0x00464170</c>,
    /// ADR-0303). Empty hands, the wall at (1,2) marked: the imp walks west from (7,2) to dig it
    /// and crosses (5,2), just dug, touching the keeper's floor at (4,2), four tiles from its new
    /// task. Level 1 seed 5's imp 452 does exactly this at (10,28), tick 143.
    /// </summary>
    [Fact]
    public void AnImpWalkingToADigClaimsTheJustDugTileItStepsOnto()
    {
        Run run = Start(new TilePos(7, 2));
        for (int x = 2; x <= 4; x++) SetTile(run, x, 2, Dk2TerrainFixture.ClaimedPath, Keeper);
        for (int x = 5; x <= 7; x++) SetTile(run, x, 2, Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);
        SetTile(run, 1, 2, Dk2TerrainFixture.Rock, PlayerIds.Unowned);
        run.World.Tile(5, 2).FreshlyDug = true;
        Mark(run, new TilePos(1, 2));

        run.Ticks(SimulationTiming.TicksFromSeconds(4));

        Assert.Contains(run.Of("job_taken"), e => e.Detail.Contains("(1,2)") && e.Detail.Contains("MarkedExcavation"));
        SimEvent claim = Assert.Single(run.Of("claimed_on_the_way"));
        Assert.Contains("at=(5,2)", claim.Detail);
        Assert.Contains("gold=0", claim.Detail);
    }

    private static void SetTile(Run run, int x, int y, byte terrain, byte owner)
    {
        ref TileState tile = ref run.World.Tile(x, y);
        tile.TerrainId = terrain;
        tile.OwnerPlayerId = owner;
        tile.Health = run.Db.TerrainById(terrain).StartingHealth;
    }

    /// <summary>
    /// <b>A digger digs on before it claims.</b> A wall east of the room and a wall
    /// south of it, both marked, not neighbours of each other. The imp digs the
    /// nearer, and then, though the just-dug claim is first of Tier A, walks over
    /// to the second mark: state 10 first asks for more of the kind just done
    /// (the record table at <c>0x006986A0</c>, imp-task-priority.md §4), and a
    /// dig's kind is dig. The just-dug claim is left for whoever looks next with
    /// no digging behind it; this imp claims it once the digging is done, claiming
    /// on from the tile it dug last.
    /// </summary>
    [Fact]
    public void ADiggerDigsTheMarkAcrossTheRoomBeforeClaimingWhatItDug()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2), new TilePos(1, 4));

        run.UntilDug(new TilePos(4, 2));
        Assert.True(run.World.Tile(1, 4).IsTaggedBy(Keeper), "the far mark should still be standing");

        run.Ticks(8);   // the dig action runs out (6 ticks at most), then 10 looks

        SimEvent next = run.Of("job_taken").Last();
        Assert.Contains("priority=MarkedExcavation", next.Detail);
        Assert.Contains("(1,4)", next.Detail);

        run.UntilDug(new TilePos(1, 4));
        run.Ticks(SimulationTiming.TicksFromSeconds(12));
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
    }

    /// <summary>
    /// A tile that was dug and then <i>claimed</i> is not fresh; a tile that
    /// was never dug is not fresh however claimable it is. Unowned floor the map
    /// shipped with is an ordinary claim.
    /// </summary>
    [Fact]
    public void FloorTheMapShippedWithIsAnOrdinaryClaim()
    {
        Run run = Start(new TilePos(3, 2));

        ref TileState outside = ref run.World.Tile(4, 2);
        outside.TerrainId = Dk2TerrainFixture.DirtPath;
        outside.Health = run.Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        outside.OwnerPlayerId = PlayerIds.Unowned;

        run.Ticks(2);   // 1 hands the imp to 10, which chooses on the next tick

        SimEvent taken = Assert.Single(run.Of("job_taken"));
        Assert.Contains("priority=Claim ", taken.Detail);
        Assert.DoesNotContain("ClaimFreshlyDug", taken.Detail);
    }

    /// <summary>The flag survives a snapshot, or a loaded game would forget which floor to go back for.</summary>
    [Fact]
    public void FreshlyDugSurvivesASnapshot()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2), new TilePos(5, 2));
        run.UntilDug(new TilePos(4, 2));
        run.UntilRaised(new TilePos(4, 2));

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(run.Sim), run.Db, Dk2TerrainFixture.DefaultSystems());

        Assert.True(restored.World.Tile(4, 2).FreshlyDug);
        Assert.Equal(run.Sim.StateHash(), restored.StateHash());
    }

    /// <summary>
    /// The kind-14 task waits for the digger: the wall falls, its dig task stays in the pool,
    /// and only when the digger's action ends is the task raised (<c>0x004655D0</c>, ADR-0305).
    /// </summary>
    [Fact]
    public void TheJustDugTileIsRaisedWhenTheDiggersActionEnds_NotWhenTheWallFalls()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2));

        run.UntilDug(new TilePos(4, 2));
        Assert.False(run.World.Tile(4, 2).FreshlyDug);
        Assert.False(run.World.Tile(4, 2).DigTaskOutlivesWallFor.IsEmpty);

        run.UntilRaised(new TilePos(4, 2));
        Assert.True(run.World.Tile(4, 2).DigTaskOutlivesWallFor.IsEmpty);
        Assert.Single(run.Of("claim_raised"));
    }

    /// <summary>
    /// An imp that runs the dig walk between the wall's fall and the digger's last action drops
    /// the dead task (<c>0x00469BC8</c>), and the digger then raises nothing: Level 1 seeds 1 and
    /// 6 in DK2 (ADR-0305). The test above is the control, without the second imp.
    /// </summary>
    [Fact]
    public void AnImpChoosingADigBeforeTheDiggersActionEnds_LeavesTheTileUnraised()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2), new TilePos(1, 4));

        run.UntilDug(new TilePos(4, 2));
        run.World.SpawnCreature(run.Db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 2));
        run.Ticks(20);

        Assert.Contains(run.Of("dig_task_dropped"), e => e.Detail.Contains("(4,2)"));
        Assert.DoesNotContain(run.Of("claim_raised"), e => e.Detail.Contains("(4,2)"));
        Assert.False(run.World.Tile(4, 2).FreshlyDug);
    }
}
