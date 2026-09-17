// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Steering;

/// <summary>The mover state the original keeps per creature at <c>entity+0x124</c>, under the
/// debug dumper's own names (docs/research/debug-dumpers.md). Positions and velocities are 16.16
/// tiles; the route window's distances are sixteenths of a tile. The three scalars are the
/// literals every creature shares (navigation-and-steering.md §3.4).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-and-steering.md")]
public sealed class SteeringBody
{
    public const int WindowSize = 5;
    public const int SolidContactSlots = 12;

    public Fixed16Vector Pos { get; set; }

    public Fixed16Vector Vel { get; set; }

    public Fixed16Vector LocalXAxis { get; set; }

    public Fixed16Vector LocalYAxis { get; set; }

    public Fixed16Vector PathTargetPos { get; set; }

    /// <summary>The route window: up to five points, 16.16 (<c>+0xB4</c>).</summary>
    public Fixed16Vector[] Subpath { get; } = new Fixed16Vector[WindowSize];

    /// <summary>Distance from window point k to k+1, sixteenths (<c>+0xE6</c>; our name).</summary>
    public int[] Segment { get; } = new int[WindowSize];

    public int Radius { get; set; } = SeparationRules.ShippedRadius;

    public int MaxForce { get; set; } = SeparationRules.ShippedMaxForce;

    public int MaxSpeed { get; set; } = SeparationRules.ShippedMaxSpeed;

    public int PathTotalDist { get; set; }

    public int PathSubpathDist { get; set; }

    public int PathTailDist { get; set; }

    public int PathLastProgress { get; set; }

    /// <summary>One bit a tick, newest lowest; a set bit is a tick without progress.</summary>
    public ushort PathStuckBuffer { get; set; }

    public byte PathTotalCount { get; set; }

    public byte PathFlags { get; set; }

    public int PathBufferCount { get; set; }

    public int SubpathIndex { get; set; }

    public bool PathFinished { get; set; }

    public bool IsBlocker { get; set; }

    public bool IgnoreBlocker { get; set; }

    public bool EscapeStuckMode { get; set; }

    /// <summary>Solid obstacles <c>Repel_Obstacle</c> recorded this tick for the integrator's slide.</summary>
    public List<SolidContact> SolidContacts { get; } = new(SolidContactSlots);

    public Fixed16Vector CurrentPoint => Subpath[SubpathIndex];

    public bool OnLastPoint => SubpathIndex == PathTotalCount - 1;

    /// <summary><c>path_flags</c> bit 0 (our name): arrival within a tenth of the radius, not half.</summary>
    public bool TightArrival => (PathFlags & 1) != 0;

    /// <summary><c>path_flags</c> bit 1 (our name): the last point is sought like any other.</summary>
    public bool NoFinalApproach => (PathFlags & 2) != 0;

    /// <summary><c>path_flags</c> bit 2 (our name): repulsion stops near the last point and the
    /// integrator snaps onto it.</summary>
    public bool SnapsOnArrival => (PathFlags & 4) != 0;
}

/// <summary>A recorded solid obstacle: the unit direction from it to the body and its flag bit 1,
/// the ten-byte record of steering-behaviours.md §2.</summary>
public readonly record struct SolidContact(Fixed16Vector Normal, bool Flag2);
