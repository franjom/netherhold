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
    public void DiggingRock_DestroysItInOneApplication_AndLeavesDirtPath()
    {
        // Rock ships at 165 health; DigRockHealth is -5000. One application is
        // enough with 4835 to spare. This is faithful to the data and it is also
        // the anomaly recorded as Q20: it makes digging earth instantaneous,
        // which is not how the original plays.
        TileState tile = TileOf(Dk2TerrainFixture.Rock);

        TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Dig, 1);

        Assert.Equal(TileWorkOutcome.Destroyed, result.Outcome);
        Assert.Equal(Dk2TerrainFixture.DirtPath, tile.TerrainId);
        Assert.Equal(Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth, tile.Health);
        Assert.Equal(-165, result.HealthDelta); // clamped to the health present
    }

    [Fact]
    public void ClaimingDirtPath_YieldsClaimedPathOwnedByTheClaimer()
    {
        // Dirt Path: 1/660, ClaimTileHealth 825 => one application crosses the top.
        TileState tile = TileOf(Dk2TerrainFixture.DirtPath, owner: 3);

        TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.Claim, 2);

        Assert.Equal(TileWorkOutcome.Transformed, result.Outcome);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, tile.TerrainId);
        Assert.Equal(2, tile.OwnerPlayerId);
        Assert.Equal(659, result.HealthDelta); // clamped at MaxHealth, not 825
    }

    [Fact]
    public void ReinforcingRock_TakesTenApplications_AndYieldsAWall()
    {
        // 165 -> 660 at ReinforceWallHealth 50 per application: 495/50 = 9.9,
        // so the tenth application completes it.
        TileState tile = TileOf(Dk2TerrainFixture.Rock);

        for (int i = 1; i <= 9; i++)
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
    public void AttackingEnemyFloor_DestroysItAfterFiveApplications()
    {
        // Claimed Path 660 at AttackTileHealth -140: 660/140 = 4.71 -> five.
        TileState tile = TileOf(Dk2TerrainFixture.ClaimedPath, owner: 1);

        for (int i = 1; i <= 4; i++)
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
        // -130 vs -8 per application against 900 health: 7 applications vs 113.
        Assert.Equal(7, ApplicationsToDestroy(Dk2TerrainFixture.ReinforcedWall,
            TileWorkKind.DigOwnWall, owner: 1, worker: 1));
        Assert.Equal(113, ApplicationsToDestroy(Dk2TerrainFixture.ReinforcedWall,
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
        // 3000 gold over 660 health, -300 per application.
        TileState tile = TileOf(Dk2TerrainFixture.Gold);

        TileWorkResult first = TileWorkRules.Apply(Db, ref tile, TileWorkKind.MineGold, 1);
        Assert.Equal(3000 * 300 / 660, first.GoldReleased);
        Assert.Equal(360, tile.Health);

        TileWorkResult second = TileWorkRules.Apply(Db, ref tile, TileWorkKind.MineGold, 1);
        Assert.Equal(3000 * 300 / 660, second.GoldReleased);
        Assert.Equal(60, tile.Health);

        TileWorkResult third = TileWorkRules.Apply(Db, ref tile, TileWorkKind.MineGold, 1);
        Assert.Equal(TileWorkOutcome.Destroyed, third.Outcome);
        Assert.Equal(Dk2TerrainFixture.DirtPath, tile.TerrainId);

        // Integer truncation loses a few gold against the stated 3000. Kept, not
        // rounded away - rounding it away would be an invention of its own.
        int total = first.GoldReleased + second.GoldReleased + third.GoldReleased;
        Assert.InRange(total, 2990, 3000);
    }

    [Fact]
    public void AGemFace_IsInexhaustibleAndYieldsTheGemsRate()
    {
        // Gems are impenetrable and their destroyed type is themselves, so a
        // face collapses and instantly reappears at full health.
        TileState tile = TileOf(Dk2TerrainFixture.Gems);
        int total = 0;

        for (int i = 0; i < 50; i++)
        {
            TileWorkResult result = TileWorkRules.Apply(Db, ref tile, TileWorkKind.MineGold, 1);
            total += result.GoldReleased;
            Assert.Equal(Dk2TerrainFixture.Gems, tile.TerrainId);
        }

        Assert.Equal(50 * 25, total);
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

        sim.World.QueueWork(tile.X, tile.Y, TileWorkKind.Dig, 1);
        sim.Tick();

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
        sim.World.QueueWork(5, 1, TileWorkKind.Dig, 1);
        sim.World.QueueWork(6, 1, TileWorkKind.Dig, 1);

        sim.Tick();

        Assert.Empty(sim.World.PendingWork);
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

        byte[] bytes = WorldSnapshot.ToBytes(sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            bytes, Dk2TerrainFixture.Database(),
            [new TileTagSystem(), new TileWorkSystem()]);

        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Single(restored.World.PendingWork);

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
