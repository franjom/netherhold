// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>The renderers' frame (<see cref="RenderWorld"/>: X east, Y up, Z north, left-handed) in a right-handed
/// engine whose camera looks down its local -Z: every point, normal and direction has Z negated (S = diag(1, 1, -1)),
/// a rotation R becomes S·R·S, and the camera takes the converted pose (godot-step3-audit.md, option 1).</summary>
/// <remarks>Triangle indices pass through unchanged. S mirrors the geometry and the camera together, so every point
/// keeps its view coordinates and lands on the same pixel; both engines take clockwise-on-screen faces as front faces,
/// so the same index order faces the same way. S is its own inverse, so the same calls bring a ray back.</remarks>
public static class RightHandedFrame
{
    public static Vector3 Point(Vector3 point)
    {
        return new(point.X, point.Y, -point.Z);
    }

    public static Vector3 Normal(Vector3 normal)
    {
        return new(normal.X, normal.Y, -normal.Z);
    }

    public static Vector3 Direction(Vector3 direction)
    {
        return new(direction.X, direction.Y, -direction.Z);
    }

    /// <summary>S·R·S for a rotation (or any linear part) R; a translation row is mirrored as a point.</summary>
    public static Matrix4x4 Rotation(Matrix4x4 r)
    {
        return new(
        r.M11, r.M12, -r.M13, r.M14,
        r.M21, r.M22, -r.M23, r.M24,
        -r.M31, -r.M32, r.M33, -r.M34,
        r.M41, r.M42, -r.M43, r.M44);
    }

    /// <summary>S·R·S as a quaternion: the axis mirrored and the angle reversed.</summary>
    public static Quaternion Rotation(Quaternion q)
    {
        return new(-q.X, -q.Y, q.Z, q.W);
    }

    /// <summary>A ray between the frames, either way.</summary>
    public static (Vector3 Origin, Vector3 Direction) Ray(Vector3 origin, Vector3 direction)
    {
        return (Point(origin), Direction(direction));
    }

    /// <summary>A camera at <paramref name="pose"/>: its origin and its basis columns, X right, Y up and Z back (the
    /// view looks down -Z), as the left-handed engine's look rotation (forward, up) orthonormalises them.</summary>
    public static RightHandedCamera Camera(in ClassicCameraPose pose)
    {
        Vector3 back = -Vector3.Normalize(Direction(pose.Forward));
        var right = Vector3.Normalize(Vector3.Cross(Direction(pose.Up), back));
        return new RightHandedCamera(Point(pose.Position), right, Vector3.Cross(back, right), back);
    }

    /// <summary>A frustum at <paramref name="near"/> with the pose's vertical field of view and its view axis
    /// <paramref name="centreShift"/> half heights above the middle (<see cref="ClassicCameraPose.ProjectionCentreShift"/>):
    /// the frustum's height there and its centre's offset from the axis, up positive: -shift half heights, so for a
    /// positive shift the centre sits below the axis and the axis draws above the middle.</summary>
    public static (float Height, float CentreOffset) Frustum(float verticalFieldOfViewDegrees, float near, float centreShift)
    {
        float height = 2f * near * MathF.Tan(verticalFieldOfViewDegrees * (MathF.PI / 360f));
        return (height, -centreShift * height / 2f);
    }
}

/// <summary>A right-handed camera: its origin and basis columns (X right, Y up, Z back).</summary>
public readonly record struct RightHandedCamera(Vector3 Origin, Vector3 X, Vector3 Y, Vector3 Z);
