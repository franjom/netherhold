// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class MiningStrikeVisibilityTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(660, true)]
    public void TheStrikeResultPrecedesTheFullHandsDecision(int seamHealth, bool delivers)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        var seam = new TilePos(4, 2);
        ref TileState tile = ref world.Tile(seam);
        tile.TerrainId = Dk2TerrainFixture.Gold;
        tile.OwnerPlayerId = PlayerIds.Neutral;
        tile.Health = seamHealth;
        tile.SetTaggedBy(PlayerIds.FirstKeeper, true);
        ref TileState store = ref world.Tile(1, 1);
        store.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        store.OwnerPlayerId = PlayerIds.FirstKeeper;
        store.Health = db.TerrainById(store.TerrainId).StartingHealth;
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(3, 2));
        imp.Dk2StateId = ImpStates.For(TileWorkKind.MineGold).Work;
        imp.TargetWork = TileWorkKind.MineGold;
        imp.TargetTileIndex = seam.Y * world.Width + seam.X;
        imp.StateEnteredTick = -8;
        imp.TimerTicks = 1;
        imp.GoldHeld = 1059;
        var sim = new GameSimulation(db, world, 99, [new ImpTaskSystem(), new TileWorkSystem()]);

        sim.Tick();

        Assert.True(imp.GoldHeld >= 1060);
        Assert.Equal(delivers, imp.Activity == CreatureActivity.DeliveringGold);
        if (!delivers)
        {
            Assert.Equal(Dk2TerrainFixture.DirtPath, tile.TerrainId);
            Assert.Equal(ImpStates.For(TileWorkKind.MineGold).Work, imp.Dk2StateId);
        }
    }
}
