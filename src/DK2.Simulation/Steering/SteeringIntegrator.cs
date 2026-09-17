// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Steering;

/// <summary>The combiner and Euler step of <c>FUN_004D4760</c> (docs/research/navigation-and-steering.md
/// §3.3 and §3.4; steering-behaviours.md §5.1): four forces each clamped to <c>max_force</c> and
/// weighted, the sum clamped again, divided by a mass of one into the velocity, the velocity
/// clamped to <c>max_speed</c> and added to the position, then the arrival snap, the local axes
/// and the stuck history. <b>Not yet here:</b> the slide along recorded solid obstacles and the
/// wall clamp on the new position, both still being read (Q349).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-and-steering.md")]
public static class SteeringIntegrator
{
    public const int Mass = Fixed16.One;

    /// <summary>The weighted sum of the four live behaviours, each clamped first (§3.3). Order
    /// does not matter: every term is clamped before it is added.</summary>
    public static Fixed16Vector Combine(SteeringBody body, Fixed16Vector avoidWalls, Fixed16Vector repelObstacle, Fixed16Vector followPath, Fixed16Vector separation) =>
        Fixed16.Truncate(avoidWalls, body.MaxForce).Scale(AvoidWallsRules.Weight)
        + Fixed16.Truncate(repelObstacle, body.MaxForce).Scale(RepelObstacleRules.Weight)
        + Fixed16.Truncate(followPath, body.MaxForce).Scale(FollowPathRules.Weight)
        + Fixed16.Truncate(separation, body.MaxForce).Scale(SeparationBehaviour.Weight);

    /// <summary>One tick of motion from a combined force (§3.4), then the bookkeeping of §5.1.</summary>
    public static void Step(SteeringBody body, Fixed16Vector total)
    {
        Fixed16Vector accel = Fixed16.Truncate(total, body.MaxForce).Over(Mass);
        body.Vel = Fixed16.Truncate(body.Vel + accel, body.MaxSpeed);

        Fixed16Vector before = body.Pos;
        if (body.SnapsOnArrival && body.OnLastPoint && (body.CurrentPoint - body.Pos).Octile() <= body.Vel.Octile())
        {
            body.Pos = body.CurrentPoint;
            body.Vel = Fixed16Vector.Zero;
        }
        else
        {
            body.Pos += body.Vel;
        }

        Fixed16Vector moved = body.Pos - before;
        body.LocalXAxis = Fixed16.Normalise(moved);
        body.LocalYAxis = body.LocalXAxis.Perpendicular();

        PushHistory(body);
    }

    /// <summary>The tick's stuck bit: shift the history and set bit 0 when progress along the route
    /// did not grow; remember the progress.</summary>
    public static void PushHistory(SteeringBody body)
    {
        int progress = FollowPathRules.Progress(body);
        var history = (ushort)(body.PathStuckBuffer << 1);
        if (progress <= body.PathLastProgress) history |= 1;
        body.PathStuckBuffer = history;
        body.PathLastProgress = progress;
    }
}
