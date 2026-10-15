// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;

namespace DK2.Presentation;

public readonly record struct ClassicShadowCaster(Vector3 Centre, float Radius, float GroundHeight);
public readonly record struct ClassicShadowSource(Vector3 Position, float FadeDistance);
public readonly record struct ClassicShadowProjection(Vector3 Centre, Vector3 CrossAxis, Vector3 AlongAxis,
    float HalfWidth, float HalfLength, float Strength, float AngleRadians);

/// <summary>Quality-three receiver geometry; research/creature-shadows.md.</summary>
public static class ClassicMeshShadow
{
    public const int TextureSize = 32;
    public const int MaximumSources = 3;
    public const float MaximumDistance = 2.5f;

    public static bool TryProject(in ClassicShadowCaster caster, in ClassicShadowSource source,
        out ClassicShadowProjection projection)
    {
        projection = default;
        Vector3 delta = caster.Centre - source.Position;
        if (caster.Radius <= 0 || source.FadeDistance <= 0
            || delta.LengthSquared() >= MaximumDistance * MaximumDistance) return false;
        float distance = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        float strength = MathF.Min(.7f, 1.5f * (source.FadeDistance - distance) / source.FadeDistance);
        if (strength <= 0) return false;
        Vector3 along = distance > 0 ? new Vector3(delta.X / distance, 0, delta.Z / distance) : Vector3.UnitZ;
        Vector3 cross = new(along.Z, 0, -along.X);
        float angle = distance * MathF.PI * .25f * 1.5f / MaximumDistance;
        float halfLength = caster.Radius / MathF.Cos(angle);
        float shift = (caster.Centre.Y - caster.GroundHeight) * MathF.Tan(angle);
        Vector3 centre = new(caster.Centre.X, caster.GroundHeight, caster.Centre.Z);
        projection = new(centre + along * shift, cross, along, caster.Radius, halfLength, strength, angle);
        return true;
    }
}
