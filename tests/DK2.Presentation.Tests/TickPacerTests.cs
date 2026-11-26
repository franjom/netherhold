// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class TickPacerTests
{
    [Fact]
    public void TheFrameClampIsOneThirdOfASecond() =>
        Assert.Equal(0.33333334f, (float)TickPacer.MaximumFrameSeconds);

    [Fact]
    public void AFrameRunsAtMostTwoTicks() => Assert.Equal(2, TickPacer.DefaultMaxTicksPerFrame);
}
