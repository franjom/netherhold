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
            WriteRun(oursPath, "Level1", turns: 1200);
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

    /// <summary>Level 20 idle, ticks 36-1218: nothing is due, and both sides do nothing. The
    /// value is in the stamps, which are stored rather than sampled — the game and we both
    /// stamp timers 0 and 1 at tick 4, so absolute tick numbering agrees with no offset and
    /// no sampling anywhere in the path. That is what the level-1 anchor could not show,
    /// because there the anchor was the thing being aligned on (Q273).</summary>
    [CaptureFact(Dk2CaptureGate.Level20IdleRelativePath)]
    public void Level20Idle_AgreesOnTheStoredStampsAndOnTheSilenceBetweenThem()
    {
        string oursPath = Path.GetTempFileName();
        try
        {
            WriteRun(oursPath, "Level20", turns: 2600);
            TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
                oursPath, Dk2CaptureGate.PathFor(Dk2CaptureGate.Level20IdleRelativePath));

            Assert.Equal(["t0", "t1", "f7"], result.InBoth);
            Assert.Empty(result.CaptureOnly);
            Assert.Empty(result.OursOnly);
            Assert.Equal(4, result.OursOrigin);
            Assert.Equal(4, result.CaptureOrigin);

            // f6 is due at 2404 and the capture stopped at 1218, so it is untested rather
            // than divergent. Counting it as a divergence is the defect this run exposed.
            Assert.Equal(["f6"], result.OursBeyondCapture);
            Assert.True(result.EventSetsMatch);
        }
        finally
        {
            File.Delete(oursPath);
        }
    }

    /// <summary>The run that answered Q273 (<c>level20-idle-002</c>). Timer 1 restamps inside
    /// the window, and a restamp is a value the game stores rather than one the probe times:
    /// DK2 wrote <b>1724</b> and so do we, exactly. The probe first saw it at 1725, which
    /// dates its own lag at one tick against the game's record — and that one tick is the
    /// whole of the residual on every sampled flag here and on level 1.</summary>
    [CaptureFact(Dk2CaptureGate.Level20LongIdleRelativePath)]
    public void Level20LongIdle_StoredStampsAgreeExactlyAndOnlySampledTurnsLag()
    {
        string oursPath = Path.GetTempFileName();
        try
        {
            WriteRun(oursPath, "Level20", turns: 2620);
            TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
                oursPath, Dk2CaptureGate.PathFor(Dk2CaptureGate.Level20LongIdleRelativePath));

            Assert.Equal(["t0", "t1", "f6", "f7"], result.InBoth);
            Assert.Empty(result.CaptureOnly);
            Assert.Empty(result.OursOnly);

            TriggerStampSeries t1 = result.StampSeries.Single(s => s.Id == "t1");
            Assert.Equal([0L, 1720L], t1.Ours);
            Assert.Equal([0L, 1720L], t1.Capture);
            Assert.True(result.StampsAgree);

            // The sampled half lags by exactly one, and by no more.
            Assert.Equal(1, result.AlignedEvents.Single(e => e.Id == "f6").Residual);
        }
        finally
        {
            File.Delete(oursPath);
        }
    }

    private static void WriteRun(string path, string levelName, int turns)
    {
        string install = Dk2InstallGate.Path;
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
        for (int turn = 0; turn < turns; turn++) simulation.Tick();
    }
}
