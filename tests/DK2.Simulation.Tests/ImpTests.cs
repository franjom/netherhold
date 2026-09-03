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

        run.Ticks(SimulationTiming.TicksFromSeconds(1));

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
    /// The stance rule has to keep giving the old answer for walls, or Q27's
    /// three-imps-a-side observation stops making sense. An imp reinforcing rock
    /// stands beside it, never on it.
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
        run.Ticks(SimulationTiming.TicksFromSeconds(4));

        // Rock at 165 health takes TWO applications - the count the owner
        // measured in the original, which the ObservedCorrections entry for
        // DigRockHealth reproduces (ADR-0023, Q20). Four seconds is ample.
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
    public void AnImpMinesAMarkedSeam_OneApplicationEveryWorkPeriod()
    {
        // The seam at (5,5) is 660 health and a swing takes 60 of it (Q47), so
        // it takes eleven applications. What is asserted is the spacing: the gap
        // between applications is one work period, whatever a period turns out
        // to be worth in seconds (Q19/Q1).
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
        run.Ticks(SimulationTiming.TicksFromSeconds(10));

        List<long> at = run.Events.Events
            .Where(e => e.Kind is "gold_mined" or "gold_uncollected")
            .Select(e => e.Tick.Value)
            .Distinct()
            .ToList();
        Assert.Equal(11, at.Count);
        for (int i = 1; i < at.Count; i++)
            Assert.Equal(SimulationTiming.WorkApplicationPeriodTicks, (int)(at[i] - at[i - 1]));

        // Mined out - and by ten seconds the imp has gone on to claim the floor
        // it opened, which is the priority order doing its job.
        Assert.NotEqual(Dk2TerrainFixture.Gold, run.World.Tile(5, 5).TerrainId);

        // A seam is worth exactly 3000 and the imp's hands hold 1400. Everything
        // above that is gold DK2 would have left on the floor and we cannot.
        Assert.Equal(1400, run.Imp.GoldHeld);
        Assert.Equal(1600, run.Of("gold_uncollected")
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

        // Three ticks: long enough for the jobs to be handed out, short enough
        // that no swing has landed. A plain wall falls in two swings and a swing
        // is four ticks (ADR-0023, ADR-0020), so waiting longer would leave
        // nothing to be crowded around.
        run.Ticks(3);

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

        // Three ticks: long enough for the jobs to be handed out, short enough
        // that no swing has landed. A plain wall falls in two swings and a swing
        // is four ticks (ADR-0023, ADR-0020), so waiting longer would leave
        // nothing to be crowded around.
        run.Ticks(3);

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

        // Three ticks: long enough for the jobs to be handed out, short enough
        // that no swing has landed. A plain wall falls in two swings and a swing
        // is four ticks (ADR-0023, ADR-0020), so waiting longer would leave
        // nothing to be crowded around.
        run.Ticks(3);

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
        // rather than being dropped and retaken every tick.
        //
        // Ticked one at a time against the terrain rather than for a fixed count,
        // because three imps swinging together put 300 damage into a plain wall's
        // 165 and the block falls fast. A fixed count here used to pass only
        // because the crowding cap was wrongly 3 for a job worked ON the tile: it
        // was reading three imps claiming the floor they had just opened, which
        // is a thing the original does not do (Q44, OnlyOneImpClaimsOneTile).
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

    /// <summary>
    /// <b>A diagonal leg is longer, and the mover charges for it.</b> Walking
    /// four tiles on the bias covers 4 x root-2 tile-widths, so it takes about
    /// 1.41x as long as walking four tiles straight — not the same time (which a
    /// mover using Chebyshev distance would give) and not twice (which the
    /// Manhattan distance it used to use would give).
    ///
    /// <para>Forced rather than chosen: a creature's speed is in tiles per second
    /// (Q29), so it moves continuously through world space at a constant rate and
    /// a longer leg simply takes longer. It also has to agree with
    /// <see cref="GridPathfinder"/>, which costed the route at 14/10 — a router
    /// and a mover that disagree produce creatures that arrive early. See Q48 and
    /// ADR-0025.</para>
    /// </summary>
    [Fact]
    public void WalkingOnTheBias_TakesRootTwoTimesAsLong()
    {
        static int TicksToWalk(bool diagonal)
        {
            GameDatabase database = Dk2TerrainFixture.Database();
            WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [Keeper]);
            CreatureState imp = world.SpawnCreature(
                Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));

            for (int i = 1; i <= 4; i++)
                imp.Path.Add(new TilePos(1 + i, diagonal ? 1 + i : 1));

            var sim = new GameSimulation(database, world, 1, [new CreatureMovementSystem()]);
            for (int tick = 1; tick <= 10_000; tick++)
            {
                sim.Tick();
                if (imp.PathIndex >= imp.Path.Count) return tick;
            }

            throw new InvalidOperationException("the imp never arrived");
        }

        int straight = TicksToWalk(diagonal: false);
        int bias = TicksToWalk(diagonal: true);

        // 14/10 is the integer stand-in for root-2 that both the mover and the
        // search use, so the ratio lands on it rather than on 1.41421...
        Assert.Equal(
            straight * GridPathfinder.DiagonalCost / GridPathfinder.StraightCost,
            bias);

        // Stated again as the property, because the numbers above would also be
        // satisfied by two coincidences: the bias walk is strictly slower than
        // the straight one and strictly faster than walking it as an L.
        Assert.True(bias > straight, $"bias {bias} was not slower than straight {straight}");
        Assert.True(bias < straight * 2, $"bias {bias} was not faster than an L of {straight * 2}");
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

    /// <summary>
    /// <b>One claiming imp per unclaimed tile</b> — CONFIRMED (owner, 2026-09-03:
    /// <i>"only one claiming imp jumping per unclaimed tile is allowed"</i>).
    ///
    /// <para>Same crowding machinery as the three-a-face wall cap (Q43) with a
    /// different number, and the difference is <b>observed rather than
    /// derived</b>. Three imps really do share one tile to get at a wall; only one
    /// may stand on a floor tile to claim it. So the cap is per job, not "how many
    /// bodies fit on a tile" — see <see cref="ImpCrowding"/>.</para>
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
        wall.Ticks(2);
        Assert.Equal(3, wall.World.Creatures.Count(c => c.TargetTileIndex == target));

        // The same tile as floor: one.
        Run floor = Start(new TilePos(3, 3), imps: 3);
        Carve(floor.World, database, new TilePos(4, 3));
        floor.Ticks(2);
        Assert.Equal(1, floor.World.Creatures.Count(c => c.TargetTileIndex == target));
    }

    // ---- taking a rival's ground (Q68) -----------------------------------

    /// <summary>
    /// <b>A rival's floor is claimed directly, with no neutral step</b> —
    /// CONFIRMED (Q68, owner 2026-09-03), and it refutes the two-step model this
    /// project had: attack the tile to neutral with <c>AttackTileHealth</c>, then
    /// claim the neutral tile.
    ///
    /// <para>The tile keeps its terrain and changes hands. Following
    /// <c>MaxHealthTypeTerrainId</c> as a claim normally does would have turned
    /// their floor into <b>Rock</b>, since that field is Claimed Path's fill-in
    /// destination (Q18).</para>
    /// </summary>
    [Fact]
    public void ARivalsFloorIsClaimedDirectlyAndStaysFloor()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Run run = Start(new TilePos(2, 2));

        // A rival's claimed floor just outside the keeper's room.
        ref TileState theirs = ref run.World.Tile(4, 2);
        theirs.TerrainId = Dk2TerrainFixture.ClaimedPath;
        theirs.OwnerPlayerId = 2;
        theirs.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;

        Assert.True(TileWorkRules.Applies(database, theirs, TileWorkKind.Claim, Keeper));

        for (int i = 0; i < 5000 && theirs.OwnerPlayerId != Keeper; i++)
            TileWorkRules.Apply(database, ref theirs, TileWorkKind.Claim, Keeper);

        Assert.Equal(Keeper, theirs.OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, theirs.TerrainId);
    }

    /// <summary>
    /// <b>An imp takes a rival's ground on its own</b>, which is hostile
    /// expansion falling out of rules that were already here: claiming is second
    /// in the priority order (ADR-0018), the tile must touch its keeper's
    /// territory (Q42), and the imp stands on the tile it claims (Q44). Nothing
    /// new decides to do it.
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
        SimEvent taken = run.Of("job_taken").First();
        Assert.Contains("priority=Claim", taken.Detail);
        Assert.Contains("(4,2)", taken.Detail);

        run.Ticks(SimulationTiming.TicksFromSeconds(4));
        Assert.Equal(Keeper, run.World.Tile(4, 2).OwnerPlayerId);
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
