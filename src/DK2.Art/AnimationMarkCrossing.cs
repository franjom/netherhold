// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Art;

/// <summary>The animation-mark crossing test of <c>0x004B4630</c>: whether either of an animation's two normalized marks
/// lies strictly inside the phase interval between two drawn frames (docs/research/sound-effects-continuous.md section 1).
/// The interval is the turn's phase step (the previous turn's phase to the current one, wrapped at 4096) scaled by the two
/// frames' fractions of the turn in 1/256ths. Presentation only: it reads a clock, never the simulation.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-continuous.md#1-footsteps")]
public static class AnimationMarkCrossing
{
    private const int One = 4096;

    /// <summary>0 for no crossing, 1 for one before the clip's wrap, 2 for one after it. A zero second mark takes
    /// the first (the native test writes it back); both zero is none.</summary>
    public static int Test(int mark1, int mark2, int previousPhase, int phase, int from, int to)
    {
        if (mark1 == 0)
        {
            if (mark2 == 0) return 0;
        }
        else if (mark2 == 0) mark2 = mark1;
        int step = phase - previousPhase;
        if (step < 0) step += One;
        int start = previousPhase + Scale(step, from);
        int end = previousPhase + Scale(step, to);
        if (Outside(mark1, mark2, start, end))
        {
            do
            {
                if (end <= One) return 0;
                start -= One;
                end -= One;
            }
            while (Outside(mark1, mark2, start, end));
            return 2;
        }
        return 1;
    }

    private static bool Outside(int mark1, int mark2, int start, int end) =>
        (mark1 <= start || end <= mark1) && (mark2 <= start || end <= mark2);

    /// <summary>The native product shifted right eight after adding 255 to a negative one: a division by 256 toward zero.</summary>
    private static int Scale(int step, int fraction)
    {
        int product = step * fraction;
        return (product + ((product >> 31) & 0xFF)) >> 8;
    }
}

/// <summary>Keeps the last drawn frame's fraction of a creature's turn, so each draw tests the interval since the one before
/// it. <c>0x0043CC30</c> passes the helper two fractions: this frame's (<c>[view+0x262D]</c>) and an earlier one (<c>[view+0x2625] + (+0x2621 - +0x2629) x 250</c>)
/// whose writers are unfound. Ours takes the last frame's fraction moved back by 256 per turn, reading the native as milliseconds into a 250 ms turn divided
/// by 256: a benign deviation, STRONGLY INFERRED.</summary>
public sealed class AnimationMarkWatcher
{
    private int _last = -1;

    /// <param name="ticksAdvanced">The clock's turns since the last draw.</param>
    /// <param name="fraction">The frame's fraction of the current turn, 0 to just under 1.</param>
    public int Observe(int mark1, int mark2, int previousPhase, int phase, int ticksAdvanced, float fraction)
    {
        int to = Math.Clamp((int)(fraction * 256f), 0, 256);
        int from = _last < 0 ? to : _last - (256 * ticksAdvanced);
        _last = to;
        return AnimationMarkCrossing.Test(mark1, mark2, previousPhase, phase, from, to);
    }

    /// <summary>Forgets the last frame, as a fresh install does.</summary>
    public void Reset() => _last = -1;
}
