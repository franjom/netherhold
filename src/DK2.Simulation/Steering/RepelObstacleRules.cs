// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Steering;

/// <summary>DK2's <c>Repel_Obstacle</c> behaviour (<c>0x004D31D0</c>, weight 0.4;
/// docs/research/steering-behaviours.md §2). Any overlapping soft <c>Obstacle</c> shoves at full
/// <c>max_force</c> with no falloff; a <c>SolidObstacle</c> is not pushed against but recorded,
/// up to twelve, for the integrator to slide the velocity along.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/steering-behaviours.md")]
public static class RepelObstacleRules
{
    public const int Weight = 0x6666;

    private static readonly List<SteeringObstacle> Scratch = new();

    public static Fixed16Vector Compute(SteeringBody body, ISteeringWorld world)
    {
        body.SolidContacts.Clear();
        int reach = body.Radius + Fixed16.One;

        if (body.SnapsOnArrival && body.OnLastPoint && (body.Pos - body.CurrentPoint).Octile() <= reach)
            return Fixed16Vector.Zero;
        if (body.EscapeStuckMode) return Fixed16Vector.Zero;

        Scratch.Clear();
        world.CollectObstacles(body.Pos, reach, Scratch);

        Fixed16Vector force = Fixed16Vector.Zero;
        foreach (SteeringObstacle obstacle in Scratch) force += Contact(body, obstacle);
        return force;
    }

    /// <summary>One obstacle's contribution (<c>0x004D3020</c>): nothing unless the centres are
    /// within <c>size + radius</c>; then the full-force shove, or a record for the slide.</summary>
    public static Fixed16Vector Contact(SteeringBody body, SteeringObstacle obstacle)
    {
        Fixed16Vector v = body.Pos - obstacle.Pos;
        int distance = v.Length();
        if (distance == 0 || distance > obstacle.Size + body.Radius) return Fixed16Vector.Zero;

        Fixed16Vector normal = Fixed16.Normalise(v);
        if (!obstacle.Solid) return normal.Scale(body.MaxForce);

        if (body.SolidContacts.Count < SteeringBody.SolidContactSlots)
            body.SolidContacts.Add(new SolidContact(normal, obstacle.Flag2));
        return Fixed16Vector.Zero;
    }
}
