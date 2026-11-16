// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>
/// A Unity recording replayed through the frame journal (Godot migration step 2h): Level 1, seed 1, main-thread
/// ticks, 509 frames and 34 ticks, recorded on 2026-11-15 at 6bf8f5a2 (copied from the evidence store's
/// <c>unity-input-completion--2026-11-15T151338Z--001</c>, session <c>151516-918-level1</c>). It holds no intent
/// and no command, and its tick hashes are HashVersion 523's, so it pins the frame records and the tick schedule,
/// not a command stream or a state hash.
/// </summary>
public class UnityFrameJournalFixtureTests
{
    private const string Folder = "tests/DK2.Presentation.Tests/Fixtures/unity-level1-frames/";

    [Fact]
    public void TheRecordedFrames_RecordedAgainThroughTheSession_GiveUnitysJournalByteForByte()
    {
        byte[] recorded = File.ReadAllBytes(Dk2CaptureGate.PathFor(Folder + "frames.journal"));
        FrameInputJournal journal = FrameInputJournal.Read(new MemoryStream(recorded));
        Assert.Equal(FrameJournalSession.UnityKeyTable, journal.Header[FrameJournalSession.KeyTableKey]);
        Assert.Equal(509, journal.Frames.Count);
        string root = Path.Combine(Path.GetTempPath(), "dk2-unity-frames-" + Guid.NewGuid().ToString("N"));
        try
        {
            var warnings = new List<string>();
            var session = new FrameJournalSession(_ => { }, warnings.Add);
            session.Open(root, "session", () => journal.Header);
            var pending = new List<PlayerCommand>();
            foreach (FrameInputRecord frame in journal.Frames)
            {
                // The raw sample the host polls; the session adds the intents, the commands and the hand-off.
                session.BeginFrame(frame with
                {
                    Intents = [], Commands = [], CommandsHandedToTick = 0, TickAtStart = null, TicksRun = 0, TickDebtAfter = 0,
                }, pending);
                if (frame.TickAtStart is GameTick start) session.Ticks(start, frame.TicksRun);
                if (!ReferenceEquals(frame, journal.Frames[^1])) session.EndFrame(pending, frame.TickDebtAfter);
                // The last frame is still open when the level is destroyed: Close writes it.
                else session.Close(journal.EndReason!, pending, frame.TickDebtAfter, null);
            }

            Assert.Empty(warnings);
            Assert.True(recorded.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "session", FrameJournalSession.FramesFile))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheFramesTicks_ChainAndMatchTheTickJournal()
    {
        FrameInputJournal frames = ReadFrames();
        InputJournal ticks;
        using (FileStream stream = File.OpenRead(Dk2CaptureGate.PathFor(Folder + "input.journal"))) ticks = InputJournal.Read(stream);

        GameTick? next = null;
        int ran = 0;
        foreach (FrameInputRecord frame in frames.Frames)
        {
            Assert.NotNull(frame.TickAtStart);
            if (next is GameTick expected) Assert.Equal(expected, frame.TickAtStart);
            Assert.InRange(frame.TicksRun, 0, TickPacer.DefaultMaxTicksPerFrame);
            // Take leaves less than a tick owed unless the cap held some back.
            if (frame.TicksRun < TickPacer.DefaultMaxTicksPerFrame) Assert.InRange(frame.TickDebtAfter, 0, 1);
            Assert.Equal(0, frame.CommandsHandedToTick);
            next = new GameTick(frame.TickAtStart!.Value.Value + frame.TicksRun);
            ran += frame.TicksRun;
        }

        Assert.Equal(34, ran);
        Assert.Empty(ticks.FrameTicks);
        // The tick journal opened at the first tick and hashed every tick the frames ran.
        long first = frames.Frames[0].TickAtStart!.Value.Value;
        Assert.Equal(first.ToString(System.Globalization.CultureInfo.InvariantCulture), ticks.Header["world.start.tick"]);
        Assert.Equal(Enumerable.Range((int)first, ran + 1).Select(t => (long)t), ticks.Hashes.Keys.Order());
    }

    private static FrameInputJournal ReadFrames()
    {
        using FileStream stream = File.OpenRead(Dk2CaptureGate.PathFor(Folder + "frames.journal"));
        return FrameInputJournal.Read(stream);
    }
}
