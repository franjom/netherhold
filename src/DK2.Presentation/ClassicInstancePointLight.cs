// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>Instance-sampled radial light and displaced-vertex response; classic-lighting.md section3.</summary>
public readonly record struct ClassicInstancePointLight
{
    private ClassicInstancePointLight(Vector3 direction, Vector3 colour, float intensity, float attenuation)
    {
        Direction = direction;
        Colour = colour;
        Intensity = intensity;
        Attenuation = attenuation;
    }

    public Vector3 Direction { get; }
    public Vector3 Colour { get; }
    public float Intensity { get; }
    public float Attenuation { get; }

    /// <summary>Uses the supplied upper 3x3 rows as a column-vector transform; ignores translation.</summary>
    public static ClassicInstancePointLight Sample(in ClassicPointLight source, Vector3 instancePosition,
        in Matrix4x4 directionTransform)
    {
        Vector3 toLight = source.Position - instancePosition;
        float distanceSquared = (toLight.X * toLight.X + toLight.Y * toLight.Y) + toLight.Z * toLight.Z;
        float radiusSquared = source.Radius * source.Radius;
        if (source.Radius <= 0 || distanceSquared >= radiusSquared) return default;
        int index = (int)Math.Round(distanceSquared * 16.0 - 0.49999f, MidpointRounding.ToEven);
        if ((uint)index >= 256) throw new ArgumentOutOfRangeException(nameof(instancePosition), "Native light lookup has 256 bins.");
        float lookup = 0.25f + 16f / index;
        float attenuation = (radiusSquared - distanceSquared) * lookup * (1f / radiusSquared);
        if (!(attenuation > 0)) return default;
        Vector3 direction = new(
            directionTransform.M11 * toLight.X + directionTransform.M12 * toLight.Y + directionTransform.M13 * toLight.Z,
            directionTransform.M21 * toLight.X + directionTransform.M22 * toLight.Y + directionTransform.M23 * toLight.Z,
            directionTransform.M31 * toLight.X + directionTransform.M32 * toLight.Y + directionTransform.M33 * toLight.Z);
        return new(direction, source.Colour, source.Intensity, attenuation);
    }

    public Vector3 ContributionAt(Vector3 vertexDisplacement, Vector3 normal)
    {
        if (!(Attenuation > 0)) return Vector3.Zero;
        Vector3 toLight = Direction - vertexDisplacement;
        float incidence = ((toLight.X * normal.X + toLight.Z * normal.Z) + toLight.Y * normal.Y) * Intensity;
        if (BitConverter.SingleToInt32Bits(incidence) < 0) return Vector3.Zero;
        float factor = incidence * Attenuation;
        if (float.IsNaN(factor))
            throw new NotSupportedException("Native zero-incidence singular-bin propagation depends on the FPU exception mask.");
        return Colour * (factor <= 1f ? factor : 1f);
    }
}
