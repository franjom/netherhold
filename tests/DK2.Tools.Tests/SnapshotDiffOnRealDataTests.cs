// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Tools.Tests;

[Collection("Console")]
public class SnapshotDiffOnRealDataTests
{
    [InstallFact]
    public void CampaignMapSnapshotsExerciseTheProductionCommandAndLevelLoader()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
        LevelMap map = Assert.IsType<LevelMap>(GameDatabaseLoader.LoadCampaignMap(install, "Level1"));
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "Level1");
        Assert.NotNull(players);
        WorldState world = WorldState.FromMap(database, map, players.Select(p => p.PlayerId));
        GameSimulation simulation = GameSimulation.Resume(database, world, new SimRandom(13));
        byte[] original = WorldSnapshot.ToBytes(simulation);
        int health = world.Tile(new TilePos(0, 0)).Health;
        world.Tile(new TilePos(0, 0)).Health = health ^ 1;
        byte[] changed = WorldSnapshot.ToBytes(simulation);
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "build", "snapshot-diff-campaign", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string before = Path.Combine(directory, "before.dk2g"), after = Path.Combine(directory, "after.dk2g");
        string output = Path.Combine(directory, "report.json");
        var header = new SavedSessionHeader("Level1", 1, 0, DateTime.UnixEpoch, "diagnostic fixture");
        SavedSessionFile.Save(before, new(header, original, new CampaignProfile()));
        SavedSessionFile.Save(after, new(header, changed, new CampaignProfile()));
        byte[] beforeFile = File.ReadAllBytes(before), afterFile = File.ReadAllBytes(after);

        Assert.Equal(1, Program.Dispatch(["snapshot-diff", before, after, output]));
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement change = Assert.Single(report.RootElement.GetProperty("changes").EnumerateArray());
        Assert.Equal("tiles[0,0].Health", change.GetProperty("path").GetString());
        Assert.Equal(health.ToString(System.Globalization.CultureInfo.InvariantCulture), change.GetProperty("before").GetString());
        Assert.Equal((health ^ 1).ToString(System.Globalization.CultureInfo.InvariantCulture), change.GetProperty("after").GetString());
        Assert.Equal(beforeFile, File.ReadAllBytes(before));
        Assert.Equal(afterFile, File.ReadAllBytes(after));
    }
}
