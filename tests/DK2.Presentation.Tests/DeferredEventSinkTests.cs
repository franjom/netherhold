// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class DeferredEventSinkTests
{
    [Fact]
    public void WorkerEventsReachTheViewOnlyOnReplay_InOrderAndExactlyOnce()
    {
        var received = new List<(SimEvent Event, int Thread)>();
        var sink = new DeferredEventSink(new RecordingSink(received));
        int frameThread = Environment.CurrentManagedThreadId;
        var events = Enumerable.Range(0, 1000)
            .Select(i => new SimEvent(new GameTick(i), "test", "event", i.ToString())).ToArray();
        Exception? workerFailure = null;
        var worker = new Thread(() =>
        {
            try { foreach (SimEvent item in events) sink.Emit(item); }
            catch (Exception error) { workerFailure = error; }
        });
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(workerFailure);
        Assert.Empty(received);

        sink.Replay();
        sink.Replay();

        Assert.Equal(events, received.Select(item => item.Event));
        Assert.All(received, item => Assert.Equal(frameThread, item.Thread));
        SimEvent next = new(new GameTick(1000), "test", "next-batch", "");
        sink.Emit(next);
        Assert.Equal(1000, received.Count);
        sink.Replay();
        Assert.Equal(next, received[^1].Event);
    }

    private sealed class RecordingSink(List<(SimEvent Event, int Thread)> received) : ISimulationEventSink
    {
        public void Emit(in SimEvent simEvent) => received.Add((simEvent, Environment.CurrentManagedThreadId));
    }
}
