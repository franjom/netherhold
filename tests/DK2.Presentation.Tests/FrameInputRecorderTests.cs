// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>The journal's per-frame bookkeeping (ADR-0553 Phase 1) attributes each queued command
/// to when it was queued relative to the frame's tick hand-off, as <c>DungeonRunner</c> drives it.</summary>
public class FrameInputRecorderTests
{
    private static readonly PlayerId Keeper = new(PlayerIds.FirstKeeper);

    [Fact]
    public void Commands_AreTaggedByWhenTheyWereQueued_AndTheHandOffIsCounted()
    {
        var pending = new List<PlayerCommand>();
        var recorder = new FrameInputRecorder();
        PlayerCommand waiting = Imp(1), between = Imp(2), during = Imp(3), after = Imp(4);

        // Frame 1 runs no tick: its command waits in the queue.
        recorder.BeginFrame(Raw(1), pending);
        pending.Add(waiting);
        FrameInputRecord first = recorder.EndFrame(pending, tickDebt: 0.5)!;

        // A UI Toolkit callback between frames, then frame 2 queues one, hands all three to its
        // tick, and queues one more after the hand-off.
        pending.Add(between);
        recorder.BeginFrame(Raw(2), pending);
        pending.Add(during);
        recorder.HandOff(pending);
        pending.Clear();
        recorder.Ticks(new GameTick(40), 1);
        pending.Add(after);
        FrameInputRecord second = recorder.EndFrame(pending, tickDebt: 0.25)!;

        Assert.Equal([(CommandQueuedWhen.DuringFrame, waiting)], Queued(first));
        Assert.Equal(0, first.CommandsHandedToTick);
        Assert.Null(first.TickAtStart);
        Assert.Equal(
            [(CommandQueuedWhen.BeforeUpdate, between), (CommandQueuedWhen.DuringFrame, during), (CommandQueuedWhen.AfterHandOff, after)],
            Queued(second));
        Assert.Equal(3, second.CommandsHandedToTick);
        Assert.Equal(new GameTick(40), second.TickAtStart);
        Assert.Equal(1, second.TicksRun);
        Assert.Equal(0.25, second.TickDebtAfter);

        // The command left after the hand-off is not counted again by the next frame.
        recorder.BeginFrame(Raw(3), pending);
        Assert.Empty(recorder.EndFrame(pending, 0)!.Commands);
    }

    [Fact]
    public void Intents_BelongToTheirFrame_AndAnOpenFrameShowsWhatItHasSoFar()
    {
        var pending = new List<PlayerCommand>();
        var recorder = new FrameInputRecorder();
        var intent = new PlayerIntent(Keeper, PlayerToolMode.Hand, new TilePos(2, 3), PointerButtonTransition.Pressed);

        Assert.Null(recorder.Open);
        recorder.Intent(intent); // outside a frame: ignored
        recorder.BeginFrame(Raw(1), pending);
        recorder.Intent(intent);
        pending.Add(Imp(1));

        Assert.Equal([intent], recorder.Open!.Intents);
        FrameInputRecord done = recorder.EndFrame(pending, 0)!;
        Assert.Equal([intent], done.Intents);
        Assert.Single(done.Commands);
        Assert.Null(recorder.EndFrame(pending, 0));

        recorder.BeginFrame(Raw(2), pending);
        Assert.Empty(recorder.EndFrame(pending, 0)!.Intents);
    }

    private static FrameInputRecord Raw(long frame) => new() { Frame = frame };

    private static PlayerCommand Imp(int x) => new CreateImpCommand(Keeper, new TilePos(x, 1));

    private static (CommandQueuedWhen, PlayerCommand)[] Queued(FrameInputRecord frame) =>
        frame.Commands.Select(c => (c.When, c.Command)).ToArray();
}
