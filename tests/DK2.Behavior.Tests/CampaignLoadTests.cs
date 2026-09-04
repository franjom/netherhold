// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// The first point where original game data reaches the simulation layer.
///
/// Skips when no Dungeon Keeper 2 installation is configured (DK2_DIR), so CI
/// stays green without proprietary assets.
/// </summary>
public class CampaignLoadTests
{
    [Fact]
    public void FirstCampaignLevel_LoadsIntoWorldState()
    {
        string? install = Dk2Install.Find();
        if (install is null) return; // skipped: no DK2 install

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

    [Fact]
    public void EveryCampaignLevel_BuildsAWorld()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        GameDatabase database = GameDatabaseLoader.Load(install);
        string maps = Path.Combine(install, "Data", "editor", "maps");

        int built = 0;
        foreach (string file in Directory.GetFiles(maps, "*Map.kld"))
        {
            string level = Path.GetFileNameWithoutExtension(file);
            level = level[..^"Map".Length];

            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            // Throws if any tile references a terrain type the database lacks.
            WorldState world = WorldState.FromMap(database, map, [(byte)0]);
            Assert.Equal(map.Width * map.Height, world.TileCount);
            built++;
        }

        Assert.True(built > 50, $"expected many campaign levels, built {built}");
    }

    [Fact]
    public void CampaignScenario_RunsDeterministically()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

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
