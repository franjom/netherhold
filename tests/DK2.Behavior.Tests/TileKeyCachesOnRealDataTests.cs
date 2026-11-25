// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The room sweep and the structure mask answer from <c>TileState.KeyWriteCount</c> without reading the
/// tiles. Over real campaign levels, after every tick, each must give what a computation from the tiles alone gives
/// (<c>TileKeyCacheTests</c> has the unit cases); a tile write that skipped the count would show here.</summary>
public sealed class TileKeyCachesOnRealDataTests
{
    [InstallTheory]
    [InlineData("Level1", 400)]
    [InlineData("Level6a", 400)]
    [InlineData("Level7", 400)]
    public void AfterEveryTick_TheCachedRoomsAndStructureBlocksAreTheOnesTheTilesGive(string name, int ticks)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, name);
        GameSimulation simulation = CampaignSimulationFactory.Create(
            database,
            GameDatabaseLoader.LoadCampaignMap(install, name)!,
            GameDatabaseLoader.LoadCampaignLevel(install, name)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, name)!,
            GameDatabaseLoader.LoadCampaignThings(install, name)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, name)!, seed: 1);

        for (int i = 0; i < ticks; i++)
        {
            simulation.Tick();
            WorldState world = simulation.World;

            IReadOnlyList<RoomInstance> cached = RoomIndex.Build(world, database);
            List<RoomInstance> fresh = RoomIndex.Build(world, database, _ => true);
            Assert.Equal(fresh.Count, cached.Count);
            for (int r = 0; r < fresh.Count; r++)
            {
                Assert.Same(fresh[r].Definition, cached[r].Definition);
                Assert.Equal(fresh[r].OwnerPlayerId, cached[r].OwnerPlayerId);
                Assert.Equal(fresh[r].TileIndices, cached[r].TileIndices);
            }

            StructureBlocks live = StructureBlocks.For(world, database);
            StructureBlocks scratch = StructureBlocks.For(WorldState.CopyOf(world), database);
            Assert.Equal(scratch.Count, live.Count);
            Assert.Equal(scratch.HeartCentres, live.HeartCentres);
            Assert.Equal(scratch.PortalCentres.OrderBy(c => c), live.PortalCentres.OrderBy(c => c));
            for (int t = 0; t < world.TileCount; t++) Assert.Equal(scratch.Blocks(t), live.Blocks(t));
        }
    }
}
