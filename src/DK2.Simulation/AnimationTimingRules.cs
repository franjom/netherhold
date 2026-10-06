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
        SlotOrFallback(definition.KnockdownAnimation, definition.KnockdownFallbackAnimation));

    public static int GetUpHoldTicks(CreatureDef definition) => ActionHoldTicks(
        SlotOrFallback(definition.GetUpAnimation, definition.GetUpFallbackAnimation));

    /// <summary>The victory pause (action 55, art slot 35 or its fallback 8): one play of the clip,
    /// 1 to 22 turns by creature (combat-approach-and-victory.md §7, Q376).</summary>
    public static int VictoryHoldTicks(CreatureDef definition) => ActionHoldTicks(
        SlotOrFallback(definition.ReaperSummonAnimation, definition.ReaperSummonFallbackAnimation));

    /// <summary>An art slot falls back when its runtime bit 0 is clear (<c>0x00494880</c>); the loader
    /// <c>0x00504D80</c> copies the file flags only for types 1 to 8 and sets the bit for any non-zero
    /// type, so a slot is present exactly when its type is non-zero.</summary>
    public static CreatureAnimationTiming SlotOrFallback(CreatureAnimationTiming slot, CreatureAnimationTiming fallback) =>
        slot.ResourceType != 0 ? slot : fallback;

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
