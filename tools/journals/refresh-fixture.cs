#:project ../../src/DK2.Harness/DK2.Harness.csproj
#:property PublishAot=false

// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using System.Globalization;

// Refresh remake fixture hashes, retaining the recorded commands and their provenance.
// dotnet run tools/journals/refresh-fixture.cs -- scenario.yaml input.journal new.journal install
if (args.Length != 4)
    throw new ArgumentException("Expected scenario.yaml input.journal new.journal install.");
if (File.Exists(args[2]))
    throw new IOException("The output must be a new file; the input is never overwritten.");

Scenario scenario = Scenario.Load(args[0]);
InputJournal original;
using (FileStream input = File.OpenRead(args[1])) original = InputJournal.Read(input);
if (original.Truncated || original.EndReason != "completed")
    throw new InvalidDataException("Only complete successful remake fixtures can be refreshed.");

var runner = new ScenarioRunner(GameDatabaseLoader.Load(args[3]), args[3]);
byte? finalPlayerStatus = null;
PlayerId? recordedPlayer = JournalHeader.IsPlayerRun(original)
    ? new PlayerId(byte.Parse(original.Header["player.id"], CultureInfo.InvariantCulture)) : null;
var options = new RunOptions
{
    AfterTick = (_, simulation) =>
    {
        if (recordedPlayer is PlayerId player) finalPlayerStatus = simulation.World.Player(player).Status;
    },
};
Console.WriteLine($"Replaying {original.FrameTicks.Count()} command frames at HashVersion {GameSimulation.CurrentHashVersion}.");
ScenarioResult result = runner.Replay(scenario, original, options, acceptOtherHashVersion: true);
if (recordedPlayer is not null && finalPlayerStatus != PlayerStatus.Won)
    throw new InvalidDataException("The completed player journal no longer reproduces a win.");
long ticks = original.TicksRun!.Value;
if (result.TicksRun != ticks || result.HashPerTick.Count != ticks + 1)
    throw new InvalidDataException("The refreshed run did not reproduce the recorded tick count.");

using var refreshed = new MemoryStream();
using (var writer = new InputJournalWriter(refreshed, original.Header))
{
    writer.WriteHash(0, result.HashPerTick[0]);
    for (int tick = 0; tick < ticks; tick++)
    {
        writer.WriteFrame(tick, original.FrameAt(tick));
        writer.WriteHash(tick + 1, result.HashPerTick[tick + 1]);
    }
    writer.WriteEnd(ticks, original.EndReason!);
}
refreshed.Position = 0;
InputJournal updated = InputJournal.Read(refreshed);
if (updated.HashVersion != GameSimulation.CurrentHashVersion ||
    updated.TicksRun != original.TicksRun || updated.EndReason != original.EndReason ||
    !updated.Header.OrderBy(p => p.Key).SequenceEqual(original.Header.OrderBy(p => p.Key)) ||
    !Commands(original).AsSpan().SequenceEqual(Commands(updated)))
    throw new InvalidDataException("Refreshing changed recorded commands, timing or provenance.");

ScenarioResult verified = runner.Replay(scenario, updated);
if (verified.Replay is not { Matches: true } || verified.Replay.ComparedTicks != ticks + 1)
    throw new InvalidDataException("The refreshed hashes did not match a second independent replay.");
using (var output = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write))
    output.Write(refreshed.ToArray());
Console.WriteLine($"HashVersion {original.HashVersion} -> {updated.HashVersion}; " +
    $"{ticks} ticks, {updated.FrameTicks.Count()} command frames, " +
    $"{verified.Replay.ComparedTicks} matching hashes; commands and header unchanged.");

static byte[] Commands(InputJournal journal)
{
    using var stream = new MemoryStream();
    using (var writer = new InputJournalWriter(stream, new Dictionary<string, string>()))
        foreach (long tick in journal.FrameTicks) writer.WriteFrame(tick, journal.FrameAt(tick));
    return stream.ToArray();
}
