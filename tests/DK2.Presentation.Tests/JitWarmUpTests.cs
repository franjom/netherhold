// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class JitWarmUpTests
{
    private static int s_initialised;

    private static class Untouched
    {
        static Untouched() => s_initialised++;

        public static int Answer() => 42;
    }

    [Fact]
    public void PreparingTypesCompilesTheirMethodsAndRunsNoStaticConstructor()
    {
        int prepared = JitWarmUp.Prepare([typeof(Untouched), typeof(JitWarmUp), typeof(LevelWarmUp)]);

        Assert.True(prepared >= 5, $"prepared {prepared}");
        Assert.Equal(0, s_initialised);
        Assert.Equal(42, Untouched.Answer());
        Assert.Equal(1, s_initialised);
    }

    [Fact]
    public void ItStopsBetweenTwoMethodsWhenAskedTo()
    {
        int asked = 0;

        int prepared = JitWarmUp.Prepare([typeof(JitWarmUp), typeof(LevelWarmUp)], () => ++asked > 3);

        Assert.Equal(3, prepared);
    }
}
