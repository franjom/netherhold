// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>An imp claims the tile it has just dug before it does anything else,
/// once nothing beside that tile is still marked for digging. STRONGLY INFERRED
/// from the binary (Q154, ADR-0058, docs/research/imp-task-priority.md §6): a dig
/// that leaves plain unowned path beside a keeper's floor raises a task that is
/// first in the imp's whole-map order, ahead of digging, and the handler takes it
/// as soon as none of the tile's eight neighbours still carries a dig
/// task.</summary>
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
    /// One wall marked. It is dug, and the very next job the imp takes is to
    /// claim the floor it just made — labelled as the just-dug claim, not as an
    /// ordinary one, and taken before any wall gets reinforced.
    /// </summary>
    [Fact]
    public void TheTileJustDugIsClaimedNext()
    {
        Run run = Start(new TilePos(3, 2));
        Mark(run, new TilePos(4, 2));

        run.UntilDug(new TilePos(4, 2));
        Assert.True(run.World.Tile(4, 2).FreshlyDug);

        run.Ticks(8);   // the dig action runs out (6 ticks at most), then 10 looks

        SimEvent next = run.Of("job_taken").Last();
        Assert.Contains("priority=ClaimFreshlyDug", next.Detail);
        Assert.Contains("(4,2)", next.Detail);
        Assert.Contains("Claim", next.Detail);
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
        Assert.DoesNotContain(run.Of("job_taken"), e => e.Detail.Contains("ClaimFreshlyDug"));

        run.UntilDug(new TilePos(5, 2));
        run.Ticks(SimulationTiming.TicksFromSeconds(12));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(5, 2).TerrainId);
        Assert.Contains(run.Of("job_taken"), e => e.Detail.Contains("priority=ClaimFreshlyDug"));
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
        Assert.True(run.World.Tile(4, 2).FreshlyDug);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(run.Sim), run.Db, Dk2TerrainFixture.DefaultSystems());

        Assert.True(restored.World.Tile(4, 2).FreshlyDug);
        Assert.Equal(run.Sim.StateHash(), restored.StateHash());
    }
}
