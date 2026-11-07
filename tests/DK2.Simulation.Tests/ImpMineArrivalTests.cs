// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ImpMineArrivalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExhaustedSeamIsRejectedByTheResumedEntryOnTheFollowingTurn(bool pathFinished)
    {
        (GameSimulation sim, CreatureState imp) = ApproachExhaustedSeam(whole: true);
        imp.Body.PathFinished = pathFinished;
        Assert.Equal(Fixed16.One, FollowPathRules.Progress(imp.Body));
        var position = (imp.PosX, imp.PosY);

        sim.Tick();

        Assert.Equal((25, CreatureStateMachine.Idle), (imp.Dk2StateId, imp.UtilityNewState));
        Assert.Equal(position, (imp.PosX, imp.PosY));
        Assert.Equal(Fixed16Vector.Zero, imp.Body.Vel);
        Assert.Equal(TileWorkKind.MineGold, imp.TargetWork);
        Assert.True(imp.TargetTileIndex >= 0);

        sim.Tick();

        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
        Assert.Equal(-1, imp.TargetTileIndex);
        Assert.Equal(position, (imp.PosX, imp.PosY));
    }

    [Fact]
    public void AnIncompleteWalkStillKeepsItsVanishedSeam()
    {
        (GameSimulation sim, CreatureState imp) = ApproachExhaustedSeam(whole: false);
        Assert.NotEqual(Fixed16.One, FollowPathRules.Progress(imp.Body));

        sim.Tick();

        Assert.Equal((6, 25), (imp.Dk2StateId, imp.UtilityNewState));
        Assert.True(imp.TargetTileIndex >= 0);
    }

    private static (GameSimulation, CreatureState) ApproachExhaustedSeam(bool whole)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        var target = new TilePos(4, 2);
        ref TileState tile = ref world.Tile(target);
        tile.TerrainId = Dk2TerrainFixture.DirtPath;
        tile.OwnerPlayerId = PlayerIds.Neutral;
        tile.Health = db.TerrainById(tile.TerrainId).StartingHealth;
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(2, 2));
        imp.TargetWork = TileWorkKind.MineGold;
        imp.TargetTileIndex = target.Y * world.Width + target.X;
        CreatureStateMachine.BeginTravel(new StateHookContext(world, db), imp, 6, 25);
        var start = new Fixed16Vector(imp.PosX << 4, imp.PosY << 4);
        var end = new Fixed16Vector(start.X + Fixed16.One, start.Y);
        imp.Body.Pos = start;
        RouteWindow.Load(imp.Body, [end], end, 0);
        if (whole)
        {
            imp.Body.Pos = new Fixed16Vector(end.X - 0x800, end.Y);
            imp.PosX = imp.Body.Pos.X >> 4;
            imp.PosY = imp.Body.Pos.Y >> 4;
        }
        imp.Body.Vel = new Fixed16Vector(1234, 5678);
        return (new GameSimulation(db, world, 99, [new ImpTaskSystem()]), imp);
    }
}
