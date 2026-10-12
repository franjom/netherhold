// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Pathfinding;

namespace DK2.Simulation.Steering;

/// <summary>The mover's route request and its window of five points (<c>0x004D5100</c>;
/// docs/research/steering-behaviours.md §7). The window does not slide: when its points are used
/// up the whole route is requested again from where the body stands.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class RouteWindow
{
    /// <summary>Sixteenths of a tile from a 16.16 distance, the window's bookkeeping unit.</summary>
    public static int Sixteenths(int distance) => distance >> 12;

    /// <summary>Route from the body's position and load the window. True when a route exists.</summary>
    public static bool Request(SteeringBody body, ISteeringWorld world, MoverProfile mover, Fixed16Vector target, byte flags)
    {
        // ADR-0439 deliberately moves live-core destinations to their edge.
        target = world.ConstrainTarget(body.Pos, target);
        var points = new List<Fixed16Vector>();
        world.Route(body.Pos, target, mover, points);
        Load(body, points, target, flags);
        return points.Count != 0;
    }

    /// <summary>Fill the body from a route: counts, the three distances, the first five points and
    /// their segments. An empty route finishes the path at once.</summary>
    public static void Load(SteeringBody body, IReadOnlyList<Fixed16Vector> route, Fixed16Vector target, byte flags)
    {
        body.PathTargetPos = target;
        body.PathFlags = flags;
        body.PathStuckBuffer = 0;
        body.PathLastProgress = 0;
        body.SubpathIndex = 0;

        int n = route.Count;
        if (n == 0)
        {
            body.PathTotalCount = 0;
            body.PathBufferCount = 0;
            body.PathTotalDist = 0;
            body.PathSubpathDist = 0;
            body.PathTailDist = 0;
            body.PathFinished = true;
            return;
        }

        body.PathTotalCount = (byte)n;
        body.PathBufferCount = n < SteeringBody.WindowSize ? n : SteeringBody.WindowSize;

        int total = Sixteenths((route[0] - body.Pos).Octile());
        for (int i = 1; i < n; i++) total += Sixteenths((route[i] - route[i - 1]).Octile());
        body.PathTotalDist = total;

        for (int i = 0; i < body.PathBufferCount; i++) body.Subpath[i] = route[i];

        int window = 0;
        for (int k = 0; k + 1 < body.PathBufferCount; k++)
        {
            body.Segment[k] = Sixteenths((body.Subpath[k + 1] - body.Subpath[k]).Octile());
            window += body.Segment[k];
        }
        body.PathSubpathDist = window;

        int tail = 0;
        for (int j = body.PathBufferCount; j < n; j++) tail += Sixteenths((route[j] - route[j - 1]).Octile());
        body.PathTailDist = tail;

        body.PathFinished = false;
    }

    /// <summary>Step to the next window point (§5.2), dropping its segment from the window's
    /// distance. True when the window is used up and the route must be requested again.</summary>
    public static bool Advance(SteeringBody body)
    {
        body.PathSubpathDist -= body.Segment[body.SubpathIndex];
        body.SubpathIndex = (body.SubpathIndex + 1) & 7;
        return body.SubpathIndex == body.PathBufferCount;
    }
}
