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

    /// <summary><c>CreatureDef_HasArt</c> (<c>0x00494A00</c>): the slot's own record is present, with
    /// no fallback.</summary>
    public static bool HasArt(CreatureDef definition, int slot) => definition.ArtSlot(slot).ResourceType != 0;

    /// <summary><c>0x00494880</c>: a missing slot's fallback, unchecked (casino.md §7). Slots 37 to 41
    /// fall back to 1; the rest by the binary's literal switch, 0 for a slot it does not list.</summary>
    public static int ResolveArtSlot(CreatureDef definition, int slot)
    {
        if (HasArt(definition, slot)) return slot;
        return slot switch
        {
            >= 37 and <= 41 => 1,
            2 or 4 or 10 or 20 or 21 or 27 or 29 or 32 or 33 or 34 => 1,
            3 => 10,
            6 or 9 or 14 or 15 or 22 => 5,
            13 or 23 or 28 or 35 => 8,
            16 or 17 or 18 or 19 or 25 or 26 or 30 => 9,
            24 => 4,
            31 or 36 => 20,
            _ => 0,
        };
    }

    /// <summary><c>0x00494860</c>'s record tested for bit 0, as states 146, 152 and 153 ask: whether
    /// the slot, or its fallback when it is missing, is present.</summary>
    public static bool ResolvesPresent(CreatureDef definition, int slot) =>
        HasArt(definition, ResolveArtSlot(definition, slot));

    /// <summary>How long an action started through <c>0x004969D0</c> with <paramref name="count"/>
    /// holds, playing its art slot resolved: count 0 or 1 plays once (<see cref="ActionHoldTicks"/>),
    /// a count of 2 or more loops <paramref name="count"/> + 1 times at the clip's frames and rate,
    /// rounded up, as the prison's flourish does (ADR-0301). LIKELY: the runner's end after the
    /// cycles is the renderer's flags, which are not read.</summary>
    public static int CountedHoldTicks(CreatureDef definition, int slot, int count)
    {
        CreatureAnimationTiming animation = definition.ArtSlot(ResolveArtSlot(definition, slot));
        if (count < 2) return ActionHoldTicks(animation);
        if (animation.ResourceType != 6 || animation.FramesPerSecond == 0) return 1;
        long frames = (long)(count + 1) * animation.FrameCount * SimulationTiming.OriginalTicksPerGameSecond;
        return (int)((frames + animation.FramesPerSecond - 1) / animation.FramesPerSecond);
    }

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

    internal static int Duration(int frames, int framesPerSecond) =>
        checked((int)(((long)frames * One) / framesPerSecond));

    internal static int AdvanceRate(int duration) =>
        Math.Min(One - 1, Divide(One, checked(duration * SimulationTiming.OriginalTicksPerGameSecond)));

    internal static int Carry(int overshoot, int previousDuration, int nextDuration) =>
        Divide(overshoot, Divide(nextDuration, previousDuration));

    private static int Divide(int numerator, int denominator) =>
        checked((int)(((long)numerator * One) / denominator));

    private enum Segment { Start, Main, End }
}
