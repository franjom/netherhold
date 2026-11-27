// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>A camera's six frustum planes in the renderers' frame (left, right, bottom, top, near, far; normals point
/// inward, a plane's value at a point is <c>N·p + D</c>), with the view-projection they came from. Matrices here are
/// column-vector transforms (<c>p' = M p</c>). Godot builds the planes from the classic pose.</summary>
public readonly struct ViewFrustum
{
    /// <summary>How far outside a plane all three corners of a triangle must lie before it is culled (Unity's
    /// <c>TransparentTerrain</c>).</summary>
    public const float CullMargin = .0001f;

    private readonly Plane _left, _right, _bottom, _top, _near, _far;

    /// <summary>The view-projection the planes were taken from; two frusta are the same view when these are equal as
    /// Unity's <c>Matrix4x4 ==</c> compares them (<see cref="SameView"/>).</summary>
    public Matrix4x4 ViewProjection { get; }

    public ViewFrustum(in Matrix4x4 viewProjection, Plane left, Plane right, Plane bottom, Plane top, Plane near, Plane far)
    {
        ViewProjection = viewProjection;
        _left = left; _right = right; _bottom = bottom; _top = top; _near = near; _far = far;
    }

    /// <summary>The plane at <paramref name="index"/>, 0 to 5 in the order above.</summary>
    public Plane this[int index]
    {
        get
        {
            return index switch
            {
                0 => _left,
                1 => _right,
                2 => _bottom,
                3 => _top,
                4 => _near,
                5 => _far,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }
    }

    /// <summary>The planes of a view-projection (Gribb and Hartmann: the fourth row plus or minus each other row),
    /// normalised.</summary>
    public static ViewFrustum FromViewProjection(in Matrix4x4 m)
    {
        var row0 = new Vector4(m.M11, m.M12, m.M13, m.M14);
        var row1 = new Vector4(m.M21, m.M22, m.M23, m.M24);
        var row2 = new Vector4(m.M31, m.M32, m.M33, m.M34);
        var row3 = new Vector4(m.M41, m.M42, m.M43, m.M44);
        return new ViewFrustum(m, Normalised(row3 + row0), Normalised(row3 - row0), Normalised(row3 + row1),
            Normalised(row3 - row1), Normalised(row3 + row2), Normalised(row3 - row2));
    }

    /// <summary>The frustum of the camera at <paramref name="pose"/>: a
    /// perspective of the pose's vertical field of view over <paramref name="aspect"/> (width over height), from
    /// <paramref name="near"/> to the pose's far distance but at least 0.01 beyond the near, with
    /// <c>projection[1, 2] = -shift</c>, looking along the forward with the pose's up (<c>Quaternion.LookRotation</c>).</summary>
    public static ViewFrustum FromPose(in ClassicCameraPose pose, float aspect, float near)
    {
        float far = Math.Max(near + .01f, pose.FarClipDistance);
        var forward = Vector3.Normalize(pose.Forward);
        var right = Vector3.Normalize(Vector3.Cross(pose.Up, forward));
        var up = Vector3.Cross(forward, right);
        // worldToCameraMatrix: the camera's inverse transform with z negated (camera space looks down -z).
        var view = new Matrix4x4(
            right.X, right.Y, right.Z, -Vector3.Dot(right, pose.Position),
            up.X, up.Y, up.Z, -Vector3.Dot(up, pose.Position),
            -forward.X, -forward.Y, -forward.Z, Vector3.Dot(forward, pose.Position),
            0, 0, 0, 1);
        float cotangent = 1f / MathF.Tan(pose.VerticalFieldOfView * (MathF.PI / 360f));
        var projection = new Matrix4x4(
            cotangent / aspect, 0, 0, 0,
            0, cotangent, -pose.ProjectionCentreShift, 0,
            0, 0, (far + near) / (near - far), 2f * far * near / (near - far),
            0, 0, -1, 0);
        return FromViewProjection(Matrix4x4.Multiply(projection, view));
    }

    /// <summary>True when one plane has all three points more than <see cref="CullMargin"/> outside it.</summary>
    public bool Excludes(Vector3 a, Vector3 b, Vector3 c)
    {
        return Outside(_left, a, b, c) || Outside(_right, a, b, c) || Outside(_bottom, a, b, c)
        || Outside(_top, a, b, c) || Outside(_near, a, b, c) || Outside(_far, a, b, c);
    }

    /// <summary>True when the box lies wholly outside one plane: the centre's distance plus the extents projected on the
    /// plane's normal below zero (Unity's <c>GeometryUtility.TestPlanesAABB</c> test; a box touching a plane is kept, our
    /// choice). The Hand's view cull uses it in place of DK2's sphere against four side planes (0x00575D70).</summary>
    public bool Excludes(in PickBox box)
    {
        Vector3 centre = box.Centre, extents = box.Extents;
        return OutsideBox(_left, centre, extents) || OutsideBox(_right, centre, extents) || OutsideBox(_bottom, centre, extents)
            || OutsideBox(_top, centre, extents) || OutsideBox(_near, centre, extents) || OutsideBox(_far, centre, extents);
    }

    private static bool OutsideBox(in Plane plane, Vector3 centre, Vector3 extents)
    {
        Vector3 n = plane.Normal;
        float distance = n.X * centre.X + n.Y * centre.Y + n.Z * centre.Z + plane.D;
        float reach = Math.Abs(n.X) * extents.X + Math.Abs(n.Y) * extents.Y + Math.Abs(n.Z) * extents.Z;
        return distance + reach < 0f;
    }

    /// <summary>Unity's <c>Matrix4x4 ==</c> on <see cref="ViewProjection"/>: each column's squared difference below
    /// 1e-10 (<c>Vector4 ==</c>).</summary>
    public bool SameView(in ViewFrustum other)
    {
        return ApproximatelyEqual(ViewProjection, other.ViewProjection);
    }

    /// <summary>Unity's <c>Matrix4x4 ==</c>, column by column.</summary>
    public static bool ApproximatelyEqual(in Matrix4x4 a, in Matrix4x4 b)
    {
        return Close(a.M11 - b.M11, a.M21 - b.M21, a.M31 - b.M31, a.M41 - b.M41)
        && Close(a.M12 - b.M12, a.M22 - b.M22, a.M32 - b.M32, a.M42 - b.M42)
        && Close(a.M13 - b.M13, a.M23 - b.M23, a.M33 - b.M33, a.M43 - b.M43)
        && Close(a.M14 - b.M14, a.M24 - b.M24, a.M34 - b.M34, a.M44 - b.M44);
    }

    // Unity's Vector4 ==: the squared length of the difference below kEpsilon squared.
    private static bool Close(float x, float y, float z, float w)
    {
        return x * x + y * y + z * z + w * w < 9.99999944E-11f;
    }

    private static bool Outside(in Plane plane, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = plane.Normal;
        return n.X * a.X + n.Y * a.Y + n.Z * a.Z + plane.D < -CullMargin
            && n.X * b.X + n.Y * b.Y + n.Z * b.Z + plane.D < -CullMargin
            && n.X * c.X + n.Y * c.Y + n.Z * c.Z + plane.D < -CullMargin;
    }

    private static Plane Normalised(Vector4 row)
    {
        float length = MathF.Sqrt(row.X * row.X + row.Y * row.Y + row.Z * row.Z);
        return new Plane(row.X / length, row.Y / length, row.Z / length, row.W / length);
    }
}
