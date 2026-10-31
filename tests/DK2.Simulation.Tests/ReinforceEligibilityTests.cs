// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class ReinforceEligibilityTests
{
    [Theory]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, 2, 449, true)]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, 2, 450, false)]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, 2, 900, false)]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, 3, 449, true)]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, 3, 450, false)]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, 4, 100, false)]
    [InlineData(Dk2TerrainFixture.Rock, 2, 165, true)]
    [InlineData(Dk2TerrainFixture.Rock, 2, 659, true)]
    [InlineData(Dk2TerrainFixture.Rock, 2, 660, false)]
    [InlineData(Dk2TerrainFixture.Rock, 2, 661, true)]
    [InlineData(Dk2TerrainFixture.Rock, 3, 100, false)]
    [InlineData(Dk2TerrainFixture.Rock, 4, 100, false)]
    [InlineData(Dk2TerrainFixture.Rock, 0, 100, false)]
    [InlineData(Dk2TerrainFixture.Gold, 2, 100, false)]
    [InlineData(Dk2TerrainFixture.ImpenetrableRock, 2, 0, false)]
    public void Kind6SelectionUsesExactOwnerHealthAndTerrainGates(byte terrain, byte owner, int health, bool expected)
    {
        var sim = Start(terrain, owner, health);
        Assert.Equal(expected, ReinforcementRules.CanStart(sim.World, sim.Database, new TilePos(2, 2), 3));
    }

    [Fact]
    public void NeutralOwnableWallNeedsTouchingOwnFloorInTheSelectionPredicate()
    {
        var sim = Start(Dk2TerrainFixture.ReinforcedWall, 2, 100);
        sim.World.Tile(2, 3).OwnerPlayerId = 2;
        Assert.False(ReinforcementRules.CanStart(sim.World, sim.Database, new TilePos(2, 2), 3));
    }

    [Fact]
    public void NeutralFormerKeeperWallHealsPastHalfWithoutBecomingOwned()
    {
        // Inject the post-defeat neutral wall state; this is not a native capture.
        var sim = Start(Dk2TerrainFixture.ReinforcedWall, 2, 449);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(350);
        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("Reinforce"));
        Assert.Equal(900, sim.World.Tile(2, 2).Health);
        Assert.Equal(2, sim.World.Tile(2, 2).OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, sim.World.Tile(2, 2).TerrainId);
    }

    [Fact]
    public void OwnWallBelowHalfTakesKind6ButTheNeutralOnlyActionDoesNoWork()
    {
        var sim = Start(Dk2TerrainFixture.ReinforcedWall, 3, 100);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(60);
        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("Reinforce"));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("RepairWall"));
        Assert.Equal(100, sim.World.Tile(2, 2).Health);
        Assert.Equal(0, sim.World.Creatures[0].ExperiencePoints);
    }

    [Fact]
    public void ASecondImpFallsThroughToRepairWhenTheOnlyKind6SideIsHeld()
    {
        // 0x0046AEA0 keeps kind 6 and kind 16 in separate pool entries with their own side slots,
        // and runs the kind-16 pass once no kind-6 side starts a walk.
        var sim = Start(Dk2TerrainFixture.ReinforcedWall, 3, 100);
        sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(2, 3));
        bool together = false;
        for (int i = 0; i < 600; i++)
        {
            sim.Tick();
            // One holds the kind-6 side while the other repairs from the same face.
            together |= sim.World.Creatures.Any(c => c.TargetTileIndex == 12 && c.TargetWork == TileWorkKind.Reinforce)
                && sim.World.Creatures.Any(c => c.TargetTileIndex == 12 && c.TargetWork == TileWorkKind.RepairWall);
        }
        Assert.True(together);
        Assert.Equal(900, sim.World.Tile(2, 2).Health);
        Assert.Equal(3, sim.World.Tile(2, 2).OwnerPlayerId);
    }

    [Fact]
    public void DamagedNeutralRockIsReinforcedAndTaken()
    {
        var sim = Start(Dk2TerrainFixture.Rock, 2, 600);
        sim.Run(100);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, sim.World.Tile(2, 2).TerrainId);
        Assert.Equal(3, sim.World.Tile(2, 2).OwnerPlayerId);
    }

    [Fact]
    public void HeldKind6TaskSurvivesCrossingHalfHealthBeforeEntry21()
    {
        var sim = Start(Dk2TerrainFixture.ReinforcedWall, 2, 449);
        CreatureState imp = sim.World.Creatures[0];
        bool selected = false;
        for (int i = 0; i < 30; i++)
        {
            sim.Tick();
            if (imp.TargetWork != TileWorkKind.Reinforce || imp.TargetTileIndex != 12 || imp.Dk2StateId == 22) continue;
            selected = true;
            break;
        }
        Assert.True(selected);
        sim.World.Tile(2, 2).Health = 451;
        sim.Run(30);
        Assert.True(sim.World.Tile(2, 2).Health > 451);
    }

    [Theory]
    [InlineData(2, 450)]
    [InlineData(4, 100)]
    public void NeutralWallAtHalfAndRivalWallGetNoReinforceJob(byte owner, int health)
    {
        var sim = Start(Dk2TerrainFixture.ReinforcedWall, owner, health);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(60);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken");
        Assert.Equal(health, sim.World.Tile(2, 2).Health);
        Assert.Equal(owner, sim.World.Tile(2, 2).OwnerPlayerId);
    }

    private static GameSimulation Start(byte terrain, byte owner, int health)
    {
        var db = Dk2TerrainFixture.Database();
        var tiles = Enumerable.Repeat(new MapTile(Dk2TerrainFixture.ImpenetrableRock, 2, 0, 0), 25).ToArray();
        tiles[17] = new MapTile(Dk2TerrainFixture.ClaimedPath, 3, 0, 0);
        var world = WorldState.FromMap(db, new LevelMap(5, 5, tiles), [(byte)2, (byte)3, (byte)4]);
        ref TileState target = ref world.Tile(2, 2);
        target.TerrainId = terrain;
        target.OwnerPlayerId = owner;
        target.Health = health;
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(2, 3));
        return new GameSimulation(db, world, 42, Dk2TerrainFixture.DefaultSystems());
    }
}
