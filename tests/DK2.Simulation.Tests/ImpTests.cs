// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Pathfinding;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The first worker: what an imp does when its keeper marks a tile.
///
/// The fixture map is a claimed-path room at (1..3, 1..3) surrounded by rock,
/// with a gold seam at (5,5) and a gem face at (6,5) — see
/// <see cref="Dk2TerrainFixture"/>. Timings below are derived from the shipped
/// data and <see cref="SimulationTiming"/> rather than written down, because the
/// tick rate is a placeholder (Q1) and a test that hardcoded ticks would be
/// asserting the placeholder rather than the behaviour.
/// </summary>
public class ImpTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;

        public CreatureState Imp => Sim.World.Creatures[0];

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++) Tick();
        }

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    private static Run Start(TilePos impAt, ulong seed = 99, int imps = 1)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        for (int i = 0; i < imps; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);

        var sim = new GameSimulation(database, world, seed, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    /// <summary>Opens a dirt-path corridor, so tests can reach the seams without digging there first.</summary>
    private static void Carve(WorldState world, GameDatabase database, params TilePos[] tiles)
    {
        foreach (TilePos p in tiles)
        {
            ref TileState tile = ref world.Tile(p);
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.Health = database.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }
    }

    /// <summary>
    /// An imp in a sealed pocket of <b>unowned</b> dirt, cut off from its
    /// keeper's room.
    ///
    /// Needed because an imp with nothing marked is not an imp with nothing to
    /// do: standing on its keeper's floor it would start claiming or
    /// reinforcing, which is the point of the priority order. Unowned ground it
    /// cannot leave is the only place a DK2 imp genuinely has no job.
    /// </summary>
    private static Run StartWithNothingToDo(int extraTiles = 0)
    {
        Run run = Start(new TilePos(5, 2));
        GameDatabase database = Dk2TerrainFixture.Database();

        var pocket = new List<TilePos> { new(5, 2) };
        for (int i = 0; i < extraTiles; i++) pocket.Add(new TilePos(5, 3 + i));

        Carve(run.World, database, [.. pocket]);
        return run;
    }

    // ---- doing nothing ---------------------------------------------------

    [Theory]
    [InlineData(TileWorkKind.Reinforce, 1)]
    [InlineData(TileWorkKind.RepairWall, 1)]
    [InlineData(TileWorkKind.Dig, 3)]
    public void WallRepairSharesTheReinforcementFaceLimit(TileWorkKind kind, int limit) =>
        Assert.Equal(limit, ImpCrowding.MaxWorkers(false, kind));

    [Fact]
    public void AnImpWithNothingToDo_StaysWhereItIs()
    {
        Run run = StartWithNothingToDo();
        int x = run.Imp.PosX;
        int y = run.Imp.PosY;

        run.Ticks(200);

        Assert.Equal(CreatureActivity.Idle, run.Imp.Activity);
        Assert.Equal(x, run.Imp.PosX);
        Assert.Equal(y, run.Imp.PosY);
        Assert.Empty(run.Of("job_taken"));
    }

    /// <summary>
    /// <b>Nothing marked is not nothing to do.</b> An imp standing in its
    /// keeper's room with no orders reinforces the walls around it — the second
    /// tier (ADR-0058), and the last thing before it gives up.
    /// </summary>
    [Fact]
    public void AnImpWithNothingMarked_ReinforcesTheWallsAroundIt()
    {
        Run run = Start(new TilePos(2, 2));

        run.Ticks(SimulationTiming.TicksFromSeconds(1));

        SimEvent taken = Assert.Single(run.Of("job_taken"));
        Assert.Contains("Reinforce", taken.Detail);
        Assert.Contains("priority=Reinforce", taken.Detail);
    }

    /// <summary>
    /// CONFIRMED (owner, 2026-09-10): "as soon as the floor tile is claimed
    /// walls are eligible to be reinforced". The keeper's own ground is what
    /// makes a wall worth reinforcing.
    /// </summary>
    [Fact]
    public void AWallBesideTheKeepersOwnFloorIsReinforced()
    {
        Run run = StartOnOwnedFloor();

        run.Ticks(SimulationTiming.TicksFromSeconds(1));

        SimEvent taken = Assert.Single(run.Of("job_taken"));
        Assert.Contains("priority=Reinforce", taken.Detail);
    }

    /// <summary>
    /// The reinforce handler's second pass (kind 16, <c>0x00453E30</c>, ADR-0387): the keeper's own
    /// reinforced wall below its maximum is repaired from the keeper's floor, at
    /// <c>RepairWallHealth</c> 22 stored as 5 and taken at the base rate to 3 an application. Level
    /// 11A's Morgana repairs the wall Drako digs. A wall at full health gives no job.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheKeepersOwnDamagedWallIsRepaired_AFullOneIsNot(bool damaged)
    {
        Run run = StartOnOwnedFloor();
        GameDatabase database = Dk2TerrainFixture.Database();
        ushort full = database.TerrainById(Dk2TerrainFixture.ReinforcedWall).MaxHealth;
        foreach (TilePos side in new TilePos[] { new(9, 5), new(7, 5), new(8, 6), new(8, 4) })
        {
            ref TileState wall = ref run.World.Tile(side);
            wall.TerrainId = Dk2TerrainFixture.ReinforcedWall;
            wall.OwnerPlayerId = Keeper;
            wall.Health = full;
        }
        if (damaged) run.World.Tile(new TilePos(9, 5)).Health = (ushort)(full - 100);

        run.Ticks(SimulationTiming.TicksFromSeconds(3));

        if (!damaged)
        {
            Assert.Empty(run.Of("job_taken"));
            return;
        }
        Assert.Contains("(9,5) RepairWall priority=Reinforce", Assert.Single(run.Of("job_taken")).Detail);
        Assert.True(run.World.Tile(new TilePos(9, 5)).Health > full - 100);
        Assert.Equal(3, TileWorkRules.PerApplication(database, TileWorkKind.RepairWall));
    }

    /// <summary>
    /// A worker on a square of claimed path away from the fixture's own keeper
    /// room, with reinforceable rock around it. The floor must be
    /// <see cref="Dk2TerrainFixture.ClaimedPath"/> and not dirt with an owner
    /// stamped on it: dirt is not <c>Ownable</c>, and the pair would then
    /// differ in terrain as well as in ownership.
    /// </summary>
    private static Run StartOnOwnedFloor(bool owned = true)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        var at = new TilePos(8, 5);
        ref TileState floor = ref world.Tile(at);
        floor.TerrainId = Dk2TerrainFixture.ClaimedPath;
        floor.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        floor.OwnerPlayerId = owned ? Keeper : PlayerIds.Unowned;

        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, at);

        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    /// <summary>DK2's state 10 hands a worker that found nothing to 12 with a 240–479 tick deadline
    /// (imp-idle-and-wandering.md §2); 12 sets out on a trip in 87 that resumes 1, and 1 sends it
    /// back to 10. So a new mark is noticed after the trip, not on a timer: 10 and 12 never read
    /// <c>ImpIdleDelayBeforeReevaluationSeconds</c> (ADR-0224). The pocket reaches (5,4), whose
    /// neighbour (6,4) is markable rock.</summary>
    [Fact]
    public void AnIdleImp_RestsInTwelve_AndLooksAgainOnlyAfterAWanderTrip()
    {
        Run run = StartWithNothingToDo(extraTiles: 2);

        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)
        run.Tick();                                       // first look: nothing to do at all
        Assert.Equal(ImpStates.Resting, run.Imp.Dk2StateId);
        Assert.InRange(run.Imp.StateDeadlineTick - run.World.CurrentTick.Value, 239, 479);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(6, 4)));
        var seen = new List<int>();
        for (int t = 0; t < 40 && !run.Of("job_taken").Any(); t++)
        {
            seen.Add(run.Imp.Dk2StateId);
            run.Tick();
        }

        Assert.Single(run.Of("job_taken"));
        Assert.Contains(ImpStates.Wandering, seen);
    }

    // ---- the priority order (CONFIRMED, owner observation) ----------------

    /// <summary>
    /// Marked excavation outranks claiming <b>however far away it is</b>. The
    /// order is an ordering, not a weighting: the imp walks past claimable floor
    /// on its doorstep to get to a tagged wall across the map.
    /// </summary>
    [Fact]
    public void MarkedWorkOutranksClaiming_AtAnyDistance()
    {
        Run run = Start(new TilePos(2, 2));
        GameDatabase database = Dk2TerrainFixture.Database();

        // Unclaimed floor immediately beside the imp, and a marked wall further off.
        Carve(run.World, database, new TilePos(4, 2), new TilePos(5, 2), new TilePos(6, 2));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(7, 2)));
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        SimEvent taken = run.Of("job_taken").First();
        Assert.Contains("priority=MarkedExcavation", taken.Detail);
        Assert.Contains("(7,2)", taken.Detail);
    }

    [Fact]
    public void ClaimingOutranksReinforcing()
    {
        Run run = Start(new TilePos(2, 2));
        GameDatabase database = Dk2TerrainFixture.Database();

        // Unclaimed floor beside the keeper's room. Rock to reinforce is
        // everywhere; the claim must win anyway.
        Carve(run.World, database, new TilePos(4, 2));

        run.Tick();
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        SimEvent taken = run.Of("job_taken").First();
        Assert.Contains("priority=Claim", taken.Detail);
        Assert.Contains("(4,2)", taken.Detail);
    }

    [Fact]
    public void AnImpClaimsFloorNobodyMarked()
    {
        Run run = Start(new TilePos(2, 2));
        GameDatabase database = Dk2TerrainFixture.Database();
        Carve(run.World, database, new TilePos(4, 2));

        run.Ticks(SimulationTiming.TicksFromSeconds(4));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(Keeper, run.World.Tile(4, 2).OwnerPlayerId);
    }

    /// <summary>
    /// <b>An imp claims a floor tile by standing on it</b> (Q44, CONFIRMED —
    /// <i>"imps claim tiles by jumping on them"</i>). It does not reach across
    /// from the tile it started on, which is what everything here used to do and
    /// what is right only for walls.
    /// </summary>
    [Fact]
    public void AnImpStandsOnTheTileItClaims()
    {
        Run run = Start(new TilePos(3, 2));
        GameDatabase database = Dk2TerrainFixture.Database();
        Carve(run.World, database, new TilePos(4, 2));

        run.Tick();
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)
        Assert.Contains("priority=Claim", run.Of("job_taken").First().Detail);

        // Run to the moment the claim lands, and look at where its feet are.
        for (int i = 0; i < 200 && run.World.Tile(4, 2).TerrainId != Dk2TerrainFixture.ClaimedPath; i++)
            run.Tick();

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(new TilePos(4, 2), run.Imp.Tile);
    }

    /// <summary>
    /// The other half of what Q44 moved: the ownership condition is on the
    /// <b>claimed tile's</b> neighbour, not on the tile the imp is standing on.
    ///
    /// The imp here starts on ground nobody owns. Under the rule this replaces
    /// it would have refused the job outright; what actually decides it is that
    /// (4,2) touches the keeper's room at (3,2). Territory still only ever grows
    /// outwards — it is the imp's feet that were in the wrong place, not the
    /// rule (Q42).
    /// </summary>
    [Fact]
    public void AnImpOnUnownedGroundStillClaims_WhenTheTileTouchesItsKeepersLand()
    {
        Run run = Start(new TilePos(5, 2));
        GameDatabase database = Dk2TerrainFixture.Database();
        Carve(run.World, database, new TilePos(5, 2), new TilePos(4, 2));

        run.Tick();
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        SimEvent taken = run.Of("job_taken").First();
        Assert.Contains("priority=Claim", taken.Detail);
        Assert.Contains("(4,2)", taken.Detail);
    }

    /// <summary>
    /// <i>"then move to an adjacent unclaimed one and do it again"</i> — the rest
    /// of the same observation. Each newly claimed tile is what makes the next
    /// one eligible, so a corridor of unowned floor is taken one tile at a time
    /// rather than all at once or not at all.
    /// </summary>
    [Fact]
    public void ClaimedTerritoryCreepsOneTileAtATime()
    {
        Run run = Start(new TilePos(3, 2));
        GameDatabase database = Dk2TerrainFixture.Database();
        Carve(run.World, database, new TilePos(4, 2), new TilePos(5, 2), new TilePos(6, 2));

        // (6,2) is only reachable as a claim once (5,2) is the keeper's, which
        // is only true once (4,2) is. Three claims in sequence, not three at once.
        run.Ticks(SimulationTiming.TicksFromSeconds(12));

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(5, 2).TerrainId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(6, 2).TerrainId);
    }

    /// <summary>
    /// The stance rule puts a reinforcing imp on an adjacent floor tile, one
    /// worker per face (Q26/Q43), never on the wall.
    /// </summary>
    [Fact]
    public void AnImpReinforcesFromBesideTheWall()
    {
        Run run = Start(new TilePos(2, 2));

        run.Ticks(SimulationTiming.TicksFromSeconds(1));

        Assert.Contains("priority=Reinforce", Assert.Single(run.Of("job_taken")).Detail);

        int width = run.World.Width;
        var target = new TilePos(
            run.Imp.TargetTileIndex % width, run.Imp.TargetTileIndex / width);
        Assert.Equal(1, run.Imp.Tile.ManhattanDistance(target));
    }

    /// <summary>
    /// Claiming and reinforcing spread from ground the keeper already holds. An
    /// imp marooned on unowned floor will not start either, however much of it
    /// is within reach — which is what keeps a dungeon growing outwards from its
    /// heart rather than appearing in islands (Q42).
    /// </summary>
    [Fact]
    public void AnImpWillNotClaimFromGroundItsKeeperDoesNotHold()
    {
        Run run = StartWithNothingToDo(extraTiles: 2);

        run.Ticks(200);

        Assert.Empty(run.Of("job_taken"));
        Assert.Equal(Dk2TerrainFixture.DirtPath, run.World.Tile(5, 3).TerrainId);
    }

    // ---- marked work -----------------------------------------------------

    [Fact]
    public void AnImpWalksToMarkedRock_AndDigsItOut()
    {
        Run run = Start(new TilePos(2, 2));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 2)));
        run.Ticks(SimulationTiming.TicksFromSeconds(8));

        // Rock at 165 health goes in ONE application on the shipped DigRockHealth,
        // as both full-state captures show (2026-09-29). Eight seconds covers the dig
        // and the claim at DK2's work cadence (ADR-0280) and walks that end on their point.
        Assert.Contains(run.Of("destroyed"), e => e.Detail.Contains("(4,2)"));

        // The first job it took was the marked one.
        SimEvent first = run.Of("job_taken").First();
        Assert.Contains("priority=MarkedExcavation", first.Detail);
        Assert.Contains("(4,2)", first.Detail);

        // And then it claimed the floor it had just opened up, because that is
        // what an imp does next when nobody has marked anything else. The whole
        // priority order in one tile.
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(Keeper, run.World.Tile(4, 2).OwnerPlayerId);
    }

    [Fact]
    public void AnImpTakesTheNearestMarkedTile()
    {
        // From (1,1): the tile beside (4,1) is (3,1), two steps away. The tile
        // beside (3,4) is (3,3), four steps away. Both are marked at once, so
        // the choice is the search's and not the order they were marked in.
        Run run = Start(new TilePos(1, 1));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(3, 4), new TilePos(4, 1)));
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)

        SimEvent first = run.Of("job_taken").First();
        Assert.Contains("(4,1)", first.Detail);
    }

    [Fact]
    public void AnImpIgnoresMarkedTilesNothingCanReach()
    {
        // (8,8) is rock in the middle of rock: there is no floor tile beside it,
        // so there is nowhere to stand and no job. Unreachable work is not
        // rejected after the fact — it is never a candidate, because the search
        // walks outwards from the imp and never arrives. The imp is sealed in an
        // unowned pocket so that nothing else distracts it either.
        Run run = StartWithNothingToDo();

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(8, 8)));
        run.Ticks(200);

        Assert.Empty(run.Of("job_taken"));
        Assert.Equal(Dk2TerrainFixture.Rock, run.World.Tile(8, 8).TerrainId);
    }

    [Fact]
    public void UnmarkingATile_TakesTheJobBackOffTheImp()
    {
        Run run = Start(new TilePos(2, 2));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 2)));
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)
        Assert.Single(run.Of("job_taken"));

        run.Tick(TagTilesCommand.Untag(Keeper, new TilePos(4, 2)));

        Assert.Single(run.Of("job_abandoned"));
        Assert.Equal(CreatureActivity.Idle, run.Imp.Activity);
        Assert.Equal(-1, run.Imp.TargetTileIndex);
    }

    [Fact]
    public void AnImpMovesStraightToTheNextMarkedTile_WithoutWaitingOutTheIdleDelay()
    {
        // The idle delay belongs to an imp that looked and found nothing. An imp
        // whose wall has just fallen has not been idle, and in the original it
        // starts on the next marked tile at once rather than standing around for
        // five seconds. Both marked tiles are reachable from the room.
        Run run = Start(new TilePos(2, 2));
        int idleDelay = SimulationTiming.TicksFromSeconds(5);

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 2), new TilePos(4, 3)));
        run.Ticks(SimulationTiming.TicksFromSeconds(4));

        // Only the two marked tiles matter here; once they are gone the imp goes
        // on to claim and reinforce, which is the priority order and not this
        // test's business.
        List<SimEvent> marked = run.Of("job_taken")
            .Where(e => e.Detail.Contains("priority=MarkedExcavation"))
            .ToList();
        Assert.Equal(2, marked.Count);

        long first = marked[0].Tick.Value;
        long second = marked[1].Tick.Value;
        Assert.True(second - first < idleDelay,
            $"took {second - first} ticks to start the next job, the idle delay is {idleDelay}");
    }

    [Fact]
    public void AMarkFromAnotherKeeper_IsNotThisImpsJob()
    {
        // In the sealed pocket, so that the imp has no work of its own to
        // confuse the question: (6,4) is rock it could reach, and the only
        // reason to touch it would be the rival's mark.
        Run run = StartWithNothingToDo(extraTiles: 2);

        run.Tick(TagTilesCommand.Tag(2, new TilePos(6, 4)));
        run.Ticks(200);

        Assert.Empty(run.Of("job_taken"));
        Assert.Equal(Dk2TerrainFixture.Rock, run.World.Tile(6, 4).TerrainId);
    }

    // ---- mining ----------------------------------------------------------

    [Fact]
    public void AnImpMinesAMarkedSeam_SecondSwingFourTicksAfterTheFirst_ThenEveryTick()
    {
        // The seam at (5,5) is 660 health and a swing takes 60 of it (Q47), so
        // eleven applications. What is asserted is the spacing the Level 2 capture
        // measured (ADR-0280): four ticks between the first two, then one. This
        // keeper has no treasury, so the imp mines the seam straight through
        // instead of running off with the first load, as in the original (Q31);
        // the overflow is put down at its feet (Q138, Q140).
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));
        Carve(run.World, database, new TilePos(4, 3), new TilePos(4, 4), new TilePos(4, 5));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(SimulationTiming.TicksFromSeconds(10));

        List<long> at = run.Events.Events
            .Where(e => e.Kind is "gold_mined" or "gold_put_down")
            .Select(e => e.Tick.Value)
            .Distinct()
            .ToList();
        Assert.Equal(11, at.Count);
        Assert.Equal(WorkCadence.SecondApplicationGap(TileWorkKind.MineGold), (int)(at[1] - at[0]));
        Assert.Equal(4, (int)(at[1] - at[0]));
        for (int i = 2; i < at.Count; i++)
            Assert.Equal(1, (int)(at[i] - at[i - 1]));

        // Mined out - and by ten seconds the imp has gone on to claim the floor
        // it opened, which is the priority order doing its job.
        Assert.NotEqual(Dk2TerrainFixture.Gold, run.World.Tile(5, 5).TerrainId);

        // A seam is worth exactly 3000 and the imp's hands hold 1400. The rest is on the
        // floor, all of it: nothing mined is lost.
        Assert.Equal(1400, run.Imp.GoldHeld);
        Assert.Equal(1600, FloorGold.Total(run.World, database));
        Assert.Empty(run.Of("gold_uncollected"));
    }

    [Fact]
    public void AnImpKeepsMiningAGemFace_BecauseItNeverRunsOut()
    {
        // Gems collapse into themselves, so the mark is never satisfied and the
        // imp never runs out of job. This is the behaviour every DK2 player
        // knows, arrived at without gems being special-cased anywhere.
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));
        Carve(run.World, database,
            new TilePos(4, 3), new TilePos(4, 4), new TilePos(4, 5), new TilePos(5, 5));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(6, 5)));
        run.Ticks(SimulationTiming.TicksFromSeconds(10));

        // The mark survives each collapse because the tile is still a gem face
        // and gem faces are markable. A rule that cleared the mark on every
        // conversion would stop the imp after one application.
        Assert.Equal(Dk2TerrainFixture.Gems, run.World.Tile(6, 5).TerrainId);
        Assert.True(run.World.Tile(6, 5).IsTaggedBy(Keeper));
        Assert.True(run.Of("gold_mined").Count() >= 5,
            $"only {run.Of("gold_mined").Count()} applications");
        Assert.Equal(CreatureActivity.Working, run.Imp.Activity);
    }

    // ---- several imps ----------------------------------------------------

    [Fact]
    public void TwoImpsWillWorkTheSameTile_BecauseNothingReservesIt()
    {
        // HYPOTHESIS (Q26). The original visibly lets imps pile onto one wall;
        // whether it is because nothing reserves the job is not established.
        // The test exists so that adding a reservation later is a deliberate
        // change with a failing test attached, not a silent one.
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2), imps: 2);
        Carve(run.World, database, new TilePos(4, 3), new TilePos(4, 4), new TilePos(4, 5));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(SimulationTiming.TicksFromSeconds(1));

        Assert.Equal(2, run.Of("job_taken").Count());
        Assert.All(run.World.Creatures, c => Assert.Equal(5 + (5 * run.World.Width), c.TargetTileIndex));
    }

    // ---- crowding (Q43) ---------------------------------------------------

    [Fact]
    public void ReinforcingAdmitsOnlyOneWorkerOnEachFace()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        TilePos face = new(8, 5);
        TilePos wall = new(8, 4);
        ref TileState floor = ref world.Tile(face);
        floor.TerrainId = Dk2TerrainFixture.ClaimedPath;
        floor.OwnerPlayerId = Keeper;
        floor.Health = database.TerrainById(floor.TerrainId).StartingHealth;
        foreach (TilePos other in new[] { new TilePos(7, 5), new TilePos(9, 5), new TilePos(8, 6) })
            world.Tile(other).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        for (int i = 0; i < 3; i++)
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, face);

        new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems()).Run(4);

        int target = wall.Y * world.Width + wall.X;
        Assert.Single(world.Creatures, c => c.TargetTileIndex == target
            && c.TargetWork == TileWorkKind.Reinforce
            && ImpCrowding.FaceOf(world, c) == face.Y * world.Width + face.X);
    }

    [Fact]
    public void FourAccessibleFacesCanReinforceTheSameWall()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        TilePos wall = new(8, 4);
        for (int y = 2; y <= 6; y++)
            for (int x = 6; x <= 10; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        ref TileState target = ref world.Tile(wall);
        target.TerrainId = Dk2TerrainFixture.Rock;
        target.Health = database.TerrainById(target.TerrainId).StartingHealth;
        TilePos[] faces =
        [
            new(7, 4), new(9, 4), new(8, 3), new(8, 5),
        ];
        foreach (TilePos face in faces)
        {
            ref TileState floor = ref world.Tile(face);
            floor.TerrainId = Dk2TerrainFixture.ClaimedPath;
            floor.OwnerPlayerId = Keeper;
            floor.Health = database.TerrainById(floor.TerrainId).StartingHealth;
            world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, face);
        }

        new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems()).Run(4);

        int targetIndex = wall.Y * world.Width + wall.X;
        Assert.Equal(4, world.Creatures.Count(c => c.TargetTileIndex == targetIndex
            && c.TargetWork == TileWorkKind.Reinforce));
        Assert.Equal(4, world.Creatures.Select(c => ImpCrowding.FaceOf(world, c)).Distinct().Count());
    }

    /// <summary>
    /// <b>Three workers to a face, and a fourth goes elsewhere.</b> CONFIRMED by
    /// the owner (Q26/Q43): three imps fit on one side of a block and twelve on
    /// the whole block, and an imp that cannot get on <b>finds other work rather
    /// than queueing</b>.
    ///
    /// <para>Four imps are dropped on the same tile beside the same marked seam.
    /// Three take it from where they stand; the fourth cannot, and the assertion
    /// is that it does not simply stand there — it is either working the same
    /// block from a different side, or working something else, or idle. What it
    /// must not be is a fourth worker on that face.</para>
    /// </summary>
    [Fact]
    public void AFourthImpCannotJoinAFullFace()
    {
        // The fixture's only floor is the claimed 3x3 room, so (3,3) is the
        // corner of it and (4,3) is the rock face beside it.
        Run run = Start(new TilePos(3, 3), imps: 4);
        int marked = (3 * run.World.Width) + 4;
        int face = (3 * run.World.Width) + 3;

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 3)));

        // ONE tick: long enough for the jobs to be handed out, short enough that
        // no swing has landed. A swing is one tick now that Q1 is settled
        // (ADR-0089), and a plain wall falls in a single swing, so waiting any
        // longer leaves nothing to be crowded around.
        run.Ticks(1);

        List<CreatureState> onThatFace = run.World.Creatures
            .Where(c => c.TargetTileIndex == marked && ImpCrowding.FaceOf(run.World, c) == face)
            .ToList();

        Assert.Equal(ImpCrowding.MaxWorkersPerFace, onThatFace.Count);
        Assert.Equal(4, run.World.Creatures.Count);

        // And the fourth went and found something else rather than waiting -
        // which is the half of Q43 that matters. Rock beside the keeper's own
        // land can be reinforced, so there is other work to find.
        CreatureState fourth = run.World.Creatures.Single(c => !onThatFace.Contains(c));
        Assert.NotEqual(-1, fourth.TargetTileIndex);
    }

    /// <summary>
    /// The cap is on the face, not on the block: the same seam approached from a
    /// different tile is a different face and takes its own three. This is what
    /// makes the owner's "twelve on a block" fall out of the three without
    /// anything counting to twelve.
    /// </summary>
    [Fact]
    public void ADifferentFaceOfTheSameBlockIsNotFull()
    {
        Run run = Start(new TilePos(3, 3), imps: 3);
        int marked = (3 * run.World.Width) + 4;

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 3)));

        // ONE tick: long enough for the jobs to be handed out, short enough that
        // no swing has landed. A swing is one tick now that Q1 is settled
        // (ADR-0089), and a plain wall falls in a single swing, so waiting any
        // longer leaves nothing to be crowded around.
        run.Ticks(1);

        // The face they are on is full...
        Assert.True(ImpCrowding.FaceIsFull(
            run.World, Dk2TerrainFixture.Database(), marked, new TilePos(3, 3), askingCreatureId: -1, workedFromTheTile: false));

        // ...and every other side of the same block is not.
        foreach (TilePos other in new[]
                 {
                     new TilePos(5, 3), new TilePos(4, 2), new TilePos(4, 4),
                 })
        {
            Assert.False(
                ImpCrowding.FaceIsFull(
                    run.World, Dk2TerrainFixture.Database(), marked, other, askingCreatureId: -1, workedFromTheTile: false),
                $"{other} should not be full");
        }
    }

    /// <summary>
    /// Each keeper has its own task pool (<c>player+0x1105</c>), so three of this keeper's imps on
    /// a face crowd only this keeper's workers: a rival's imp beside them sees the face free and
    /// can take none of their slots or tasks (ADR-0388, superseding ADR-0026's every-player
    /// count). Level 11A's Drako stole Morgana's claim of the hole in her wall this way.
    /// </summary>
    [Fact]
    public void ARivalKeepersImpNeitherFillsAFaceNorHoldsItsTask()
    {
        Run run = Start(new TilePos(3, 3), imps: 3);
        int marked = (3 * run.World.Width) + 4;
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 3)));
        run.Ticks(1);
        Assert.Equal(3, run.World.Creatures.Count(c => c.TargetTileIndex == marked));
        CreatureState mine = run.World.Creatures.First(c => c.TargetTileIndex == marked);

        CreatureState rival = run.World.SpawnCreature(
            Dk2TerrainFixture.Database(), Dk2TerrainFixture.ImpCreatureId, (byte)(Keeper + 1), new TilePos(3, 3));

        Assert.True(ImpCrowding.FaceIsFull(
            run.World, Dk2TerrainFixture.Database(), marked, new TilePos(3, 3), askingCreatureId: -1, workedFromTheTile: false));
        Assert.False(ImpCrowding.FaceIsFull(
            run.World, Dk2TerrainFixture.Database(), marked, new TilePos(3, 3), rival.Id, workedFromTheTile: false));
        Assert.False(TaskStealing.InPool(TaskStealing.PoolOf(run.World, rival.Id), mine));
        Assert.True(TaskStealing.InPool(TaskStealing.PoolOf(run.World, mine.Id), run.World.Creatures.Last(c => c.TargetTileIndex == marked)));
    }

    /// <summary>
    /// An imp re-evaluating the job it already holds must not count itself out of
    /// its own face. Without the exclusion, the third imp on a face would drop
    /// its job the moment it looked at it again.
    /// </summary>
    [Fact]
    public void AnImpDoesNotCountItselfOutOfItsOwnFace()
    {
        Run run = Start(new TilePos(3, 3), imps: 3);
        int marked = (3 * run.World.Width) + 4;

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 3)));

        // ONE tick: long enough for the jobs to be handed out, short enough that
        // no swing has landed. A swing is one tick now that Q1 is settled
        // (ADR-0089), and a plain wall falls in a single swing, so waiting any
        // longer leaves nothing to be crowded around.
        run.Ticks(1);

        CreatureState[] working = [.. run.World.Creatures.Where(c => c.TargetTileIndex == marked)];
        Assert.Equal(3, working.Length);

        foreach (CreatureState imp in working)
        {
            Assert.False(
                ImpCrowding.FaceIsFull(
                    run.World, Dk2TerrainFixture.Database(), marked, new TilePos(3, 3), imp.Id, workedFromTheTile: false),
                $"imp {imp.Id} counts itself out of its own face");
        }

        // And they keep it for as long as the block lasts: it goes on being dug
        // rather than being dropped and retaken every tick. Ticked one at a time
        // against the terrain rather than for a fixed count, because three imps
        // swinging together put 300 damage into a plain wall's 165 and the block
        // falls fast — a fixed count passed only while the crowding cap was
        // wrongly 3 for a job worked ON the tile (Q44, OnlyOneImpClaimsOneTile).
        for (int i = 0; i < 8; i++)
        {
            run.Tick();
            if (run.World.Tile(4, 3).TerrainId != Dk2TerrainFixture.Rock) break;
            Assert.Equal(3, run.World.Creatures.Count(c => c.TargetTileIndex == marked));
        }

        // And the block really did fall, so the loop above was not vacuous.
        Assert.NotEqual(Dk2TerrainFixture.Rock, run.World.Tile(4, 3).TerrainId);
    }

    // ---- movement --------------------------------------------------------

    [Fact]
    public void AnImpFinishesItsWalkNearTheTargetAndNeverSnapsToTheCentre()
    {
        // Follow_Path's imp arm (steering-behaviours.md §5.2) finishes a route once the body is
        // within 0.1 tile of the target, at any speed; that tick still moves it by its braking
        // velocity, and nothing puts it on the centre afterwards unless the request carried the
        // snap flag, which none does yet.
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [Keeper]);
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        Assert.True(new Navigation.WorldSteering(database, world).TryRoute(imp, new TilePos(2, 1), Navigation.MoveOptions.Plain));

        var sim = new GameSimulation(database, world, 1, [new CreatureMovementSystem()]);
        int ticks = 0;
        while (imp.HasRoute && ticks < 20)
        {
            sim.Tick();
            ticks++;
        }

        Assert.False(imp.HasRoute);
        Assert.InRange(ticks, 2, 3);
        Assert.InRange(imp.PosX, CreatureState.CentreOf(2) - (Fp32.One / 10), CreatureState.CentreOf(3));
        Assert.NotEqual(CreatureState.CentreOf(2), imp.PosX);
        Assert.Equal(CreatureState.CentreOf(1), imp.PosY);
    }

    /// <summary>Two floor tiles that touch only at a corner share no edge, so the mesh has no gate
    /// between them and the router no route — as in DK2, whose region check refuses such a pair
    /// before any search (Q28, 2026-09-17). Whether the mover can still be steered through the
    /// corner is Q28's open half and is not what this pins.</summary>
    [Fact]
    public void TheMeshHasNoRouteBetweenTilesThatTouchOnlyAtACorner_AsTheOriginalRefusesItToo()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [Keeper]);
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));

        // The fixture's room is (1,1)-(3,3); a chain of tiles on the bias leads out of its corner.
        for (int i = 1; i <= 3; i++) Carve(world, database, new TilePos(3 + i, 3 + i));

        Assert.False(new Navigation.WorldSteering(database, world).TryRoute(imp, new TilePos(6, 6), Navigation.MoveOptions.Plain));
    }

    [Fact]
    public void ASecondOfWalkingCoversTheRecordsSpeed()
    {
        // The speed setter (0x0048D010) caps the body at the definition's speed divided by the
        // tick rate, and an imp reaches that cap on its first tick, so a second on a straight
        // corridor covers the record's 7536: 1.84 tiles a second (Q29).
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [Keeper]);
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));

        // A long straight route so nothing snaps to a waypoint mid-second, and
        // an open corridor to walk it down — a route over rock would be dropped
        // at the first solid waypoint (see WallsBlockMovementTests).
        for (int x = 2; x <= 14; x++) Carve(world, database, new TilePos(x, 1));
        Assert.True(new Navigation.WorldSteering(database, world).TryRoute(imp, new TilePos(14, 1), Navigation.MoveOptions.Plain));

        var sim = new GameSimulation(database, world, 1, [new CreatureMovementSystem()]);
        int start = imp.PosX;
        sim.Run(SimulationTiming.TicksPerSecond);

        // The body's cap is the record's speed over the tick rate in 16.16, and the velocity is
        // clamped to it by the original's three-band truncation, which lands a fraction over on a
        // straight run; every tick then moves that clamped amount, shifted back to entity units.
        int perTick = CreatureMovementSystem.MaxSpeedOf(database, Dk2TerrainFixture.Imp(), imp.Level) >> 4;
        Assert.Equal(Dk2TerrainFixture.Imp().Speed.Raw, SimulationTiming.TicksPerSecond * perTick);
        Assert.InRange(imp.PosX - start, Dk2TerrainFixture.Imp().Speed.Raw, Dk2TerrainFixture.Imp().Speed.Raw * 101 / 100);
    }

    // ---- the gate --------------------------------------------------------

    [Fact]
    public void ImpsAreDeterministicUnderIdenticalInput()
    {
        static ulong Play()
        {
            Run run = Start(new TilePos(2, 2), imps: 3);
            run.Tick(TagTilesCommand.Rect(Keeper, 4, 1, 4, 3));
            run.Ticks(120);
            return run.Sim.StateHash();
        }

        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void AnImpMidJob_SurvivesASnapshot()
    {
        Run run = Start(new TilePos(2, 2));
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 3)));
        run.Ticks(4);

        byte[] saved = WorldSnapshot.ToBytes(run.Sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            saved, Dk2TerrainFixture.Database(), Dk2TerrainFixture.DefaultSystems());

        Assert.Equal(run.Sim.StateHash(), restored.StateHash());

        // Continuation, not byte equality: a restored imp that re-planned its
        // route could pick a different equally short one and drift apart later.
        for (int i = 0; i < 60; i++)
        {
            run.Tick();
            restored.Tick();
            Assert.Equal(run.Sim.StateHash(), restored.StateHash());
        }
    }

    /// <summary>
    /// <b>One claiming imp per unclaimed tile</b> — CONFIRMED (owner, 2026-09-03:
    /// <i>"only one claiming imp jumping per unclaimed tile is allowed"</i>).
    ///
    /// <para>Same crowding machinery as wall work (Q43) with a different cap.
    /// Digging admits three per face, reinforcing one, and claiming one on the
    /// target tile. The cap belongs to the job and stance; see
    /// <see cref="ImpCrowding"/>.</para>
    /// </summary>
    [Fact]
    public void OnlyOneImpClaimsOneTile()
    {
        Run run = Start(new TilePos(3, 3), imps: 3);
        GameDatabase database = Dk2TerrainFixture.Database();

        // One claimable tile beside the keeper's room, and three imps able to
        // reach it. Rock to reinforce is everywhere, so the surplus has
        // somewhere else to go.
        Carve(run.World, database, new TilePos(4, 3));
        int target = (3 * run.World.Width) + 4;

        run.Ticks(2);

        Assert.Equal(1, run.World.Creatures.Count(c => c.TargetTileIndex == target));

        // And the other two did not queue: they went and found other work, which
        // is the half of Q43 that matters and holds here too.
        foreach (CreatureState other in run.World.Creatures.Where(c => c.TargetTileIndex != target))
            Assert.NotEqual(-1, other.TargetTileIndex);
    }

    /// <summary>
    /// The cap follows the stance, not the tile: the very same tile admits three
    /// workers while it is a wall to be dug and one once it is floor to be
    /// claimed. Asserted on one tile, so the two numbers cannot be put down to the
    /// tiles being different.
    /// </summary>
    [Fact]
    public void TheSameTileTakesThreeDiggersAndOneClaimer()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        int target;

        // As a wall: three.
        Run wall = Start(new TilePos(3, 3), imps: 3);
        target = (3 * wall.World.Width) + 4;
        wall.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, 3)));
        wall.Ticks(1);   // one tick: a swing is one tick now, and one swing ends a wall
        Assert.Equal(3, wall.World.Creatures.Count(c => c.TargetTileIndex == target));

        // The same tile as floor: one.
        Run floor = Start(new TilePos(3, 3), imps: 3);
        Carve(floor.World, database, new TilePos(4, 3));
        floor.Ticks(2);
        Assert.Equal(1, floor.World.Creatures.Count(c => c.TargetTileIndex == target));
    }

    // ---- taking a rival's ground (Q68) --------------------------------

    /// <summary>
    /// <b>A rival's floor is attacked down to Dirt Path, then claimed</b> —
    /// CONFIRMED (owner, 2026-09-25): the imp jumps on it until it is unclaimed
    /// dirt, then claims it. It is not claimed directly; Q68's direct take was
    /// a bridge, which is a room.
    /// </summary>
    [Fact]
    public void ARivalsFloorIsAttackedToDirtBeforeItIsClaimed()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));

        // A rival's claimed floor just outside the keeper's room.
        ref TileState theirs = ref run.World.Tile(4, 2);
        theirs.TerrainId = Dk2TerrainFixture.ClaimedPath;
        theirs.OwnerPlayerId = 2;
        theirs.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;

        Assert.False(TileWorkRules.Applies(database, theirs, TileWorkKind.Claim, Keeper));
        Assert.True(TileWorkRules.Applies(database, theirs, TileWorkKind.AttackTile, Keeper));

        for (int i = 0; i < 5000 && theirs.TerrainId == Dk2TerrainFixture.ClaimedPath; i++)
            TileWorkRules.Apply(database, ref theirs, TileWorkKind.AttackTile, Keeper);

        Assert.Equal(Dk2TerrainFixture.DirtPath, theirs.TerrainId);
        Assert.Equal(PlayerIds.Neutral, theirs.OwnerPlayerId);
        Assert.True(TileWorkRules.Applies(database, theirs, TileWorkKind.Claim, Keeper));
    }

    /// <summary>
    /// <c>0x0046AB50</c> refuses a tile whose navigation byte carries a door, so a rival's
    /// locked door on its floor is never attacked (imp-claim-path.md §4). Without the gate ours
    /// took the job, found no route onto the door and dropped it, again and again: Level 4's hero
    /// door at (24,27) drew about 5,000 such takes from seven imps over 3,000 turns (2026-10-12).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARivalsLockedDoorTileIsNeverAttacked(bool door)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));
        ref TileState theirs = ref run.World.Tile(4, 2);
        theirs.TerrainId = Dk2TerrainFixture.ClaimedPath;
        theirs.OwnerPlayerId = 2;
        theirs.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        if (door)
            ThingProjection.Place(run.World, database, [new DoorThing(new byte[20])
            {
                X = 4, Y = 2, DoorId = 1, PlayerId = 2, Flags = 0x01, Unknown_0011 = new byte[3],
            }]);

        run.Ticks(200);

        bool attacked = run.Of("job_taken").Any(e => e.Detail.Contains("(4,2) AttackTile", StringComparison.Ordinal));
        Assert.Equal(!door, attacked);
    }

    /// <summary>
    /// A take fails when its walk cannot start (<c>0x004651A0</c> → <c>0x004D0F60</c>,
    /// imp-task-stealing.md §7, Q588): the claimable dirt at (6,2) lies past a rival's locked
    /// door, which the selection grid walks through and the router does not, so the imp never
    /// takes it and is never left to drop it as out of reach.
    /// </summary>
    [Fact]
    public void AClaimBeyondARivalsLockedDoorIsNeverTaken()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));
        void Lay(int x, byte terrain, byte owner)
        {
            ref TileState tile = ref run.World.Tile(x, 2);
            tile.TerrainId = terrain;
            tile.OwnerPlayerId = owner;
            tile.Health = database.TerrainById(terrain).StartingHealth;
        }
        Lay(4, Dk2TerrainFixture.ClaimedPath, 2);
        Lay(5, Dk2TerrainFixture.ClaimedPath, Keeper);
        Lay(6, Dk2TerrainFixture.DirtPath, PlayerIds.Neutral);
        ThingProjection.Place(run.World, database, [new DoorThing(new byte[20])
        {
            X = 4, Y = 2, DoorId = 1, PlayerId = 2, Flags = 0x01, Unknown_0011 = new byte[3],
        }]);

        run.Ticks(200);

        Assert.DoesNotContain(run.Of("job_taken"), e => e.Detail.Contains("(6,2)", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Of("job_abandoned"), e => e.Detail.Contains("(6,2)", StringComparison.Ordinal));
        Assert.Contains(run.Of("job_unreachable"), e => e.Detail.Contains("(6,2) Claim", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>An imp takes a rival's ground on its own</b>, in the two steps the owner
    /// watched: first unclaimed dirt, then the imp's keeper's floor. Claiming is
    /// second in the priority order (ADR-0018), the tile must touch its keeper's
    /// territory (Q42), and the imp stands on the tile it works (Q44).
    /// </summary>
    [Fact]
    public void AnImpTakesARivalsFloorWithoutBeingTold()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));

        ref TileState theirs = ref run.World.Tile(4, 2);
        theirs.TerrainId = Dk2TerrainFixture.ClaimedPath;
        theirs.OwnerPlayerId = 2;
        theirs.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;

        run.Tick();
        run.Tick(); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)
        SimEvent taken = run.Of("job_taken").First();
        Assert.Contains("priority=Claim", taken.Detail);
        Assert.Contains("(4,2)", taken.Detail);

        bool wasDirt = false;
        for (int i = 0; i < SimulationTiming.TicksFromSeconds(20)
                        && run.World.Tile(4, 2).OwnerPlayerId != Keeper; i++)
        {
            run.Tick();
            TileState now = run.World.Tile(4, 2);
            if (now.TerrainId == Dk2TerrainFixture.DirtPath && now.OwnerPlayerId == PlayerIds.Neutral)
                wasDirt = true;
        }

        Assert.True(wasDirt, "the rival's floor never passed through unclaimed dirt");
        Assert.Equal(Keeper, run.World.Tile(4, 2).OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
    }

    /// <summary>
    /// A keeper does not claim ground it already holds — otherwise every imp
    /// would spend for ever re-claiming its own dungeon.
    /// </summary>
    [Fact]
    public void AKeeperDoesNotClaimItsOwnGround()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));

        Assert.False(TileWorkRules.Applies(
            database, run.World.Tile(2, 2), TileWorkKind.Claim, Keeper));
    }
}
