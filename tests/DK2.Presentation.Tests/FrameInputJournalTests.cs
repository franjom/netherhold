// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>The Unity frame input journal (ADR-0553 Phase 1) reads back what was written, and a
/// session cut short keeps every whole frame.</summary>
public class FrameInputJournalTests
{
    private static readonly PlayerId Keeper = new(PlayerIds.FirstKeeper);

    [Fact]
    public void AFrame_ReadsBackAsWritten()
    {
        FrameInputRecord frame = Busy(frame: 7);
        using var stream = new MemoryStream();
        using (var writer = new FrameInputJournalWriter(stream, new Dictionary<string, string> { ["source"] = "unity" }))
        {
            writer.Write(frame);
            writer.WriteEnd(1, "quit");
        }

        stream.Position = 0;
        FrameInputJournal journal = FrameInputJournal.Read(stream);

        Assert.Equal("unity", journal.Header["source"]);
        Assert.Equal(1, journal.FramesWritten);
        Assert.Equal("quit", journal.EndReason);
        FrameInputRecord read = Assert.Single(journal.Frames);
        // Records compare their lists by reference, so the lists are compared one by one.
        Assert.Equal(frame with { KeysDown = [], KeysUp = [], KeysHeld = [], Intents = [], Commands = [] },
            read with { KeysDown = [], KeysUp = [], KeysHeld = [], Intents = [], Commands = [] });
        Assert.Equal(frame.KeysDown, read.KeysDown);
        Assert.Equal(frame.KeysUp, read.KeysUp);
        Assert.Equal(frame.KeysHeld, read.KeysHeld);
        Assert.Equal(frame.Intents, read.Intents);
        Assert.Equal(frame.Commands.Select(c => c.When), read.Commands.Select(c => c.When));
        Assert.Equal(frame.Commands.Select(c => Encoded(c.Command)), read.Commands.Select(c => Encoded(c.Command)));
    }

    [Fact]
    public void ACutShortJournal_KeepsEveryWholeFrame()
    {
        using var stream = new MemoryStream();
        using (var writer = new FrameInputJournalWriter(stream, new Dictionary<string, string>()))
        {
            writer.Write(Busy(frame: 1));
            writer.Write(Busy(frame: 2));
        }
        byte[] whole = stream.ToArray();
        int headerEnd = HeaderLength();
        int firstEnd = headerEnd + (whole.Length - headerEnd) / 2; // two equal-sized frames

        // Every cut: inside the first frame's tag, length or payload, at its end, and inside the second.
        for (int cut = headerEnd; cut < whole.Length; cut++)
        {
            FrameInputJournal journal = FrameInputJournal.Read(new MemoryStream(whole, 0, cut));
            Assert.True(journal.Truncated);
            Assert.Equal(cut < firstEnd ? Array.Empty<long>() : new[] { 1L }, journal.Frames.Select(f => f.Frame).ToArray());
        }
        Assert.Equal([1L, 2L], FrameInputJournal.Read(new MemoryStream(whole)).Frames.Select(f => f.Frame));
    }

    [Fact]
    public void AJournalCutInsideItsEndRecord_IsTruncated()
    {
        using var stream = new MemoryStream();
        using (var writer = new FrameInputJournalWriter(stream, new Dictionary<string, string>()))
        {
            writer.Write(Busy(frame: 1));
            writer.WriteEnd(1, "released");
        }
        byte[] whole = stream.ToArray();

        FrameInputJournal journal = FrameInputJournal.Read(new MemoryStream(whole, 0, whole.Length - 3));
        Assert.True(journal.Truncated);
        Assert.Single(journal.Frames);
    }

    [Fact]
    public void AJournalCutInsideItsHeader_IsRefused()
    {
        using var stream = new MemoryStream();
        using (new FrameInputJournalWriter(stream, new Dictionary<string, string> { ["commit"] = "abc" })) { }

        Assert.Throws<InvalidDataException>(() => FrameInputJournal.Read(new MemoryStream(stream.ToArray(), 0, 10)));
    }

    private static FrameInputRecord Busy(long frame) => new()
    {
        Frame = frame,
        UnscaledDeltaSeconds = 1 / 60d,
        Mode = FrameInputMode.Game,
        TickAtStart = new GameTick(120),
        TicksRun = 2,
        CommandsHandedToTick = 1,
        TickDebtAfter = 0.25,
        PointerX = 400.5f,
        PointerY = 300,
        ButtonsHeld = FrameMouseButtons.Primary,
        ButtonsDown = FrameMouseButtons.Primary,
        WheelY = -1,
        PanHorizontal = 1,
        TypedText = "ab",
        KeysDown = [new HostKey(97)],
        KeysHeld = [new HostKey(97), new HostKey(304)],
        KeysUp = [new HostKey(98)],
        Hud = FrameHudFlags.OrdinaryInput | FrameHudFlags.CameraPath,
        ActiveTool = PlayerToolMode.Hand,
        Intents =
        [
            new PlayerIntent(Keeper, PlayerToolMode.Hand, new TilePos(4, 5), PointerButtonTransition.Pressed,
                RoomTypeId.Treasury, PointerOverHud: false, PointerPosition: new FpPosition2(new Fp32(4096 * 4 + 7), new Fp32(4096 * 5))),
            new PlayerIntent(Keeper, PlayerToolMode.TagTiles, null, PointerButtonTransition.Released,
                SecondaryButton: PointerButtonTransition.Pressed | PointerButtonTransition.Released, PointerOverHud: true),
        ],
        Commands =
        [
            new QueuedCommand(CommandQueuedWhen.BeforeUpdate, new CreateImpCommand(Keeper, new TilePos(4, 5))),
            new QueuedCommand(CommandQueuedWhen.DuringFrame, TagTilesCommand.Tag(Keeper, new TilePos(4, 5))),
            new QueuedCommand(CommandQueuedWhen.AfterHandOff, new SlapCreatureCommand(Keeper, 12)),
        ],
    };

    private static byte[] Encoded(PlayerCommand command)
    {
        using var buffer = new MemoryStream();
        using (var w = new BinaryWriter(buffer)) CommandCodec.Write(w, command);
        return buffer.ToArray();
    }

    private static int HeaderLength()
    {
        using var stream = new MemoryStream();
        using (new FrameInputJournalWriter(stream, new Dictionary<string, string>())) { }
        return (int)stream.Length;
    }
}
