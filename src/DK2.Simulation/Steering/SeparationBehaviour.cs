// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Steering;

/// <summary>DK2's <c>Separation</c> behaviour (<c>0x004D2DF0</c>, weight 0.13;
/// docs/research/navigation-and-steering.md §3.2), assembled from <see cref="SeparationRules"/>:
/// a push from every overlapping neighbour, scaled by the cosine against the body's own velocity
/// and the neighbour's size share through the original's unshifted multiply, plus a repulsion
/// from one remembered entity within its sight range.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/navigation-and-steering.md")]
public static class SeparationBehaviour
{
    public const int Weight = 0x2147;

    /// <summary>The neighbour query's square span is 1.2 times the mover's radius.</summary>
    public const int QueryRadii = 0x13333;

    /// <summary>Arrived within half a tile of the last point, with the flag, nothing pushes.</summary>
    public const int ArrivalSlack = 0x8000;

    // Per thread, not per process: the original is single-threaded, the test host is not.
    [ThreadStatic] private static List<SteeringNeighbour>? _scratch;

    private static List<SteeringNeighbour> Scratch => _scratch ??= new List<SteeringNeighbour>();

    public static Fixed16Vector Compute(SteeringBody body, SteeringMover mover, ISteeringWorld world)
    {
        if (body.SeparatesUntilArrival && (body.OnLastPoint || body.PathFinished)
            && (body.Pos - body.CurrentPoint).Octile() <= body.Radius + ArrivalSlack)
            return Fixed16Vector.Zero;

        Scratch.Clear();
        world.CollectNeighbours(body.Pos, Fixed16.Mul(QueryRadii, body.Radius), Scratch);

        Fixed16Vector force = Fixed16Vector.Zero;
        foreach (SteeringNeighbour n in Scratch) force += Push(body, mover.SizeClass, n.Body, n.SizeClass);

        SteeringBody? other = world.RelatedBody(mover, out int reachTiles);
        return other == null ? force : force + KeepAway(body, other, reachTiles);
    }

    /// <summary>The callback <c>0x004D2B80</c>: one neighbour's contribution to the pusher.</summary>
    public static Fixed16Vector Push(SteeringBody pusher, int pusherSize, SteeringBody neighbour, int neighbourSize)
    {
        if (ReferenceEquals(pusher, neighbour) || pusher.IsBlocker) return Fixed16Vector.Zero;

        Fixed16Vector v = pusher.Pos - neighbour.Pos;
        int distance = v.Octile();
        Fixed16Vector normal = Fixed16.Normalise(v);
        int radii = pusher.Radius + neighbour.Radius;

        Fixed16Vector push = Fixed16Vector.Zero;
        if (SeparationRules.Applies(distance, radii))
        {
            int ratio = SeparationRules.Ratio(distance, radii);
            push = normal.Scale(SeparationRules.Magnitude(ratio, pusher.MaxForce, neighbour.IsBlocker, pusher.IgnoreBlocker));
        }

        int cosine = normal.Dot(Fixed16.Normalise(pusher.Vel));
        return push.Scale(SeparationRules.Scale(cosine, SeparationRules.SizeShare(pusherSize, neighbourSize)));
    }

    /// <summary>Step 3 of the outer routine: away from the remembered entity while within its reach
    /// in tiles plus the body's radius; the two-arm magnitude with no root arm.</summary>
    public static Fixed16Vector KeepAway(SteeringBody body, SteeringBody other, int reachTiles)
    {
        Fixed16Vector v = body.Pos - other.Pos;
        int distance = v.Octile();
        if (distance == 0) return Fixed16Vector.Zero;

        int reach = (reachTiles << 16) + body.Radius;
        if (distance > reach) return Fixed16Vector.Zero;

        int ratio = Fixed16.Div(distance, reach);
        int magnitude = ratio <= SeparationRules.SaturationRatio ? body.MaxForce : SeparationRules.Reciprocal(ratio);
        return Fixed16.Normalise(v).Scale(magnitude);
    }
}
