// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>
/// Level 1's Horned Reaper finale on a recorded win (campaign-win-sequence.md §2-§3): the campaign
/// player's journal for seed 1, won by player 3 at tick 2301 with a 1,500-tick finale window. The Lord
/// of the Land carries the Portal Gem (action 20) and drops it where he dies; the Reaper must reach it.
/// </summary>
public class ReaperFinaleOnRealDataTests(ITestOutputHelper output)
{
    private const string Fixture = "tests/DK2.Behavior.Tests/Fixtures/level1-seed1-win/";
    private const long WinTick = 2301;
    /// <summary>The journal's finale window (its <c>player.limits</c> <c>finale_ticks</c>).</summary>
    private const long FinaleWindowTicks = 1500;
    private static readonly PlayerId Winner = new(3);

    [InstallFact]
    public void TheReaperTakesTheGemTheLordDroppedWithinTheFinaleWindow()
    {
        long pickup = 0;
        (int X, int Y)? gemAtWin = null;
        var options = new RunOptions
        {
            AfterTick = (tick, simulation) =>
            {
                ref PlayerState player = ref simulation.World.Player(Winner);
                if (gemAtWin is null && player.Finale.GemObjectId != 0)
                    gemAtWin = (player.Finale.GemTileX, player.Finale.GemTileY);
                if (pickup == 0) pickup = player.Finale.PickupTick;
            },
        };
        using FileStream journal = File.OpenRead(Dk2CaptureGate.PathFor(Fixture + "input.journal"));
        // The journal was re-recorded at HashVersion 484 (own-land regeneration every turn).
        // A later hash version replays it with its hashes as evidence, not a verdict.
        ScenarioResult result = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path).Replay(
            Scenario.Load(Dk2CaptureGate.PathFor(Fixture + "level1.yaml")), InputJournal.Read(journal), options,
            acceptOtherHashVersion: true);

        // The win tick and the Lord's death tile are this remake run's output, not DK2 facts: if a later rule
        // changes Level 1 before the win, re-record the journal rather than tuning these.
        SimEvent created = Assert.Single(result.Events, e => e.Kind == "reaper_created");
        Assert.True(created.Tick.Value == WinTick,
            $"The replay no longer wins at t{WinTick} (t{created.Tick.Value}); the journal needs re-recording.");
        Assert.True(gemAtWin == (14, 20), $"The gem is at {gemAtWin} at the win, not where the Lord died (14,20).");
        Assert.InRange(pickup, WinTick + 1, WinTick + FinaleWindowTicks);
        Assert.Contains(result.Events, e => e.Kind == "gem_taken" && e.Tick.Value == pickup);
        output.WriteLine($"pickup t{pickup}; first hash difference from the journal: t{result.Replay!.FirstDivergentTick}");
    }
}
