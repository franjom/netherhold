// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The clock of the dollar a creature in want of pay shows in place of its flower: one shared overlay
/// animation, 30 frames at 30 a second, looping. 0x005961A0 advances it after each dollar drawn, and every non-dollar
/// flower drawn resets it, so its speed follows the number of dollars on screen and the frame rate (godot step 2
/// audit, open points); this keeps that as it was drawn.</summary>
public sealed class FlowerDollarClock
{
    public static readonly CreatureAnimationTiming Timing = new() { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 };

    /// <summary>The mesh's turn about the vertical, in degrees.</summary>
    public const float YawDegrees = 180f;

    private RenderAnimationClock? _clock;
    private bool _running;

    /// <summary>A non-dollar flower was drawn: the next dollar starts the clip again.</summary>
    public void Reset() => _running = false;

    /// <summary>The clip phase (0..1) to draw this dollar at, between the last advance and the next.</summary>
    public float Phase(float fraction)
    {
        if (!_running)
        {
            // A restarted clock is a new one: Replace installs the timing at phase 0, as the constructor does.
            if (_clock == null)
            {
                _clock = new RenderAnimationClock(Timing, loop: true);
            }
            else
            {
                _clock.Replace(Timing, loop: true, AnimationReplacement.Immediate);
            }

            _running = true;
        }
        return _clock!.Sample(fraction).Phase;
    }

    /// <summary>Called after each dollar drawn.</summary>
    public void Advance()
    {
        if (_running)
        {
            _clock!.Advance();
        }
    }

    /// <summary>The fraction a dollar is drawn at: the wall clock's quarter seconds, <c>(seconds·4) mod 1</c>.</summary>
    public static float Fraction(double unscaledSeconds) => (float)(unscaledSeconds * 4 % 1);
}
