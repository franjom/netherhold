// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// The first point where original game data reaches the simulation layer.
///
/// Skips when no Dungeon Keeper 2 installation is configured (DK2_DIR), so CI
/// stays green without proprietary assets.
/// </summary>
public class CampaignLoadTests
{
    [InstallFact]
    public void FirstCampaignLevel_LoadsIntoWorldState()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase database = GameDatabaseLoader.Load(install);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;

        WorldState world = WorldState.FromMap(
            database, map, map.Tiles.Select(t => t.PlayerId).Distinct());

        Assert.Equal(map.Width, world.Width);
        Assert.Equal(map.Height, world.Height);

        // Every tile carries its terrain's starting health - no invented numbers.
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                TerrainDef terrain = database.TerrainById(map[x, y].TerrainId);
                Assert.Equal(terrain.StartingHealth, world.Tile(x, y).Health);
                Assert.Equal(map[x, y].PlayerId, world.Tile(x, y).OwnerPlayerId);
            }
    }

    [InstallFact]
    public void EveryCampaignLevel_BuildsAWorld()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase database = GameDatabaseLoader.Load(install);
        string maps = Path.Combine(install, "Data", "editor", "maps");

        int built = 0;
        foreach (string file in Directory.GetFiles(maps, "*Map.kld"))
        {
            string level = Path.GetFileNameWithoutExtension(file);
            level = level[..^"Map".Length];

            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);

            Assert.NotNull(map);
            if (map is null) continue;

            // Throws if any tile references a terrain type the database lacks.
            WorldState world = WorldState.FromMap(database, map, [(byte)0]);
            Assert.Equal(map.Width * map.Height, world.TileCount);
            built++;
        }

        Assert.True(built > 50, $"expected many campaign levels, built {built}");
    }

    [InstallFact]
    public void CampaignScenario_RunsDeterministically()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase database = GameDatabaseLoader.Load(install);
        var runner = new ScenarioRunner(database, install);
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "smoke", "campaign_level1_load.yaml"));

        Assert.Equal(runner.Run(scenario).HashPerTick, runner.Run(scenario).HashPerTick);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root (no DK2.slnx above the test binaries).");
    }
}
