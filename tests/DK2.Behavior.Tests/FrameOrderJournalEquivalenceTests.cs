// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Presentation;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// The journal-equivalence harness of the frame order (Godot migration step 2h): a recorded command stream,
/// queued frame by frame as an adapter queues its input, goes through the frame driver's tick hand-off and must
/// come out as the same per-tick input. The recording is the campaign player's Level 1 journal (seed 1, 2,926
/// ticks, 164 command frames). See docs/research/hash-versions.md for fixture refreshes.
/// Frames are cut so that some commands wait through a paused, a stopped or a short frame, some arrive before
/// the frame (a callback between frames), and some frames run two ticks.
/// </summary>
public class FrameOrderJournalEquivalenceTests
{
    private const string Fixture = "tests/DK2.Behavior.Tests/Fixtures/level1-seed1-win/";

    [Fact]
    public void TheRecordedCommandStream_ComesOutOfTheHandOffByteForByte()
    {
        byte[] recorded = File.ReadAllBytes(Dk2CaptureGate.PathFor(Fixture + "input.journal"));
        var journal = InputJournal.Read(new MemoryStream(recorded));
        Replayed replayed = Drive(journal);

        byte[] rewritten = Rewrite(journal, replayed.Given);
        int differs = 0;
        while (differs < Math.Min(recorded.Length, rewritten.Length) && recorded[differs] == rewritten[differs])
        {
            differs++;
        }

        Assert.True(recorded.AsSpan().SequenceEqual(rewritten),
            $"first difference at byte {differs} of {recorded.Length} (rewritten {rewritten.Length})");
        Assert.True(replayed.Frames.Count > journal.TicksRun, "the schedule must hold frames that run no tick");
    }

    [Fact]
    public void EachFramesJournalRecord_HoldsWhatItQueuedAndWhatItHandedOff()
    {
        InputJournal journal = Read();
        Replayed replayed = Drive(journal);

        int checkedFrames = 0;
        foreach (long tick in journal.FrameTicks)
        {
            IReadOnlyList<PlayerCommand> commands = journal.FrameAt(tick).All;
            // The frame that ran this tick, and every frame since the previous hand-off.
            int handing = replayed.HandingFrame[tick];
            int first = handing;
            while (first > 0 && replayed.Frames[first - 1].CommandsHandedToTick == 0)
            {
                first--;
            }

            var queued = new List<PlayerCommand>();
            for (int i = first; i <= handing; i++)
            {
                foreach (QueuedCommand command in replayed.Frames[i].Commands)
                {
                    queued.Add(command.Command);
                }
            }

            Assert.Equal(Encoded(commands), Encoded(queued));
            Assert.Equal(commands.Count, replayed.Frames[handing].CommandsHandedToTick);
            Assert.Equal(new GameTick(tick), replayed.Frames[handing].TickAtStart);
            foreach (QueuedCommand command in replayed.Frames[handing].Commands)
            {
                Assert.NotEqual(CommandQueuedWhen.AfterHandOff, command.When);
            }

            checkedFrames++;
        }
        Assert.Equal(164, checkedFrames);
        Assert.Contains(replayed.Frames, f => f.Commands.Any(c => c.When == CommandQueuedWhen.BeforeUpdate));
        Assert.Contains(replayed.Frames, f => f.TicksRun == 2);
    }

    /// <summary>A sanity replay: the bytes it replays are the ones the byte-for-byte test proves equal to the recording, so it
    /// adds no frame-order evidence. It says that this build's simulation, fed the driver's frames, still gives the recorded
    /// hashes, which the frame-order verdict above depends on.</summary>
    [InstallFact]
    public void SanityReplay_TheDriversFramesStillReplayToTheRecordedHashes()
    {
        InputJournal journal = Read();
        var rebuilt = InputJournal.Read(new MemoryStream(Rewrite(journal, Drive(journal).Given)));

        ScenarioResult result = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path).Replay(
            Scenario.Load(Dk2CaptureGate.PathFor(Fixture + "level1.yaml")), rebuilt);

        Assert.True(result.Replay!.Matches, $"first divergent tick {result.Replay.FirstDivergentTick}");
        Assert.Equal(journal.TicksRun!.Value + 1, result.Replay.ComparedTicks);
    }

    /// <summary>The commands as the journal encodes them: a command decoded from a journal holds new arrays.</summary>
    private static byte[] Encoded(IEnumerable<PlayerCommand> commands)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            foreach (PlayerCommand command in commands)
            {
                CommandCodec.Write(writer, command);
            }
        }

        return stream.ToArray();
    }

    private static InputJournal Read()
    {
        using FileStream stream = File.OpenRead(Dk2CaptureGate.PathFor(Fixture + "input.journal"));
        return InputJournal.Read(stream);
    }

    /// <summary>The recorded journal rewritten with the driver's per-tick input and the recorded hashes.</summary>
    private static byte[] Rewrite(InputJournal journal, IReadOnlyList<InputFrame> given)
    {
        var stream = new MemoryStream();
        using (var writer = new InputJournalWriter(stream, journal.Header))
        {
            writer.WriteHash(0, journal.Hashes[0]);
            for (int t = 0; t < given.Count; t++)
            {
                writer.WriteFrame(t, given[t]);
                writer.WriteHash(t + 1, journal.Hashes[t + 1]);
            }
            writer.WriteEnd(journal.TicksRun!.Value, journal.EndReason!);
        }
        return stream.ToArray();
    }

    private sealed record Replayed(List<InputFrame> Given, List<FrameInputRecord> Frames, Dictionary<long, int> HandingFrame);

    /// <summary>One frame of the schedule: commands queued before it (a callback between frames) and during it, and
    /// its clock.</summary>
    private sealed record Step(List<PlayerCommand> Before, List<PlayerCommand> During, double Elapsed, float Speed, bool Paused);

    private static readonly double OneTick = 1.0 / SimulationTiming.TicksPerSecond;

    private static IEnumerable<Step> Schedule(InputJournal journal)
    {
        long ticks = journal.TicksRun!.Value;
        int k = 0; // the command frames so far: the recording's commands all land on ticks 1 mod 4
        for (long t = 0; t < ticks; t++)
        {
            IReadOnlyList<PlayerCommand> commands = journal.FrameAt(t).All;
            bool nextEmpty = t + 1 < ticks && journal.FrameAt(t + 1).All.Count == 0;
            bool twoTicks = commands.Count > 0 && nextEmpty && k % 4 == 1;
            double elapsed = twoTicks ? 2 * OneTick : OneTick;
            if (commands.Count == 0)
            {
                yield return new Step([], [], elapsed, 1f, false);
                continue;
            }
            int half = commands.Count / 2;
            List<PlayerCommand> early = [.. commands.Take(half)], late = [.. commands.Skip(half)];
            switch (k++ % 4)
            {
                case 0: // the first half waits through a paused frame
                    yield return new Step([], early, OneTick, 1f, true);
                    yield return new Step([], late, elapsed, 1f, false);
                    break;
                case 1: // through a frame whose clock reached no tick
                    yield return new Step([], early, 0, 1f, false);
                    yield return new Step([], late, elapsed, 1f, false);
                    break;
                case 2: // through a frame at speed 0
                    yield return new Step([], early, OneTick, 0f, false);
                    yield return new Step([], late, elapsed, 1f, false);
                    break;
                default: // the first half queued between frames
                    yield return new Step(early, late, elapsed, 1f, false);
                    break;
            }
            if (twoTicks)
            {
                t++;
            }
        }
    }

    private static readonly PlayerId Viewer = new PlayerId(3);

    /// <summary>Queues the schedule frame by frame through <see cref="FrameDriver"/>'s order, journalled by a
    /// <see cref="FrameJournalSession"/> into a temporary folder, and hands each frame's ticks their input. Nothing is
    /// possessed (no pose), held (no drops) or scripted (no path), so those steps run and add nothing.</summary>
    private static Replayed Drive(InputJournal journal)
    {
        GameDatabase database = ScenarioTests.SyntheticDatabase();
        var world = WorldState.FromMap(database, new LevelMap(4, 4, [.. Enumerable.Repeat(new MapTile(3, 0, 0, 0), 16)]),
            [Viewer.Value]);
        var pending = new List<PlayerCommand>();
        var pacer = new TickPacer();
        string root = Path.Combine(Path.GetTempPath(), "dk2-frame-order-" + Guid.NewGuid().ToString("N"));
        var warnings = new List<string>();
        var session = new FrameJournalSession(_ => { }, warnings.Add);
        try
        {
            session.Open(root, "harness", () => FrameJournalSession.Header("test", FrameJournalSession.GodotKeyTable,
                GameSessionMode.Campaign, "level1", 1, Viewer, true, false, 0, 0));
            Assert.True(session.Recording);
            var script = new ScriptedViewDirector(Viewer, _ => null, _ => null);
            var driver = new FrameDriver(pending, new WorldView(database, Viewer), pacer, session);
            var given = new List<InputFrame>();
            var handing = new Dictionary<long, int>();
            long frame = 0;
            foreach (Step step in Schedule(journal))
            {
                pending.AddRange(step.Before);
                session.BeginFrame(new FrameInputRecord { Frame = frame }, pending);
                driver.UpdateScript(script, frame, world, Viewer, ticksPending: 0);
                driver.AdvancePickups(world, Viewer, frame);
                Assert.Null(driver.TakeScriptedInput(script, Viewer, skipKey: true).Glide);
                pending.AddRange(step.During);
                FrameTicks ticks = driver.TakeTicks(world, Viewer, step.Elapsed, step.Speed, 4, step.Paused,
                    TickPacer.DefaultMaxTicksPerFrame, poses: null);
                long start = given.Count;
                session.Ticks(new GameTick(start), ticks.Count);
                for (int i = 0; i < ticks.Count; i++)
                {
                    given.Add(ticks.InputFor(i));
                }

                if (ticks.FirstInput.Commands != null)
                {
                    handing[start] = (int)frame;
                }

                driver.AfterTicks(world, frame);
                session.EndFrame(pending, pacer.Debt);
                frame++;
            }
            Assert.Empty(pending);
            session.Close("completed", pending, pacer.Debt, null);
            Assert.Empty(warnings);
            FrameInputJournal frames;
            using (FileStream stream = File.OpenRead(Path.Combine(root, "harness", FrameJournalSession.FramesFile)))
            {
                frames = FrameInputJournal.Read(stream);
            }

            Assert.Equal(frame, frames.FramesWritten);
            Assert.Equal(FrameJournalSession.GodotKeyTable, frames.Header[FrameJournalSession.KeyTableKey]);
            return new Replayed(given, [.. frames.Frames], handing);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
