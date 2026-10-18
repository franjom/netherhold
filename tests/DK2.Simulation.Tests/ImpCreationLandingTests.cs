// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

public class ImpCreationLandingTests
{
    private static GameSimulation Staffed(int gravity = 240)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        db.Variables.Misc[VariableTypeId.GravityConstant] = new(VariableTypeId.GravityConstant, gravity, 0, 0);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Neutral, PlayerIds.Good, PlayerIds.FirstKeeper]);
        ref TileState heart = ref world.Tile(2, 2);
        heart.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        heart.OwnerPlayerId = PlayerIds.FirstKeeper;
        heart.Health = db.TerrainById(heart.TerrainId).StartingHealth;
        world.Player(PlayerIds.FirstKeeper).Mana = 100_000;
        var sim = new GameSimulation(db, world, 5, Dk2TerrainFixture.DefaultSystems());
        sim.Run(ImpSupplyRules.FreeImpIntervalTicks(db) * 5);
        return sim;
    }

    private static CreatureState Cast(GameSimulation sim)
    {
        sim.Tick(InputFrame.Of(new CreateImpCommand(PlayerIds.FirstKeeper,
            new FpPosition2(new Fp32(2 * Fp32.One + 681), new Fp32(3 * Fp32.One + 1337)))));
        return sim.World.Creatures[^1];
    }

    [Fact]
    public void ACreatedImpFallsThenInitializesBeforeChoosingItsFirstTask()
    {
        GameSimulation sim = Staffed();
        CreatureState imp = Cast(sim);
        var point = (imp.PosX, imp.PosY);
        Assert.Equal((214, 2048), (imp.Dk2StateId, imp.PosZ));
        Assert.False(imp.JustDropped);

        sim.Tick();
        Assert.Equal((214, 0), (imp.Dk2StateId, imp.PosZ));
        Assert.Equal(2048, imp.LastPosZ);
        sim.Tick();
        Assert.Equal((11, 0), (imp.Dk2StateId, imp.PosZ));
        Assert.False(imp.JustDropped);
        sim.Tick();
        Assert.Equal((10, 0), (imp.Dk2StateId, imp.PosZ));
        Assert.True(imp.JustDropped);
        Assert.Equal(point, (imp.PosX, imp.PosY));
        Assert.False(imp.HasRoute);
        Assert.Equal(-1, imp.TargetTileIndex);
        sim.Tick();
        Assert.NotEqual(10, imp.Dk2StateId);
    }

    [Fact]
    public void ZeroGravityDoesNotTurnTheFallIntoAFixedWait()
    {
        GameSimulation sim = Staffed(gravity: 0);
        CreatureState imp = Cast(sim);
        sim.Run(12);
        Assert.Equal((214, 2048), (imp.Dk2StateId, imp.PosZ));
        Assert.False(imp.HasRoute);
        Assert.False(imp.JustDropped);
    }

    [Fact]
    public void LowerGravityAccumulatesVelocityAndClampsAtTheFloor()
    {
        GameSimulation sim = Staffed(gravity: 16);
        CreatureState imp = Cast(sim);
        foreach (int height in new[] { 1792, 1280, 512, 0 })
        {
            sim.Tick();
            Assert.Equal((214, height), (imp.Dk2StateId, imp.PosZ));
        }
        sim.Tick();
        Assert.Equal((11, 0), (imp.Dk2StateId, imp.PosZ));
    }

    [Fact]
    public void ASavedFallResumesWithTheSameVelocityAndFirstChoice()
    {
        GameSimulation sim = Staffed(gravity: 16);
        CreatureState imp = Cast(sim);
        sim.Tick();
        Assert.Equal(1792, imp.PosZ);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim),
            sim.Database, Dk2TerrainFixture.DefaultSystems());
        for (int i = 0; i < 12; i++)
        {
            sim.Tick();
            restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
    }

    [Fact]
    public void ThePersistentFallVelocityContributesToTheStateHash()
    {
        GameSimulation sim = Staffed(gravity: 16);
        CreatureState imp = Cast(sim);
        sim.Tick();
        ulong before = sim.StateHash();
        imp.CreationVelocityZ += 256;
        Assert.NotEqual(before, sim.StateHash());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TileAndDefaultHeartCastsRetainTheirExistingPath(bool tileCast)
    {
        GameSimulation sim = Staffed();
        sim.Tick(InputFrame.Of(new CreateImpCommand(PlayerIds.FirstKeeper,
            tileCast ? new TilePos(2, 3) : null)));
        CreatureState imp = sim.World.Creatures[^1];
        Assert.Equal((1, 0), (imp.Dk2StateId, imp.PosZ));
        Assert.True(imp.JustDropped);
    }

    [Theory]
    [InlineData(240, 0)]
    [InlineData(16, 1)]
    public void PickingUpAnAirborneNewbornEndsItsCreationOnlyElevation(int gravity, int fallingTicks)
    {
        GameSimulation sim = Staffed(gravity);
        CreatureState imp = Cast(sim);
        sim.Run(fallingTicks);
        Assert.True(imp.PosZ > 0);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(PlayerIds.FirstKeeper, imp.Id)));
        Assert.True(imp.InHand);
        Assert.NotEqual(214, imp.Dk2StateId);
        Assert.Equal((0, 0, 0), (imp.PosZ, imp.LastPosZ, imp.CreationVelocityZ));
    }
}
