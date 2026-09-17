// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Steering;

/// <summary>DK2's <c>Follow_Path</c> behaviour (<c>0x004D3C40</c>, weight 0.35;
/// docs/research/steering-behaviours.md §5). Stuck detection and re-routing, the advance and
/// arrival rules of the imp and creature arms, the turn that must finish before the body moves,
/// and the seek. The force is a desired velocity minus the current one.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class FollowPathRules
{
    public const int Weight = 0x5999;

    /// <summary>Five of fifteen ticks without progress: re-route and enter <c>escape_stuck_mode</c>.</summary>
    public const int RerouteFraction = 0x5555;

    /// <summary>Two of fifteen: leave <c>escape_stuck_mode</c>.</summary>
    public const int EscapeClearFraction = 0x2222;

    /// <summary>An imp steps to its next point half a tile out; a creature at 1.2 radii (0.24).</summary>
    public const int ImpAdvance = 0x8000;

    public const int CreatureAdvanceRadii = 0x13333;

    /// <summary>A creature also steps past a point inside an obstacle from 0.8 tile out.</summary>
    public const int OccupiedAdvance = 0xCCCC;

    /// <summary>Range of the obstacle probe around a waypoint (<c>0x004D2850</c>): 1.2 tiles.</summary>
    public const int ObstacleProbeRange = 0x13333;

    /// <summary>An imp has arrived within 0.1 tile; a creature also has to be at or under 0.38 speed.</summary>
    public const int ImpArrival = 0x1999;

    public const int ArrivalSpeed = 0x6147;

    /// <summary>The crowd probe's square, 0.95 tile a side, and the two seek gains its sum comes off.</summary>
    public const int CrowdSpan = 0xF333;

    public const int ImpGain = 0x1D332;

    public const int CreatureGain = 0x1051E;

    // Per thread, not per process: the original is single-threaded, the test host is not.
    [ThreadStatic] private static List<SteeringObstacle>? _scratch;

    private static List<SteeringObstacle> Scratch => _scratch ??= new List<SteeringObstacle>();

    /// <summary>How much of the route is behind the body, 16.16; 1.0 with no route left.</summary>
    public static int Progress(SteeringBody body)
    {
        if (body.PathFinished || body.PathTotalDist == 0) return Fixed16.One;
        int remaining = RouteWindow.Sixteenths((body.CurrentPoint - body.Pos).Octile())
            + body.PathSubpathDist + body.PathTailDist;
        return Fixed16.One - Fixed16.Div(remaining, body.PathTotalDist);
    }

    /// <summary>The share of the last fifteen ticks that made no progress, 16.16.</summary>
    public static int StuckFraction(ushort history)
    {
        int bits = 0;
        for (int mask = 1; mask < 1 << 15; mask <<= 1)
        {
            if ((history & mask) != 0) bits++;
        }

        return (bits << 16) / 15;
    }

    public static Fixed16Vector Compute(SteeringBody body, SteeringMover mover, ISteeringWorld world)
    {
        if (Progress(body) != Fixed16.One && StuckFraction(body.PathStuckBuffer) >= RerouteFraction)
        {
            ushort history = body.PathStuckBuffer;
            RouteWindow.Request(body, world, mover.Profile, body.PathTargetPos, body.PathFlags);
            body.PathStuckBuffer = history;
            body.EscapeStuckMode = true;
        }

        if (body.EscapeStuckMode)
        {
            int stuck = Progress(body) == Fixed16.One ? 0 : StuckFraction(body.PathStuckBuffer);
            if (stuck <= EscapeClearFraction) body.EscapeStuckMode = false;
        }

        if (body.PathTotalCount == 0 || body.PathFinished) return -body.Vel;
        return mover.IsWorker ? ImpArm(body, mover, world) : CreatureArm(body, mover, world);
    }

    /// <summary>One tick of the turn toward an aim (§5.3), from the body's position in the entity's
    /// 1/4096 units. The distance still to turn; the body may move only when it is zero.</summary>
    public static int Turn(SteeringBody body, SteeringMover mover, Fixed16Vector aim)
    {
        int target = Bearing.Of((aim.X >> 4) - (body.Pos.X >> 4), (aim.Y >> 4) - (body.Pos.Y >> 4));
        int heading = mover.Heading;
        int remaining = Bearing.TurnToward(ref heading, target, mover.TurnRate);
        mover.Heading = heading;
        return remaining;
    }

    /// <summary>Whether some <c>Obstacle</c> near the point has it inside its size (<c>0x004D27F0</c>).</summary>
    public static bool Occupied(Fixed16Vector point, ISteeringWorld world)
    {
        Scratch.Clear();
        world.CollectObstacles(point, ObstacleProbeRange, Scratch);
        foreach (SteeringObstacle o in Scratch)
        {
            if ((o.Pos - point).Octile() <= o.Size) return true;
        }

        return false;
    }

    private static Fixed16Vector ImpArm(SteeringBody body, SteeringMover mover, ISteeringWorld world)
    {
        Fixed16Vector aim;
        if (body.OnLastPoint)
        {
            aim = body.PathTargetPos;
            if ((aim - body.Pos).Octile() < ImpArrival) body.PathFinished = true;
        }
        else
        {
            if ((body.CurrentPoint - body.Pos).Octile() < ImpAdvance && RouteWindow.Advance(body))
                RouteWindow.Request(body, world, mover.Profile, body.PathTargetPos, body.PathFlags);
            aim = body.CurrentPoint;
        }

        if (Turn(body, mover, aim) != 0) return -body.Vel;
        return (aim - body.Pos).Scale(ImpGain - CrowdWeight(body, world)) - body.Vel;
    }

    private static Fixed16Vector CreatureArm(SteeringBody body, SteeringMover mover, ISteeringWorld world)
    {
        Fixed16Vector aim;
        if (body.OnLastPoint)
        {
            aim = body.PathTargetPos;
            int threshold = Fixed16.Mul(body.TightArrival ? 0x1999 : 0x8000, body.Radius);
            if ((aim - body.Pos).Octile() < threshold && body.Vel.Octile() <= ArrivalSpeed)
                body.PathFinished = true;
        }
        else
        {
            Fixed16Vector p = body.CurrentPoint;
            bool occupied = Occupied(p, world);
            int d = (p - body.Pos).Octile();
            bool advance = d < Fixed16.Mul(CreatureAdvanceRadii, body.Radius) || (occupied && d < OccupiedAdvance);
            if (advance && RouteWindow.Advance(body))
                RouteWindow.Request(body, world, mover.Profile, body.PathTargetPos, body.PathFlags);
            aim = body.CurrentPoint;
        }

        bool finalApproach = body.SubpathIndex >= body.PathTotalCount - 1 && !body.NoFinalApproach;
        if (Turn(body, mover, aim) != 0) return -body.Vel;

        if (!finalApproach) return (aim - body.Pos).Scale(CreatureGain - CrowdWeight(body, world)) - body.Vel;

        Fixed16Vector toAim = aim - body.Pos;
        int len = toAim.Length();
        if (len == 0) return Fixed16Vector.Zero;
        int speed = Fixed16.Div(len, 2 * Fixed16.One);
        if (body.MaxSpeed < speed) speed = body.MaxSpeed;
        return toAim.Scale(Fixed16.Div(speed, len)) - body.Vel;
    }

    private static int CrowdWeight(SteeringBody body, ISteeringWorld world)
    {
        int c = world.CrowdWeight(body.CurrentPoint, CrowdSpan);
        return c < Fixed16.One ? c : Fixed16.One;
    }
}
