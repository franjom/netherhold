// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation.Tests;

public class SpeechQueueTests
{
    private static SoundRequest Line(uint id, string bank = "speech_mentor") => new(new SoundCategory(bank), new SoundId(id));

    [Fact]
    public void LinesAreFifoAndWaitStrictlyMoreThan1000MillisecondsAfterCompletion()
    {
        var queue = new SpeechQueue();
        queue.Enqueue(Line(1)); queue.Enqueue(Line(2));
        Assert.Equal(Line(1), queue.Update(100, false));
        Assert.Null(queue.Update(200, true));
        Assert.Null(queue.Update(300, false));
        Assert.Null(queue.Update(1300, false));
        Assert.Equal(Line(2), queue.Update(1301, false));
    }

    [Fact]
    public void DedupeUsesTheQueuedLineAndCategoryButDoesNotInventARepeatTimer()
    {
        var queue = new SpeechQueue();
        Assert.True(queue.Enqueue(Line(1)));
        Assert.False(queue.Enqueue(Line(1)));
        Assert.True(queue.Enqueue(Line(1, "speech_level1")));
        Assert.Equal(Line(1), queue.Update(1, false));
        Assert.True(queue.Enqueue(Line(1)));
        queue.ClearWaiting();
        Assert.Equal(Line(1), queue.Current);
        Assert.Equal(0, queue.WaitingCount);
    }

    [Fact]
    public void TheSeventeenthWaitingLineIsRejected()
    {
        var queue = new SpeechQueue();
        for (uint i = 1; i <= 16; i++) Assert.True(queue.Enqueue(Line(i)));
        Assert.False(queue.Enqueue(Line(17)));
    }
}
