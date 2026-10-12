// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Pathfinding;

namespace DK2.Simulation.Steering;

/// <summary>The combiner and step of <c>FUN_004D4760</c> (docs/research/navigation-and-steering.md
/// §3.3–§3.4; steering-behaviours.md §4, §5.1): four forces each clamped to <c>max_force</c> and
/// weighted; the slide along recorded solid obstacles; the sum clamped again and divided by a
/// mass of one into the velocity; the velocity clamped to <c>max_speed</c>; the arrival snap;
/// the wall clamp on the new position with its three fallbacks; the local axes; the stuck bits.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class SteeringIntegrator
{
    public const int Mass = Fixed16.One;

    /// <summary>A recorded obstacle is ignored once the tentative motion moves away from it by 0.05.</summary>
    public const int SlideThreshold = 0xCCC;

    /// <summary>The weighted sum of the four live behaviours, each clamped first (§3.3).</summary>
    public static Fixed16Vector Combine(SteeringBody body, Fixed16Vector avoidWalls, Fixed16Vector repelObstacle, Fixed16Vector followPath, Fixed16Vector separation) =>
        Fixed16.Truncate(avoidWalls, body.MaxForce).Scale(AvoidWallsRules.Weight)
        + Fixed16.Truncate(repelObstacle, body.MaxForce).Scale(RepelObstacleRules.Weight)
        + Fixed16.Truncate(followPath, body.MaxForce).Scale(FollowPathRules.Weight)
        + Fixed16.Truncate(separation, body.MaxForce).Scale(SeparationBehaviour.Weight);

    /// <summary>One tick of motion from a combined force.</summary>
    public static void Step(SteeringBody body, Fixed16Vector total, MoverProfile mover, ISteeringWorld world)
    {
        total = Slide(body, total);
        Fixed16Vector clamped = Fixed16.Truncate(total, body.MaxForce);
        body.Vel = Fixed16.Truncate(body.Vel + clamped.Over(Mass), body.MaxSpeed);

        Fixed16Vector next = body.Pos + body.Vel;
        if (body.SnapsOnArrival && body.OnLastPoint && (body.CurrentPoint - body.Pos).Octile() < body.Vel.Octile())
        {
            next = body.CurrentPoint;
            body.Vel = Fixed16Vector.Zero;
        }

        Fixed16Vector axis = clamped;
        if (!Clamp(body, next, mover, world, ref axis))
            body.PathStuckBuffer = (ushort)((body.PathStuckBuffer << 1) | 1);

        body.LocalXAxis = Fixed16.Normalise(axis);
        body.LocalYAxis = body.LocalXAxis.Perpendicular();
        PushHistory(body);
    }

    /// <summary>The slide (steering-behaviours.md §4): for each recorded solid obstacle the tentative
    /// motion is still moving into, the force and the velocity lose their inward component (x
    /// first, and y from the updated x) and a tangential push at <c>max_force</c> is gathered,
    /// on the side the projected motion leans to. Returns the force with the pushes added.</summary>
    public static Fixed16Vector Slide(SteeringBody body, Fixed16Vector total)
    {
        Fixed16Vector pushes = Fixed16Vector.Zero;
        for (int i = body.SolidContacts.Count - 1; i >= 0; i--)
        {
            Fixed16Vector n = body.SolidContacts[i].Normal;
            Fixed16Vector v = body.Vel;
            int inward = Fixed16.Mul(total.X + v.X, n.X) + Fixed16.Mul(total.Y + v.Y, n.Y);
            if (inward >= SlideThreshold) continue;

            int keepX = Fixed16.One - Fixed16.Mul(n.X, n.X);
            int keepY = Fixed16.One - Fixed16.Mul(n.Y, n.Y);
            int cross = Fixed16.Mul(n.X, n.Y);
            int fx = Fixed16.Mul(keepX, total.X) - Fixed16.Mul(total.Y, cross);
            int fy = Fixed16.Mul(keepY, total.Y) - Fixed16.Mul(fx, cross);
            total = new Fixed16Vector(fx, fy);
            int vx = Fixed16.Mul(keepX, v.X) - Fixed16.Mul(v.Y, cross);
            int vy = Fixed16.Mul(keepY, v.Y) - Fixed16.Mul(vx, cross);
            body.Vel = new Fixed16Vector(vx, vy);

            var tangent = new Fixed16Vector(-n.Y, n.X);
            int side = Fixed16.Mul(fx + vx, tangent.X) + Fixed16.Mul(fy + vy, tangent.Y);
            if (side < 0) tangent = -tangent;
            pushes += tangent.Scale(body.MaxForce);
        }

        return total + pushes;
    }

    /// <summary>The wall clamp: the new position stands if its tile is passable from the old one;
    /// else the x move alone (y velocity dropped), else the y move alone (x velocity dropped), else
    /// the step turned a quarter left, else a quarter right; else the body stays. False when it
    /// stayed. The turned step becomes the local axes' source.</summary>
    private static bool Clamp(SteeringBody body, Fixed16Vector next, MoverProfile mover, ISteeringWorld world, ref Fixed16Vector axis)
    {
        (int ox, int oy) = body.Pos.Tile();
        (int nx, int ny) = next.Tile();
        byte own = world.NavByte(ox, oy);

        if (Open(own, new TilePos(nx, ny), body.Pos, next, mover, world))
        {
            body.Pos = next;
            return true;
        }

        if (Open(own, new TilePos(nx, oy), body.Pos, new Fixed16Vector(next.X, body.Pos.Y), mover, world))
        {
            body.Pos = new Fixed16Vector(next.X, body.Pos.Y);
            body.Vel = new Fixed16Vector(body.Vel.X, 0);
            return true;
        }

        if (Open(own, new TilePos(ox, ny), body.Pos, new Fixed16Vector(body.Pos.X, next.Y), mover, world))
        {
            body.Pos = new Fixed16Vector(body.Pos.X, next.Y);
            body.Vel = new Fixed16Vector(0, body.Vel.Y);
            return true;
        }

        int dx = nx - ox, dy = ny - oy;
        Fixed16Vector step = next - body.Pos;
        if (Open(own, new TilePos(ox - dy, oy + dx), body.Pos, new Fixed16Vector(body.Pos.X - step.Y, body.Pos.Y + step.X), mover, world))
        {
            body.Pos = new Fixed16Vector(body.Pos.X - step.Y, body.Pos.Y + step.X);
            axis = step;
            return true;
        }

        if (Open(own, new TilePos(ox + dy, oy - dx), body.Pos, new Fixed16Vector(body.Pos.X + step.Y, body.Pos.Y - step.X), mover, world))
        {
            body.Pos = new Fixed16Vector(body.Pos.X + step.Y, body.Pos.Y - step.X);
            axis = step;
            return true;
        }

        return false;
    }

    private static bool Open(byte own, TilePos tile, Fixed16Vector from, Fixed16Vector to, MoverProfile mover, ISteeringWorld world) =>
        world.CanMove(from, to) && MeshRouter.TransitionCost(world.NavByte(tile.X, tile.Y), own, mover) != 0;

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
