using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The M3 tile mechanics, against DK2's own numbers (see
/// <see cref="Dk2TerrainFixture"/>). Every expected value below is arithmetic on
/// the shipped data, never a number chosen to make a test pass.
/// </summary>
public class TileWorkTests
{
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static TileState TileOf(byte terrainId, byte owner = 0)
    {
        TerrainDef t = Db.TerrainById(terrainId);
        return new TileState
        {
            TerrainId = terrainId,
            OwnerPlayerId = owner,
            Health = t.StartingHealth,
        };
    }

    // ---- the rules -------------------------------------------------------

    [Fact]
    public void DiggingRock_TakesTwoApplications_AndLeavesDirtPath()
    {
        // TWO applications, which is what the owner counted in the original
        // (2026-08-30) - being careful to note that a third animation plays
        // over the block already crumbling, so two do the damage.
        //
        // The shipped DigRockHealth of -5000 would fell it in one with 4835 to
        // spare. That value cannot produce the observed behaviour, so it is
        // corrected to -500 in ObservedCorrections - the project's only such
        // override, and the reasoning is there. See ADR-0023 and Q20.
        TileState tile = TileOf(Dk2TerrainFixture.Rock);

        TileWorkResult first = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Dig, 1);

        Assert.Equal(TileWorkOutcome.Progressed, first.Outcome);
        Assert.Equal(Dk2TerrainFixture.Rock, tile.TerrainId);
        Assert.Equal(-100, first.HealthDelta);   // -500 a second, a fifth each
        Assert.Equal(65, tile.Health);

        TileWorkResult second = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Dig, 1);

        Assert.Equal(TileWorkOutcome.Destroyed, second.Outcome);
        Assert.Equal(Dk2TerrainFixture.DirtPath, tile.TerrainId);
        Assert.Equal(Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth, tile.Health);
        Assert.Equal(-65, second.HealthDelta);   // clamped to the health present
    }

    /// <summary>
    /// The override is narrow on purpose: it fires only on the exact shipped
    /// value it was measured against, so a mod or a patch carrying a workable
    /// number is left alone. Guarding this is the difference between a recorded
    /// correction and a silent one.
    /// </summary>
    [Fact]
    public void TheDigCorrection_LeavesAnyOtherValueAlone()
    {
        Assert.Equal(
            ObservedCorrections.CorrectedDigRockHealth,
            ObservedCorrections.TileWorkPerSecond(Db, VariableTypeId.DigRockHealth));
        Assert.True(ObservedCorrections.IsCorrected(Db, VariableTypeId.DigRockHealth));

        // The raw value is still there and still says what the file says.
        Assert.Equal(
            ObservedCorrections.ShippedDigRockHealth, Db[VariableTypeId.DigRockHealth]);

        // Nothing else in the table is touched.
        foreach (TileWorkKind kind in Enum.GetValues<TileWorkKind>())
        {
            if (kind == TileWorkKind.Dig) continue;
            Assert.False(
                ObservedCorrections.IsCorrected(Db, TileWorkRules.Variable(kind)),
                $"{kind} is being corrected and should not be");
        }
    }

    [Fact]
    public void ClaimingDirtPath_YieldsClaimedPathOwnedByTheClaimer()
    {
        // Dirt Path: 1/660, ClaimTileHealth 825 per second over five applications
        // a second (Q47) => 165 a swing, and four swings to cross the top. That
        // count is the owner's "2-3-4 jumps MAX" for claiming one tile (Q46).
        TileState tile = TileOf(Dk2TerrainFixture.DirtPath, owner: 3);

        for (int i = 0; i < 3; i++)
        {
            TileWorkResult step = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Claim, 2);
            Assert.Equal(TileWorkOutcome.Progressed, step.Outcome);
            Assert.Equal(165, step.HealthDelta);
            Assert.Equal(Dk2TerrainFixture.DirtPath, tile.TerrainId);
        }

        TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Claim, 2);

        Assert.Equal(TileWorkOutcome.Transformed, result.Outcome);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, tile.TerrainId);
        Assert.Equal(2, tile.OwnerPlayerId);
        Assert.Equal(164, result.HealthDelta); // clamped at MaxHealth, not 165
    }

    [Fact]
    public void ReinforcingRock_TakesFiftyApplications_AndYieldsAWall()
    {
        // 165 -> 660 at ReinforceWallHealth 50 per SECOND over five applications
        // a second (Q47): 10 a swing, 495 / 10 = 49.5, so the fiftieth completes
        // it. Ten seconds of an imp's time either way - the granularity changed,
        // the rate did not.
        TileState tile = TileOf(Dk2TerrainFixture.Rock);

        for (int i = 1; i <= 49; i++)
        {
            TileWorkResult step = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Reinforce, 1);
            Assert.Equal(TileWorkOutcome.Progressed, step.Outcome);
            Assert.Equal(Dk2TerrainFixture.Rock, tile.TerrainId);
        }

        TileWorkResult last = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Reinforce, 1);

        Assert.Equal(TileWorkOutcome.Transformed, last.Outcome);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, tile.TerrainId);
        Assert.Equal(1, tile.OwnerPlayerId);
    }

    [Fact]
    public void ClaimedPath_DoesNotSpontaneouslyBecomeRock()
    {
        // The whole reason the upward transition is a property of the work and
        // not of the health level: Claimed Path ships full (660/660) with a
        // max-health type of Rock. A level-triggered rule would turn every
        // claimed floor tile in every level to rock. See Q18.
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        sim.Run(200);

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, sim.World.Tile(2, 2).TerrainId);
    }

    [Fact]
    public void RepairingAFullTile_ChangesNothing()
    {
        TileState tile = TileOf(Dk2TerrainFixture.ClaimedPath, owner: 1);

        TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.RepairTile, 1);

        Assert.Equal(TileWorkOutcome.NoChange, result.Outcome);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, tile.TerrainId);
        Assert.Equal(660, tile.Health);
    }

    [Fact]
    public void AttackingEnemyFloor_DestroysItAfterTwentyFourApplications()
    {
        // Claimed Path 660 at AttackTileHealth -140 a second, 28 a swing (Q47):
        // 660 / 28 = 23.6 -> twenty-four.
        TileState tile = TileOf(Dk2TerrainFixture.ClaimedPath, owner: 1);

        for (int i = 1; i <= 23; i++)
            Assert.Equal(
                TileWorkOutcome.Progressed,
                TileWorkRules.Apply(Db, ref tile, TileWorkKind.AttackTile, 2).Outcome);

        TileWorkResult last = TileWorkRules.Apply(Db, ref tile, TileWorkKind.AttackTile, 2);

        Assert.Equal(TileWorkOutcome.Destroyed, last.Outcome);
        Assert.Equal(Dk2TerrainFixture.DirtPath, tile.TerrainId);

        // Destroying a tile never hands it to the attacker: it has to be claimed.
        Assert.NotEqual(2, tile.OwnerPlayerId);
    }

    [Fact]
    public void DiggingAnEnemyWallIsVastlySlowerThanDiggingYourOwn()
    {
        // -130 vs -8 per second against 900 health, at five applications a
        // second: 26 vs 1.6 a swing, so 35 swings against 563.
        //
        // DigEnemyWallHealth is one of only two deltas that does not divide by
        // five, so this is also the test that would catch a lost remainder: 563
        // swings is 8 health a second exactly, where truncating to -1 a swing
        // would take 900 and run the job 37% slow forever.
        Assert.Equal(35, ApplicationsToDestroy(Dk2TerrainFixture.ReinforcedWall,
            TileWorkKind.DigOwnWall, owner: 1, worker: 1));
        Assert.Equal(563, ApplicationsToDestroy(Dk2TerrainFixture.ReinforcedWall,
            TileWorkKind.DigEnemyWall, owner: 1, worker: 2));
    }

    [Fact]
    public void DestroyedReinforcedWall_RevertsToRock_NotToFloor()
    {
        TileState tile = TileOf(Dk2TerrainFixture.ReinforcedWall, owner: 1);
        while (tile.TerrainId == Dk2TerrainFixture.ReinforcedWall)
            TileWorkRules.Apply(Db, ref tile, TileWorkKind.DigOwnWall, 1);

        Assert.Equal(Dk2TerrainFixture.Rock, tile.TerrainId);
    }

    [Fact]
    public void FillingInClaimedPath_TurnsItBackIntoRock()
    {
        // The reading that makes Claimed Path's max-health type of Rock coherent.
        // HYPOTHESIS on the timing (FillInHealth is 5 against a tile already at
        // 660/660, so one application does it) - see Q22.
        TileState tile = TileOf(Dk2TerrainFixture.ClaimedPath, owner: 1);

        TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.FillIn, 1);

        Assert.Equal(TileWorkOutcome.Transformed, result.Outcome);
        Assert.Equal(Dk2TerrainFixture.Rock, tile.TerrainId);
    }

    // ---- gold ------------------------------------------------------------

    [Fact]
    public void MiningAGoldSeam_ReleasesGoldInProportionToTheRockRemoved()
    {
        // 3000 gold over 660 health. MineGoldHealth is -300 per second and work
        // lands five times a second (Q47), so a swing takes 60 health — and the
        // owner measured 273 gold a swing in the original, which is where the 60
        // came from: 273 * 660 / 3000 = 60.06.
        TileState tile = TileOf(Dk2TerrainFixture.Gold);
        var swings = new List<int>();

        for (int i = 0; i < 11; i++)
        {
            TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.MineGold, 1);
            swings.Add(result.GoldReleased);

            Assert.Equal(
                i == 10 ? TileWorkOutcome.Destroyed : TileWorkOutcome.Progressed,
                result.Outcome);
            Assert.Equal(-60, result.HealthDelta);
        }

        // Eleven swings, of which eight are the 273 that was observed and three
        // are 272. The alternation is integer truncation showing through a
        // running total, not a rounding choice: gold is computed from where the
        // seam has got to, so the swings differ by a coin but the sum is exact.
        Assert.Equal(11, swings.Count);
        Assert.Equal(8, swings.Count(g => g == 273));
        Assert.Equal(3, swings.Count(g => g == 272));
        Assert.Equal(3000, swings.Sum());

        Assert.Equal(Dk2TerrainFixture.DirtPath, tile.TerrainId);
    }

    [Fact]
    public void AGemFace_IsInexhaustibleAndYieldsTheGemsRate()
    {
        // Gems are impenetrable and their destroyed type is themselves, so a
        // face collapses and instantly reappears at full health.
        //
        // 100 health against 60 a swing (Q47) means a face takes two swings to
        // collapse, and only the collapse pays. Fifty swings are therefore
        // twenty-five collapses — the *rate per second* is unchanged by the
        // finer granularity, which is the whole point of it.
        TileState tile = TileOf(Dk2TerrainFixture.Gems);
        int total = 0;

        for (int i = 0; i < 50; i++)
        {
            TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.MineGold, 1);
            total += result.GoldReleased;
            Assert.Equal(Dk2TerrainFixture.Gems, tile.TerrainId);
        }

        Assert.Equal(25 * 25, total);
        Assert.Equal(100, tile.Health);
    }

    // ---- what work is refused -------------------------------------------

    [Theory]
    [InlineData(Dk2TerrainFixture.ImpenetrableRock, TileWorkKind.Dig)]
    [InlineData(Dk2TerrainFixture.ImpenetrableRock, TileWorkKind.Claim)]
    [InlineData(Dk2TerrainFixture.ImpenetrableRock, TileWorkKind.Reinforce)]
    [InlineData(Dk2TerrainFixture.Gold, TileWorkKind.Dig)]
    [InlineData(Dk2TerrainFixture.Gems, TileWorkKind.Dig)]
    [InlineData(Dk2TerrainFixture.ClaimedPath, TileWorkKind.Dig)]
    [InlineData(Dk2TerrainFixture.ClaimedPath, TileWorkKind.Claim)]
    [InlineData(Dk2TerrainFixture.Rock, TileWorkKind.Claim)]
    [InlineData(Dk2TerrainFixture.Rock, TileWorkKind.MineGold)]
    public void WorkThatDoesNotApply_IsRejectedAndChangesNothing(byte terrainId, TileWorkKind kind)
    {
        TileState tile = TileOf(terrainId, owner: 1);
        TileState before = tile;

        TileWorkResult result = TileWorkRules.Apply(Db, ref tile, kind, playerId: 1);

        Assert.Equal(TileWorkOutcome.Rejected, result.Outcome);
        Assert.Equal(before.TerrainId, tile.TerrainId);
        Assert.Equal(before.Health, tile.Health);
    }

    [Fact]
    public void EveryWorkKind_ResolvesToAVariableTheDatabaseCarries()
    {
        foreach (TileWorkKind kind in Enum.GetValues<TileWorkKind>())
            Assert.True(Db.Variables.TryGet(TileWorkRules.Variable(kind), out _),
                $"{kind} has no balance variable.");
    }

    // ---- tagging ---------------------------------------------------------

    [Fact]
    public void Tagging_MarksTaggableTerrainOnly()
    {
        GameSimulation sim = Dk2TerrainFixture.Simulation();

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(1,
            new TilePos(5, 1),    // Rock: taggable
            new TilePos(2, 2),    // Claimed Path: not taggable
            new TilePos(0, 0)))); // Impenetrable Rock: not taggable

        Assert.True(sim.World.Tile(5, 1).IsTaggedBy(1));
        Assert.False(sim.World.Tile(2, 2).IsTaggedByAnyone);
        Assert.False(sim.World.Tile(0, 0).IsTaggedByAnyone);
    }

    [Fact]
    public void Tags_ArePerPlayer()
    {
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        var tile = new TilePos(5, 1);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(1, tile)));
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(2, tile)));
        sim.Tick(InputFrame.Of(TagTilesCommand.Untag(1, tile)));

        Assert.False(sim.World.Tile(tile).IsTaggedBy(1));
        Assert.True(sim.World.Tile(tile).IsTaggedBy(2));
    }

    [Fact]
    public void Tags_AreClearedWhenTheTileChangesType()
    {
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        var tile = new TilePos(5, 1);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(1, tile)));
        Assert.True(sim.World.Tile(tile).IsTaggedBy(1));

        // Two applications: a plain wall takes two swings (ADR-0023, Q20).
        for (int i = 0; i < 2; i++)
        {
            sim.World.QueueWork(tile.X, tile.Y, TileWorkKind.Dig, 1);
            sim.Tick();
        }

        Assert.Equal(Dk2TerrainFixture.DirtPath, sim.World.Tile(tile).TerrainId);
        Assert.False(sim.World.Tile(tile).IsTaggedByAnyone);
    }

    [Fact]
    public void AMarkSurvivesAGemCollapse_BecauseAGemFaceIsStillMarkable()
    {
        // Gems collapse into themselves. Clearing the mark on every conversion
        // would make a marked gem face a one-shot job, when the original mines
        // one indefinitely from a single mark. The mark is cleared only when the
        // tile becomes something that cannot carry one.
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        var tile = new TilePos(6, 5);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(1, tile)));
        sim.World.QueueWork(tile.X, tile.Y, TileWorkKind.MineGold, 1);
        sim.Tick();

        Assert.Equal(Dk2TerrainFixture.Gems, sim.World.Tile(tile).TerrainId);
        Assert.True(sim.World.Tile(tile).IsTaggedBy(1));
    }

    [Fact]
    public void EveryTerrainAdmitsAtMostOneKindOfMarkedWork()
    {
        // The four marked-work kinds are tried in a fixed order, and that order
        // is only free of policy while no tile admits two of them.
        foreach (TerrainDef terrain in Db.Terrain.Values)
        {
            foreach (byte owner in new byte[] { 0, 1 })
            {
                var tile = new TileState
                {
                    TerrainId = terrain.TerrainId,
                    OwnerPlayerId = owner,
                    Health = terrain.StartingHealth,
                };

                int applicable = TileWorkRules.TaggedWork
                    .Count(k => TileWorkRules.Applies(Db, tile, k, 1));

                Assert.True(applicable <= 1,
                    $"{terrain.Name} (owner {owner}) admits {applicable} kinds of marked work");
            }
        }
    }

    [Fact]
    public void TaggingARectangle_CoversEveryTileInIt()
    {
        GameSimulation sim = Dk2TerrainFixture.Simulation();

        sim.Tick(InputFrame.Of(TagTilesCommand.Rect(1, 5, 1, 7, 3)));

        for (int y = 1; y <= 3; y++)
            for (int x = 5; x <= 7; x++)
                Assert.True(sim.World.Tile(x, y).IsTaggedBy(1), $"({x},{y}) not tagged");
    }

    [Fact]
    public void TaggingPlayerIdsBeyondTheBitmask_AreRefusedLoudly()
    {
        var tile = new TileState();
        Assert.Throws<ArgumentOutOfRangeException>(() => tile.SetTaggedBy(16, true));
    }

    // ---- the work system -------------------------------------------------

    [Fact]
    public void TheWorkSystem_DrainsItsQueueEveryTick()
    {
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        // Two rounds, because a plain wall takes two swings (ADR-0023). What
        // this test is about is that the queue empties every tick, which the
        // first round already shows.
        for (int round = 0; round < 2; round++)
        {
            sim.World.QueueWork(5, 1, TileWorkKind.Dig, 1);
            sim.World.QueueWork(6, 1, TileWorkKind.Dig, 1);

            sim.Tick();

            Assert.Empty(sim.World.PendingWork);
        }

        Assert.Equal(Dk2TerrainFixture.DirtPath, sim.World.Tile(5, 1).TerrainId);
        Assert.Equal(Dk2TerrainFixture.DirtPath, sim.World.Tile(6, 1).TerrainId);
    }

    [Fact]
    public void QueuedWork_IsResolvedInQueueOrder()
    {
        // Two players mine the same seam. Order decides who gets which slice, so
        // it is FIFO and never sorted.
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.World.QueueWork(5, 5, TileWorkKind.MineGold, 1);
        sim.World.QueueWork(5, 5, TileWorkKind.MineGold, 2);
        sim.Tick();

        List<string> gold = events.Events
            .Where(e => e.Kind == "gold_uncollected")
            .Select(e => e.Detail)
            .ToList();

        Assert.Equal(2, gold.Count);
        Assert.Contains("player=1", gold[0]);
        Assert.Contains("player=2", gold[1]);
    }

    [Fact]
    public void WorkOnATileOutsideTheMap_IsRefusedWhenQueued()
    {
        GameSimulation sim = Dk2TerrainFixture.Simulation();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => sim.World.QueueWork(999, 0, TileWorkKind.Dig, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => sim.World.QueueWork(new TileWorkOrder(999_999, TileWorkKind.Dig, 1)));
    }

    [Fact]
    public void MiningWithNobodySwinging_CreditsNobody()
    {
        // Imps carry gold now (Q31), so the way to lose gold is for nothing to
        // have swung: work queued straight onto the world by a test or by the
        // harness has no creature behind it, and there is nobody for the gold to
        // go to. A treasury standing ready does not change that — gold does not
        // teleport into a room, it is carried there.
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        var events = new ListEventSink();
        sim.ObserveWith(events);

        // Player 1's room at the top-left, turned into somewhere gold could go.
        ref TileState treasury = ref sim.World.Tile(1, 1);
        RoomRules.Place(Dk2TerrainFixture.Database(), ref treasury,
            Dk2TerrainFixture.Database().Room(RoomTypeId.Treasury), 1);

        sim.World.QueueWork(5, 5, TileWorkKind.MineGold, 1);
        sim.Tick();

        Assert.Contains(events.Events,
            e => e.Kind == "gold_uncollected" && e.Detail.Contains("nobody swung"));
        Assert.Equal(0, GoldStore.Total(sim.World, 1));
    }

    // ---- determinism, with real systems registered ------------------------

    [Fact]
    public void TileMechanics_AreDeterministicUnderIdenticalInput()
    {
        static ulong RunOne()
        {
            GameSimulation sim = Dk2TerrainFixture.Simulation();
            for (int tick = 0; tick < 120; tick++)
            {
                int x = 4 + (tick % 9);
                int y = 1 + (tick % 9);
                sim.World.QueueWork(x, y, tick % 3 == 0 ? TileWorkKind.Dig : TileWorkKind.Claim, 1);
                sim.Tick(InputFrame.Of(TagTilesCommand.Tag((byte)(1 + (tick % 2)), new TilePos(x, y))));
            }
            return sim.StateHash();
        }

        Assert.Equal(RunOne(), RunOne());
    }

    [Fact]
    public void PendingWork_SurvivesASnapshot()
    {
        GameSimulation sim = Dk2TerrainFixture.Simulation();
        sim.World.QueueWork(5, 1, TileWorkKind.Dig, 1);

        // Restored with the SAME systems it was saved with. A snapshot carries
        // state, not behaviour, so a run restored under a different system list
        // is a different game — and this test used to get away with a shorter
        // list only because none of the systems it dropped did anything on the
        // one tick it takes afterwards.
        byte[] bytes = WorldSnapshot.ToBytes(sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            bytes, Dk2TerrainFixture.Database(), Dk2TerrainFixture.DefaultSystems());

        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Single(restored.World.PendingWork);

        sim.Tick();
        restored.Tick();
        Assert.Equal(sim.StateHash(), restored.StateHash());

        // The queued swing was the first of the two a plain wall takes
        // (ADR-0023), so the restored run has to be given the second before the
        // tile falls — and it has to fall in both runs identically, which is
        // the point of the test.
        sim.World.QueueWork(5, 1, TileWorkKind.Dig, 1);
        restored.World.QueueWork(5, 1, TileWorkKind.Dig, 1);
        sim.Tick();
        restored.Tick();

        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(Dk2TerrainFixture.DirtPath, restored.World.Tile(5, 1).TerrainId);
    }

    private static int ApplicationsToDestroy(byte terrainId, TileWorkKind kind, byte owner, byte worker)
    {
        TileState tile = TileOf(terrainId, owner);
        int applications = 0;
        while (tile.TerrainId == terrainId && applications < 10_000)
        {
            TileWorkRules.Apply(Db, ref tile, kind, worker);
            applications++;
        }
        return applications;
    }
}
