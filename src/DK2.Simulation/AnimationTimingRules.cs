// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-tremor.md")]
public static class AnimationTimingRules
{
    private const int One = 4096;

    public static int KnockdownHoldTicks(CreatureDef definition) => ActionHoldTicks(
        definition.KnockdownAnimation.ResourceType != 0 ? definition.KnockdownAnimation : definition.KnockdownFallbackAnimation);

    public static int GetUpHoldTicks(CreatureDef definition) => ActionHoldTicks(
        definition.GetUpAnimation.ResourceType != 0 ? definition.GetUpAnimation : definition.GetUpFallbackAnimation);

    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-reaper.md")]
    public static int FirstCycleHoldTicks(CreatureAnimationTiming animation)
    {
        if (animation.ResourceType != 6) return 1;
        int mainDuration = Duration(animation.FrameCount, animation.FramesPerSecond);
        bool hasStart = (animation.Flags & CreatureAnimationFlags.StartSegment) != 0;
        bool hasEnd = (animation.Flags & CreatureAnimationFlags.EndSegment) != 0;
        int startDuration = hasStart ? Duration(animation.StartSegmentFrames, animation.FramesPerSecond) : 0;
        int endDuration = hasEnd ? Duration(animation.EndSegmentFrames, animation.FramesPerSecond) : 0;
        Segment segment = hasStart ? Segment.Start : Segment.Main;
        int duration = hasStart ? startDuration : mainDuration;
        int phase = 0;
        int ticks = 0;
        while (true)
        {
            ticks++;
            int step = AdvanceRate(duration);
            if (step == 0) return ticks;
            phase += step;
            if (phase < One) continue;
            if (segment == Segment.Start)
            {
                phase = Carry(phase - One, duration, mainDuration);
                duration = mainDuration;
                segment = Segment.Main;
                if (AdvanceRate(duration) == 0) return ticks;
                if (phase <= One) continue;
                if (!hasEnd)
                {
                    phase = One;
                    continue;
                }
                phase = Math.Min(One, Carry(phase - One, duration, endDuration));
                duration = endDuration;
                segment = Segment.End;
                if (AdvanceRate(duration) == 0) return ticks;
                continue;
            }
            return ticks;
        }
    }

    public static int ActionHoldTicks(CreatureAnimationTiming animation)
    {
        if (animation.ResourceType != 6) return 1;
        int mainDuration = Duration(animation.FrameCount, animation.FramesPerSecond);
        bool hasStart = (animation.Flags & CreatureAnimationFlags.StartSegment) != 0;
        bool hasEnd = (animation.Flags & CreatureAnimationFlags.EndSegment) != 0;
        int startDuration = hasStart ? Duration(animation.StartSegmentFrames, animation.FramesPerSecond) : 0;
        int endDuration = hasEnd ? Duration(animation.EndSegmentFrames, animation.FramesPerSecond) : 0;
        Segment segment = hasStart ? Segment.Start : Segment.Main;
        int duration = hasStart ? startDuration : mainDuration;
        int phase = 0;
        int ticks = 0;
        bool complete = false;
        while (true)
        {
            ticks++;
            if (complete) return ticks;
            int step = AdvanceRate(duration);
            if (step == 0) return ticks;
            phase += step;
            if (phase < One) continue;
            if (segment == Segment.Start)
            {
                phase = Carry(phase - One, duration, mainDuration);
                duration = mainDuration;
                segment = Segment.Main;
                if (AdvanceRate(duration) == 0) return ticks;
                if (phase <= One) continue;
                if (!hasEnd)
                {
                    phase = One;
                    continue;
                }
            }
            if (segment == Segment.Main && hasEnd)
            {
                phase = Math.Min(One, Carry(phase - One, duration, endDuration));
                duration = endDuration;
                segment = Segment.End;
                if (AdvanceRate(duration) == 0) return ticks;
                continue;
            }
            complete = true;
        }
    }

    private static int Duration(int frames, int framesPerSecond) =>
        checked((int)(((long)frames * One) / framesPerSecond));

    private static int AdvanceRate(int duration) =>
        Math.Min(One - 1, Divide(One, checked(duration * SimulationTiming.OriginalTicksPerGameSecond)));

    private static int Carry(int overshoot, int previousDuration, int nextDuration) =>
        Divide(overshoot, Divide(nextDuration, previousDuration));

    private static int Divide(int numerator, int denominator) =>
        checked((int)(((long)numerator * One) / denominator));

    private enum Segment { Start, Main, End }
}
