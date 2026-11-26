// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;

namespace DK2.Presentation;

/// <summary>An axis-aligned box in the renderers' frame, as Unity's <c>Bounds</c> holds it: a drawn thing's bounds for the
/// Hand's pick (<see cref="WorldHoverPicker.TryAdmit"/>).</summary>
public readonly record struct PickBox(Vector3 Min, Vector3 Max)
{
    public Vector3 Centre => new((Min.X + Max.X) * .5f, (Min.Y + Max.Y) * .5f, (Min.Z + Max.Z) * .5f);

    /// <summary>Half the size on each axis.</summary>
    public Vector3 Extents => new((Max.X - Min.X) * .5f, (Max.Y - Min.Y) * .5f, (Max.Z - Min.Z) * .5f);

    /// <summary>The box around <paramref name="points"/>; an empty list gives the empty box at the origin.</summary>
    public static PickBox Around(IReadOnlyList<Vector3> points)
    {
        if (points is null) throw new ArgumentNullException(nameof(points));
        if (points.Count == 0) return default;
        Vector3 low = points[0], high = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            low = Vector3.Min(low, points[i]);
            high = Vector3.Max(high, points[i]);
        }
        return new PickBox(low, high);
    }

    /// <summary>The world box of a local box drawn at <paramref name="position"/>, turned by <paramref name="turn"/> and scaled
    /// by <paramref name="scale"/> (Unity's renderer bounds): the centre carried over, the extents through the absolute
    /// rotation-scale matrix.</summary>
    public PickBox Placed(Vector3 position, Quaternion turn, Vector3 scale)
    {
        Vector3 x = Vector3.Transform(new Vector3(scale.X, 0, 0), turn), y = Vector3.Transform(new Vector3(0, scale.Y, 0), turn),
            z = Vector3.Transform(new Vector3(0, 0, scale.Z), turn);
        Vector3 c = Centre, e = Extents;
        Vector3 centre = position + (x * c.X) + (y * c.Y) + (z * c.Z);
        var extents = new Vector3(
            (MathF.Abs(x.X) * e.X) + (MathF.Abs(y.X) * e.Y) + (MathF.Abs(z.X) * e.Z),
            (MathF.Abs(x.Y) * e.X) + (MathF.Abs(y.Y) * e.Y) + (MathF.Abs(z.Y) * e.Z),
            (MathF.Abs(x.Z) * e.X) + (MathF.Abs(y.Z) * e.Y) + (MathF.Abs(z.Z) * e.Z));
        return new PickBox(centre - extents, centre + extents);
    }

    /// <summary>Where the ray enters the box (slabs, standing in for Unity's <c>Bounds.IntersectRay</c>): the entry
    /// distance, or null when the ray misses or the box lies behind it. From inside it is negative, our definition; a pick
    /// cannot tell, since <see cref="WorldHoverPicker.Admits"/> compares it only with a positive terrain distance.</summary>
    public float? RayEntry(Vector3 origin, Vector3 direction)
    {
        float near = float.NegativeInfinity, far = float.PositiveInfinity;
        if (!Slab(origin.X, direction.X, Min.X, Max.X, ref near, ref far)
            || !Slab(origin.Y, direction.Y, Min.Y, Max.Y, ref near, ref far)
            || !Slab(origin.Z, direction.Z, Min.Z, Max.Z, ref near, ref far))
            return null;
        return far < 0f ? null : near;
    }

    private static bool Slab(float origin, float direction, float low, float high, ref float near, ref float far)
    {
        if (direction == 0f) return origin >= low && origin <= high;
        float a = (low - origin) / direction, b = (high - origin) / direction;
        if (a > b) (a, b) = (b, a);
        near = Math.Max(near, a);
        far = Math.Min(far, b);
        return near <= far;
    }
}

/// <summary>A model's bounding sphere for the Hand's screen-circle test (<see cref="HoverPickRules.CircleHolds"/>): the KMF
/// Origin in the renderers' frame and the KMF BoundsScale.</summary>
public readonly record struct PickSphere(Vector3 Centre, float Radius)
{
    /// <summary>Whether the pick uses it: a sphere of no radius leaves the renderer bounds to stand in (Unity's UV2 test).</summary>
    public bool Usable => Radius > 0f;

    /// <summary>The model's sphere: an animation's first frame, else its mesh (the bounds Unity's <c>ClassicMeshes</c> writes
    /// into UV2); null for a model with neither.</summary>
    public static PickSphere? Of(KmfModel model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        KmfMesh? bounds = model.Animation is { IsComplete: true } animation ? animation.Frames[0] : model.Mesh;
        return bounds is null ? null : new PickSphere(RenderMesh.ToRenderFrame(bounds.Origin), bounds.BoundsScale);
    }

    /// <summary>The radius drawn at <paramref name="scale"/>: times its largest absolute axis.</summary>
    public static float ScaledRadius(float radius, Vector3 scale) =>
        radius * Math.Max(Math.Abs(scale.X), Math.Max(Math.Abs(scale.Y), Math.Abs(scale.Z)));

    /// <summary>The sphere of a model drawn at <paramref name="position"/>, <paramref name="turn"/> and <paramref name="scale"/>.</summary>
    public PickSphere Placed(Vector3 position, Quaternion turn, Vector3 scale) =>
        new(position + Vector3.Transform(Centre * scale, turn), ScaledRadius(Radius, scale));
}

/// <summary>The Hand's triangle test on a drawn model, in model coordinates.</summary>
public static class PickTriangles
{
    /// <summary>Lowers <paramref name="nearest"/> to the ray distance of the nearest triangle of <paramref name="triangles"/>
    /// (three indices into <paramref name="positions"/> each) the ray crosses before it
    /// (<see cref="WorldHoverPicker.TryHitTriangle"/>); true when one did.</summary>
    public static bool TryNearest(IReadOnlyList<Vector3> positions, IReadOnlyList<int> triangles, Vector3 origin,
        Vector3 direction, ref float nearest)
    {
        if (positions is null) throw new ArgumentNullException(nameof(positions));
        if (triangles is null) throw new ArgumentNullException(nameof(triangles));
        bool found = false;
        for (int i = 0; i + 2 < triangles.Count; i += 3)
            if (WorldHoverPicker.TryHitTriangle(origin, direction, positions[triangles[i]], positions[triangles[i + 1]],
                    positions[triangles[i + 2]], out float t) && t < nearest)
            {
                nearest = t;
                found = true;
            }
        return found;
    }

    /// <summary>A ray in the space of a model drawn at <paramref name="position"/>, <paramref name="turn"/> and
    /// <paramref name="scale"/>; the map is affine, so a distance along it is the same in both spaces.</summary>
    public static void ToLocal(Vector3 position, Quaternion turn, Vector3 scale, ref Vector3 origin, ref Vector3 direction)
    {
        var back = Quaternion.Conjugate(turn);
        origin = Vector3.Transform(origin - position, back) / scale;
        direction = Vector3.Transform(direction, back) / scale;
    }
}
