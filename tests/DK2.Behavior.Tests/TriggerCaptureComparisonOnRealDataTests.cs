// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class TriggerCaptureComparisonOnRealDataTests
{
    [CaptureFact]
    public void Level1Idle_MatchesEventSetAndEveryMeasuredResidualIsAtMostOneTick()
    {
        string oursPath = Path.GetTempFileName();
        try
        {
            WriteLevel1Run(oursPath);
            TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
                oursPath, Dk2CaptureGate.Path);

            Assert.Equal(["t15", "f1", "f34", "f83"], result.InBoth);
            Assert.Empty(result.CaptureOnly);
            Assert.Empty(result.OursOnly);
            TriggerAlignedEvent[] measured = result.AlignedEvents
                .Where(e => e.Residual.HasValue).ToArray();
            Assert.Equal(["f1", "f34"], measured.Select(e => e.Id));
            Assert.All(measured,
                e => Assert.InRange(Math.Abs(e.Residual!.Value), 0, 1));
        }
        finally
        {
            File.Delete(oursPath);
        }
    }

    private static void WriteLevel1Run(string path)
    {
        string install = Dk2InstallGate.Path;
        const string levelName = "Level1";
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, levelName)!;
        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(install, levelName)!;
        List<TriggerRecord> records = GameDatabaseLoader.LoadCampaignTriggers(install, levelName)!;
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(install, levelName)!;
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, levelName)!;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, levelName);
        WorldState world = WorldState.FromMap(
            database, map, players.Select(p => p.PlayerId).ToArray());
        HeroPartyThing[] parties = things.OfType<HeroPartyThing>().ToArray();
        ThingProjection.Place(world, database, things);
        var tree = new TriggerTree(records, level.TriggerId, parties,
            players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
        var simulation = new GameSimulation(database, world, seed: 0, triggers: tree);

        using var writer = new StreamWriter(path);
        simulation.EmitTriggerTurnsTo(writer);
        for (int turn = 0; turn < 1200; turn++) simulation.Tick();
    }
}
