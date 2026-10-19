// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The animation flags a creature's action clock keeps (DK2's animation object +0xAD,
/// creature +0xF1).</summary>
[Flags]
public enum ActionAnimationFlags : byte
{
    None = 0,
    Loop = 1,
    Complete = 2,
    /// <summary>Set by a Main wrap; only an install clears it, so after the first wrap every
    /// action's animation counts as done (<c>0x00496AA0</c>).</summary>
    Wrapped = 4,
    /// <summary>The handler's stop request: Main does not wrap again but runs into End.</summary>
    Stop = 8,
    InStart = 16,
    InEnd = 32,
}

/// <summary>The animation clock behind a mode-3 action's end, kept as creature state rather than
/// reconstructed: phase and rate in 1/4096 of a segment, advanced once a turn (animation-clock.md).
/// Install is <c>0x004B37D0</c> with <c>0x004B41E0</c>'s rate, the advance <c>0x004B3C10</c>, and
/// <see cref="Begin"/> the keep-or-install test of <c>0x00496700</c>. The queued resource, the
/// alternate rate and the walk scalar are not modelled: no work action uses them.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/animation-clock.md")]
public static class ActionAnimationClock
{
    private const int One = 4096;

    /// <summary>An action that plays <paramref name="slot"/> begins: the running clip is kept when it
    /// is the same slot and has not finished a non-looping play, otherwise installed fresh.</summary>
    public static void Begin(CreatureState creature, CreatureDef definition, byte slot)
    {
        ActionAnimationFlags flags = creature.ActionAnimationFlags;
        bool finished = (flags & (ActionAnimationFlags.Complete | ActionAnimationFlags.Wrapped)) != 0
            && (flags & ActionAnimationFlags.Loop) == 0;
        if (creature.ActionArtSlot == slot && !finished) return;
        Install(creature, definition.ArtSlot(slot), slot);
    }

    /// <summary>Whether <paramref name="slot"/> has the data the clock needs.</summary>
    public static bool CanPlay(CreatureDef definition, int slot)
    {
        CreatureAnimationTiming art = definition.ArtSlot(slot);
        return art.ResourceType != 0 && art.FrameCount != 0 && art.FramesPerSecond != 0;
    }

    /// <summary>No action plays: the chooser puts the standing or walking clip on, which clears
    /// every action flag (<c>0x0048BE50</c>).</summary>
    public static void Clear(CreatureState creature)
    {
        creature.ActionArtSlot = 0;
        creature.ActionAnimationPhase = 0;
        creature.ActionAnimationRate = 0;
        creature.ActionAnimationFlags = ActionAnimationFlags.None;
        creature.ActionHandlerDone = false;
    }

    /// <summary>The animation half of the runner's completion test (<c>0x00496AA0</c>).</summary>
    public static bool AnimationDone(CreatureState creature) =>
        creature.ActionAnimationRate == 0
        || (creature.ActionAnimationFlags & (ActionAnimationFlags.Complete | ActionAnimationFlags.Wrapped)) != 0;

    /// <summary>One turn of <c>0x004B3C10</c>.</summary>
    public static void Advance(CreatureState creature, CreatureDef definition)
    {
        if (creature.ActionArtSlot == 0) return;
        ActionAnimationFlags flags = creature.ActionAnimationFlags;
        if ((flags & ActionAnimationFlags.Complete) != 0)
        {
            creature.ActionAnimationPhase = One;
            creature.ActionAnimationRate = 0;
            return;
        }
        if (creature.ActionAnimationRate == 0) return;

        CreatureAnimationTiming art = definition.ArtSlot(creature.ActionArtSlot);
        int phase = creature.ActionAnimationPhase + creature.ActionAnimationRate;
        if (phase < One)
        {
            creature.ActionAnimationPhase = phase;
            return;
        }

        int main = AnimationTimingRules.Duration(art.FrameCount, art.FramesPerSecond);
        bool hasEnd = (art.Flags & CreatureAnimationFlags.EndSegment) != 0 && art.EndSegmentFrames != 0;
        int end = hasEnd ? AnimationTimingRules.Duration(art.EndSegmentFrames, art.FramesPerSecond) : 0;

        if ((flags & ActionAnimationFlags.InEnd) != 0)
        {
            Finish(creature, flags);
            return;
        }

        if ((flags & ActionAnimationFlags.InStart) != 0)
        {
            int start = AnimationTimingRules.Duration(art.StartSegmentFrames, art.FramesPerSecond);
            flags &= ~ActionAnimationFlags.InStart;
            phase = AnimationTimingRules.Carry(phase - One, start, main);
            creature.ActionAnimationRate = AnimationTimingRules.AdvanceRate(main);
            if (phase > One && hasEnd)
            {
                flags |= ActionAnimationFlags.InEnd;
                phase = AnimationTimingRules.Carry(phase - One, main, end);
                creature.ActionAnimationRate = AnimationTimingRules.AdvanceRate(end);
            }
            creature.ActionAnimationPhase = Math.Min(phase, One);
            creature.ActionAnimationFlags = flags;
            return;
        }

        if ((flags & ActionAnimationFlags.Loop) != 0 && (flags & ActionAnimationFlags.Stop) == 0)
        {
            creature.ActionAnimationPhase = phase - One;
            creature.ActionAnimationFlags = flags | ActionAnimationFlags.Wrapped;
            return;
        }

        if (hasEnd)
        {
            creature.ActionAnimationPhase = Math.Min(One, AnimationTimingRules.Carry(phase - One, main, end));
            creature.ActionAnimationRate = AnimationTimingRules.AdvanceRate(end);
            creature.ActionAnimationFlags = flags | ActionAnimationFlags.InEnd;
            return;
        }

        Finish(creature, flags);
    }

    private static void Finish(CreatureState creature, ActionAnimationFlags flags)
    {
        creature.ActionAnimationPhase = One;
        creature.ActionAnimationFlags = flags | ActionAnimationFlags.Complete;
    }

    private static void Install(CreatureState creature, CreatureAnimationTiming art, byte slot)
    {
        bool hasStart = (art.Flags & CreatureAnimationFlags.StartSegment) != 0 && art.StartSegmentFrames != 0;
        int first = hasStart
            ? AnimationTimingRules.Duration(art.StartSegmentFrames, art.FramesPerSecond)
            : AnimationTimingRules.Duration(art.FrameCount, art.FramesPerSecond);
        creature.ActionArtSlot = slot;
        creature.ActionAnimationPhase = 0;
        creature.ActionAnimationRate = AnimationTimingRules.AdvanceRate(first);
        creature.ActionAnimationFlags = ActionAnimationFlags.Loop
            | (hasStart ? ActionAnimationFlags.InStart : ActionAnimationFlags.None);
    }
}
