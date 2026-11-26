// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The terrain key set covers the pieces exposed by individual tile views.</summary>
public sealed class TerrainArtKeysTests
{
    private static readonly PlayerId Viewer = new(1);

    [InstallFact]
    public void TheTerrainKeysCoverEveryPieceSetAnyTileHandsOut()
    {
        (GameDatabase database, GameSimulation simulation) = Level8();
        var view = new WorldView(database, Viewer);
        var expected = new HashSet<AssetRef>();
        for (int y = 0; y < simulation.World.Height; y++)
        {
            for (int x = 0; x < simulation.World.Width; x++)
            {
                TileView tile = view.TileAt(simulation.World, x, y);
                if (tile.PieceKeys is { } pieces)
                {
                    expected.UnionWith(pieces.All());
                }

                if (tile.BedKeys is { } bed)
                {
                    expected.UnionWith(bed.All());
                }

                if (tile.RoomKeys is { } room)
                {
                    expected.UnionWith(room.All());
                }

                expected.Add(tile.SightFogOverlay);
            }
        }

        expected.Remove(default);

        AssertCovered(new HashSet<AssetRef>(view.TerrainArtKeys(simulation.World)), expected);
    }

    private static void AssertCovered(IReadOnlyCollection<AssetRef> keys, IEnumerable<AssetRef> wanted)
    {
        string[] missing = [.. wanted.Where(key => !keys.Contains(key)).Select(key => key.ToString())];
        Assert.True(missing.Length == 0, "not listed: " + string.Join(", ", missing));
    }

    private static (GameDatabase, GameSimulation) Level8()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        GameSimulation simulation = CampaignSimulationFactory.Create(database,
            GameDatabaseLoader.LoadCampaignMap(install, "Level8")!, GameDatabaseLoader.LoadCampaignLevel(install, "Level8")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level8")!, GameDatabaseLoader.LoadCampaignThings(install, "Level8")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level8")!, 1, fogOfWarEnabled: false);
        return (database, simulation);
    }

}
