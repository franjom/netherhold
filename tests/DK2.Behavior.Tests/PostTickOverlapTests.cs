// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Harness;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

[Collection(TimedAlone.Name)]
public class PostTickOverlapTests
{
    [InstallTheory]
    [InlineData("keepers/level11a_drako_attack.yaml", false)]
    [InlineData("keepers/level18_nemesis_allies.yaml", false)]
    [InlineData("keepers/level18_nemesis_allies.yaml", true)]
    public void OverlappedAndSerialRunsAreIdentical(string relative, bool monitor)
    {
        Scenario scenario = ScenarioCorpus.Load(relative);
        var runner = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path);
        var options = new RunOptions
        {
            OverlapStateHash = false,
            Coverage = monitor,
            ProgressEvery = monitor ? 40 : 0,
            SnapshotCheckEvery = monitor ? 40 : 0,
            SnapshotCheckTicks = 5,
        };
        ScenarioResult serial = runner.Run(scenario, options);
        ScenarioResult overlapped = runner.Run(scenario, options with { OverlapStateHash = true });

        Assert.Equal(serial.HashPerTick, overlapped.HashPerTick);
        Assert.NotEmpty(serial.KeeperCommandLog);
        Assert.Equal(serial.KeeperCommandLog.Select(e => (e.InputTick, Fingerprint(e.Command))),
            overlapped.KeeperCommandLog.Select(e => (e.InputTick, Fingerprint(e.Command))));
        Assert.Equal(serial.Events, overlapped.Events);
        Assert.Equal(serial.Progress, overlapped.Progress);
        Assert.Equal(serial.SnapshotChecks, overlapped.SnapshotChecks);
        Assert.Empty(serial.InvariantFailures);
        Assert.Empty(overlapped.InvariantFailures);
        if (monitor)
        {
            Assert.NotEmpty(serial.Progress);
            Assert.True(serial.SnapshotChecks > 0);
            Assert.Equal(serial.Coverage!.TriggerRecordsReached, overlapped.Coverage!.TriggerRecordsReached);
            Assert.Equal(serial.Coverage.CreatureTicksByState.OrderBy(e => e.Key), overlapped.Coverage.CreatureTicksByState.OrderBy(e => e.Key));
        }
    }

    private static ulong Fingerprint(PlayerCommand command)
    {
        Hash64 hash = Hash64.Start();
        command.HashInto(ref hash);
        return hash.Value;
    }
}
