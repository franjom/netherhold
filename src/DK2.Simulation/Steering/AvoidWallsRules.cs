// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Pathfinding;

namespace DK2.Simulation.Steering;

/// <summary>DK2's <c>Avoid_Walls</c> behaviour (<c>0x004D35C0</c>, weight 0.6;
/// docs/research/steering-behaviours.md §3 and §6): a one-radius margin against the position
/// one tick ahead, judged per axis from three tile probes in the heading's quadrant, a reflection
/// off an inside corner, and in <c>escape_stuck_mode</c> a two-raw-unit sideways nudge.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class AvoidWallsRules
{
    public const int Weight = 0x9999;

    /// <summary>The stuck nudge as shipped: the tile step's sign doubled, added to a 16.16
    /// accumulator without a shift. Two 65536ths of a tile, a tie-break rather than a push.</summary>
    public const int StuckNudge = 2;

    public static Fixed16Vector Compute(SteeringBody body, MoverProfile mover, ISteeringWorld world)
    {
        Fixed16Vector ahead = body.Pos + body.Vel;
        (int tx, int ty) = body.Pos.Tile();
        byte own = world.NavByte(tx, ty);

        Fixed16Vector toAhead = ahead - body.Pos;
        int len = toAhead.Length();
        Fixed16Vector dir = len == 0 ? toAhead : toAhead.Over(len);
        int sx = dir.X < 0 ? -1 : 1;
        int sy = dir.Y < 0 ? -1 : 1;

        bool sideY = Open(world, own, tx, ty + sy, mover);
        bool diagonal = Open(world, own, tx + sx, ty + sy, mover);
        bool sideX = Open(world, own, tx + sx, ty, mover);

        Fixed16Vector force;
        if (!diagonal && sideY && sideX)
        {
            force = CornerReflection(body, ahead, dir, len, tx + (sx > 0 ? 1 : 0), ty + (sy > 0 ? 1 : 0));
        }
        else
        {
            int fx = sideX ? 0 : AxisMargin(ahead.X, tx, sx, body.Radius);
            int fy = sideY ? 0 : AxisMargin(ahead.Y, ty, sy, body.Radius);
            force = new Fixed16Vector(fx, fy);
        }

        return body.EscapeStuckMode ? force + Nudge(body, mover, world, own, tx, ty) : force;
    }

    /// <summary>Twice the overshoot past the wall face less one radius, on one axis; zero when the
    /// predicted position is still inside the margin.</summary>
    public static int AxisMargin(int ahead, int tile, int sign, int radius)
    {
        if (sign > 0)
        {
            int limit = ((tile + 1) << 16) - radius;
            return ahead > limit ? (limit - ahead) << 1 : 0;
        }

        int inner = (tile << 16) + radius;
        return ahead < inner ? (inner - ahead) << 1 : 0;
    }

    private static Fixed16Vector CornerReflection(SteeringBody body, Fixed16Vector ahead, Fixed16Vector dir, int len, int cornerX, int cornerY)
    {
        var corner = new Fixed16Vector(cornerX << 16, cornerY << 16);
        int t = RayCircle.Distance(body.Pos, dir, corner, body.Radius);
        if (t == RayCircle.Miss || t <= 0 || t > len) return Fixed16Vector.Zero;

        Fixed16Vector hit = body.Pos + dir.Scale(t);
        Fixed16Vector normal = (hit - corner).Over(body.Radius);
        int dot = (hit - ahead).Dot(normal);
        return normal.Scale(dot << 1) - body.Vel;
    }

    private static Fixed16Vector Nudge(SteeringBody body, MoverProfile mover, ISteeringWorld world, byte own, int tx, int ty)
    {
        Fixed16Vector v = (body.CurrentPoint - body.Pos) + body.Vel;
        int sx = 0, sy = 0;
        if (Fixed16.Abs(v.X) > Fixed16.Abs(v.Y)) sx = v.X < 0 ? -1 : 1;
        else sy = v.Y < 0 ? -1 : 1;

        int px = sy, py = -sx;
        bool pSide = Open(world, own, tx + px, ty + py, mover) && Open(world, own, tx + sx + px, ty + sy + py, mover);
        bool qSide = Open(world, own, tx - px, ty - py, mover) && Open(world, own, tx + sx - px, ty + sy - py, mover);
        bool towardP = pSide || !qSide;
        return towardP
            ? new Fixed16Vector(StuckNudge * px, StuckNudge * py)
            : new Fixed16Vector(-StuckNudge * px, -StuckNudge * py);
    }

    /// <summary>Whether this mover may step from its own tile onto another: the router's transition
    /// cost, nonzero.</summary>
    private static bool Open(ISteeringWorld world, byte own, int tileX, int tileY, MoverProfile mover) =>
        MeshRouter.TransitionCost(world.NavByte(tileX, tileY), own, mover) != 0;
}

/// <summary>The original's ray–circle test (<c>0x004D20F0</c>, steering-behaviours.md §3), operation
/// for operation. It is a quadratic in 16.16 with two departures from the textbook that are kept:
/// the discriminant is <c>4·(b² − a·c)</c> with <c>b</c> already doubled, and the root is the
/// integer square root of the 16.16 discriminant taken as it comes. So a head-on approach reports
/// a negative distance and the caller's corner reflection fires only in a narrow band.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class RayCircle
{
    /// <summary>Reported when the discriminant is not positive.</summary>
    public const int Miss = int.MinValue;

    public static int Distance(Fixed16Vector origin, Fixed16Vector dir, Fixed16Vector centre, int radius)
    {
        Fixed16Vector m = origin - centre;
        int a = Fixed16.Mul(dir.X, dir.X) + Fixed16.Mul(dir.Y, dir.Y);
        int b = (Fixed16.Mul(dir.X, m.X) + Fixed16.Mul(dir.Y, m.Y)) << 1;
        int c = Fixed16.Mul(m.Y, m.Y) - Fixed16.Mul(radius, radius) + Fixed16.Mul(m.X, m.X);
        int discriminant = (Fixed16.Mul(b, b) - Fixed16.Mul(a, c)) << 2;
        if (discriminant <= 0) return Miss;

        int root = IntegerMath.SquareRoot(discriminant);
        int first = -(root + b);
        int second = root - b;
        return (first < second ? first : second) >> 1;
    }
}
