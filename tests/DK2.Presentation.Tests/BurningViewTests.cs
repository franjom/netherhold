// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class BurningViewTests
{
    [InstallFact]
    public void BurningBaseUsesTheCreatureFloorRatherThanThePortalHole()
    {
        var database = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        var world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level8")!, [1, 3]);
        var view = new WorldView(database);
        TilePos portal = Enumerable.Range(0, world.Width * world.Height)
            .Select(i => new TilePos(i % world.Width, i / world.Width))
            .First(p => database.RoomsByTerrain.TryGetValue(world.Tile(p).TerrainId, out var room)
                && room.TypeId == RoomTypeId.Portal && view.GroundHeightAt(world,
                    new(new(CreatureState.CentreOf(p.X)), new(CreatureState.CentreOf(p.Y)), default)).Raw == 0);
        CreatureState creature = world.SpawnCreature(database, database.CreatureByName("Goblin")!.CreatureId, 1, portal);
        Assert.Equal(Fp32.One, view.CreatureEffectGround(world, creature).Raw);
        Assert.Equal(Fp32.One, view.Creatures(world).Single(c => c.Id == creature.Id).Height);
    }

    [Fact]
    public void BurningRedYieldsToAllThreeHigherPriorityStatuses()
    {
        var creature = new CreatureState { BurningTicks = 2 };
        Assert.True(CreatureBurningVisual.ShowsRed(creature, default));
        Assert.False(CreatureBurningVisual.ShowsRed(creature, CreatureFlags.IsHorny));
        creature.FrozenTicks = 1;
        Assert.False(CreatureBurningVisual.ShowsRed(creature, default));
        creature.FrozenTicks = 0; creature.PoisonTicks = 1;
        Assert.False(CreatureBurningVisual.ShowsRed(creature, default));
        creature.PoisonTicks = 0; creature.InvisibleTicks = 1;
        Assert.False(CreatureBurningVisual.ShowsRed(creature, default));
        creature.InvisibleTicks = 0; creature.BurningTicks = 0;
        Assert.False(CreatureBurningVisual.ShowsRed(creature, default));
    }

    [Fact]
    public void OnlyLocalInfernoRequestsConsumeTwoBridgeDrawsPerPresentationCall()
    {
        var view = new ScriptedViewDirector(PlayerIds.FirstKeeper, _ => null, _ => null);
        var expected = new Dk2Lcg(1);
        Send(new PlayerId(4), 9);
        Send(PlayerIds.FirstKeeper, 2);
        view.Update(0, 0);
        Assert.Equal(new FpPosition3(new(expected.NextInt(200) - 100), new(expected.NextInt(200) - 100), new(0)), view.CameraJitter);
        Assert.Equal(1, view.PendingCameraJitters);
        view.Update(1, 0); // Outside the 50ms particle gate, with fresh offsets, not a random walk.
        Assert.Equal(new FpPosition3(new(expected.NextInt(200) - 100), new(expected.NextInt(200) - 100), new(0)), view.CameraJitter);
        Assert.Equal(0, view.PendingCameraJitters);
        view.Update(2, 0);
        Assert.Equal(default, view.CameraJitter);
        void Send(PlayerId player, int count) => view.Emit(new SimEvent(default, "test", "shake", "")
        { ScriptedView = new(ScriptedViewKind.CameraJitter, player, count, default, false) });
    }
}
