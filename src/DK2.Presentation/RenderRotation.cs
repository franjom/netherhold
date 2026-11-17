// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>A <see cref="RenderEuler"/> as one rotation in the renderers' frame (<see cref="RenderWorld"/>), composed in
/// the order <see cref="MeshOrientation"/> states: the yaw about Y, then the roll about Z, then the pitch about X, outermost
/// first. Each angle turns as a left-handed engine's <c>Quaternion.Euler</c> and <c>AngleAxis</c> turn it, which is the
/// same quaternion as <see cref="Quaternion.CreateFromAxisAngle"/>; a right-handed engine converts the result with
/// <see cref="RightHandedFrame.Rotation(Quaternion)"/>.</summary>
public static class RenderRotation
{
    private const float Radians = MathF.PI / 180f;

    /// <summary>Yaw · roll · pitch, applied to a point as <see cref="Vector3.Transform(Vector3, Quaternion)"/>.</summary>
    public static Quaternion Of(in RenderEuler turn) =>
        Heading(turn.Yaw)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, turn.Roll * Radians)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, turn.Pitch * Radians);

    /// <summary>A heading-only turn, <paramref name="yawDegrees"/> about Y.</summary>
    public static Quaternion Heading(float yawDegrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * Radians);
}
