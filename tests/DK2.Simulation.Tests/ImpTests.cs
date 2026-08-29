using DK2.Core;
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
    private const byte Keeper = 1;

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
            database, Dk2TerrainFixture.Map(), [(byte)0, Keeper, (byte)2]);

        for (int i = 0; i < imps; i++)
            world.SpawnCreature(Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);

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
    /// keeper's room with no orders reinforces the walls around it — third in
    /// the priority order, and the last thing before it gives up.
    /// </summary>
    [Fact]
    public void AnImpWithNothingMarked_ReinforcesTheWallsAroundIt()
    {
        Run run = Start(new TilePos(2, 2));

        run.Ticks(SimulationTiming.WorkApplicationPeriodTicks * 2);

        SimEvent taken = Assert.Single(run.Of("job_taken"));
        Assert.Contains("Reinforce", taken.Detail);
        Assert.Contains("priority=Reinforce", taken.Detail);
    }

    [Fact]
    public void AnIdleImp_LooksForWorkOnTheIntervalTheDataNames()
    {
        // ImpIdleDelayBeforeReevaluationSeconds is 5. An imp that found nothing
        // on its first look must not notice a new mark before that delay is up,
        // and must notice it once the delay has passed. The pocket reaches (5,4),
        // whose neighbour (6,4) is markable rock.
        Run run = StartWithNothingToDo(extraTiles: 2);
        int delay = SimulationTiming.TicksFromSeconds(5);

        run.Tick();                                       // first look: nothing to do at all
        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(6, 4)));

        run.Ticks(delay - 2);
        Assert.Empty(run.Of("job_taken"));

        run.Ticks(2);
        Assert.Single(run.Of("job_taken"));
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

        run.Ticks(SimulationTiming.WorkApplicationPeriodTicks * 2);

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, run.World.Tile(4, 2).TerrainId);
        Assert.Equal(Keeper, run.World.Tile(4, 2).OwnerPlayerId);
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
        run.Ticks(SimulationTiming.WorkApplicationPeriodTicks * 3);

        // Rock at 165 health against DigRockHealth -5000: one application ends
        // it (that is Q20, not a bug here).
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
        run.Ticks(SimulationTiming.WorkApplicationPeriodTicks * 2);

        Assert.Equal(2, run.Of("job_taken").Count());

        long first = run.Of("job_taken").First().Tick.Value;
        long second = run.Of("job_taken").Last().Tick.Value;
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
    public void AnImpMinesAMarkedSeam_OneApplicationEveryWorkPeriod()
    {
        // The seam at (5,5) is 660 health against MineGoldHealth -300, so it
        // takes three applications. What is asserted is the spacing: the gap
        // between applications is one work period, whatever a period turns out
        // to be worth in seconds (Q19).
        //
        // This keeper has no treasury, which is why the imp mines the seam
        // straight through instead of running off with the first load. An imp
        // with nowhere to put gold keeps mining in the original too (Q31); what
        // is missing here is the floor lump the original leaves behind, so the
        // overflow is reported as uncollected and lost.
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));
        Carve(run.World, database, new TilePos(4, 3), new TilePos(4, 4), new TilePos(4, 5));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));
        run.Ticks(SimulationTiming.WorkApplicationPeriodTicks * 6);

        List<long> at = run.Events.Events
            .Where(e => e.Kind is "gold_mined" or "gold_uncollected")
            .Select(e => e.Tick.Value)
            .Distinct()
            .ToList();
        Assert.Equal(3, at.Count);
        Assert.Equal(SimulationTiming.WorkApplicationPeriodTicks, (int)(at[1] - at[0]));
        Assert.Equal(SimulationTiming.WorkApplicationPeriodTicks, (int)(at[2] - at[1]));
        Assert.Equal(Dk2TerrainFixture.DirtPath, run.World.Tile(5, 5).TerrainId);

        // A seam is worth 3000 and the imp's hands hold 1400. Everything above
        // that is gold DK2 would have left on the floor and we cannot.
        Assert.Equal(1400, run.Imp.GoldHeld);
        Assert.Equal(1598, run.Of("gold_uncollected")
            .Sum(e => int.Parse(e.Detail.Split("amount=")[1].Split(' ')[0])));
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
        run.Ticks(SimulationTiming.WorkApplicationPeriodTicks * 12);

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
        run.Ticks(SimulationTiming.WorkApplicationPeriodTicks * 2);

        Assert.Equal(2, run.Of("job_taken").Count());
        Assert.All(run.World.Creatures, c => Assert.Equal(5 + (5 * run.World.Width), c.TargetTileIndex));
    }

    // ---- movement --------------------------------------------------------

    [Fact]
    public void AnImpCoversATileAtTheSpeedItsDataGives()
    {
        // The Imp's speed is 7536 in DK2's 4096-denominator fixed point: 1.84
        // tiles per second (STRONGLY INFERRED — see CreatureMovementSystem).
        // Crossing one tile therefore takes ceil(4096 * ticksPerSecond / 7536)
        // ticks, and nothing about that number is written into the simulation.
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [Keeper]);
        CreatureState imp = world.SpawnCreature(Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        imp.Path.Add(new TilePos(2, 1));

        var sim = new GameSimulation(database, world, 1, [new CreatureMovementSystem()]);

        int speed = Dk2TerrainFixture.Imp().Speed.Raw;
        int expected = ((Fp32.One * SimulationTiming.TicksPerSecond) + speed - 1) / speed;

        for (int i = 0; i < expected - 1; i++) sim.Tick();
        Assert.NotEqual(CreatureState.CentreOf(2), imp.PosX);

        sim.Tick();
        Assert.Equal(CreatureState.CentreOf(2), imp.PosX);
        Assert.Equal(CreatureState.CentreOf(1), imp.PosY);
    }

    [Fact]
    public void SpeedDoesNotDriftFromRoundingItAwayEveryTick()
    {
        // 7536 does not divide by the tick rate, and throwing the remainder away
        // each tick would leave every creature permanently slower than its data.
        // Over a whole second the distance covered must be exactly the speed.
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [Keeper]);
        CreatureState imp = world.SpawnCreature(Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));

        // A long straight route so nothing snaps to a waypoint mid-second.
        for (int x = 2; x <= 14; x++) imp.Path.Add(new TilePos(x, 1));

        var sim = new GameSimulation(database, world, 1, [new CreatureMovementSystem()]);
        int start = imp.PosX;
        sim.Run(SimulationTiming.TicksPerSecond);

        Assert.Equal(Dk2TerrainFixture.Imp().Speed.Raw, imp.PosX - start);
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
}
