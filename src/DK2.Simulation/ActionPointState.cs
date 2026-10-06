// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One action point as DK2 constructs it from a placed THING. The rectangle, ids
/// and flags are level-authored data; the passage count and wait timer are
/// runtime fields and stay mutable even though their consumers are not yet
/// identified. There is deliberately no <c>Available</c> field — the four trigger
/// actions once thought to write one use their byte as input to separate
/// operations and never write the action-point object (ADR-0103).
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/action-points.md")]
public sealed class ActionPointState
{
    /// <summary>The five source flag bits DK2's loader copies: 0, 2, 3, 4 and 5.</summary>
    public const byte LoadedFlagMask = 0x3D;

    public byte Id { get; init; }
    public ushort StartX { get; init; }
    public ushort StartY { get; init; }
    public ushort EndX { get; init; }
    public ushort EndY { get; init; }
    public ushort TriggerId { get; init; }
    public byte NextWaypointId { get; init; }
    public byte Flags { get; init; }

    /// <summary>Runtime counter initialised to zero. Its modifying path remains Q236.</summary>
    public ushort PassageCount { get; set; }

    /// <summary>
    /// The authored wait delay multiplied by DK2's four-Hz tick rate at load.
    /// <para>Scales by the <b>original</b> rate while the trigger threshold beside
    /// it scales by ours; the two are equal today. Both reproduce the same loader
    /// idiom — one multiply by variable 19 at load — but this is a stored field
    /// mirroring the original's saved block, so it keeps the original's number
    /// whatever our step rate becomes, while
    /// <see cref="TriggerState.HasElapsed"/> builds a deadline against <i>our</i>
    /// clock and must scale by <see cref="SimulationTiming.TicksPerSecond"/>. If
    /// <c>TicksPerSecond</c> ever stops being 4, do not make one match the other:
    /// whatever decrements this counter has to say which clock it counts in.</para>
    /// </summary>
    public ushort WaitTimerTicks { get; set; }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(StartX);
        hash.Add(StartY);
        hash.Add(EndX);
        hash.Add(EndY);
        hash.Add(TriggerId);
        hash.Add(NextWaypointId);
        hash.Add(Flags);
        hash.Add(PassageCount);
        hash.Add(WaitTimerTicks);
    }
}
