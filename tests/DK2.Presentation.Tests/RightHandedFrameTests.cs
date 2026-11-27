// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation.Tests;

/// <summary>The renderers' left-handed frame in a right-handed engine (godot-step3-audit.md, option 1): Z negated on
/// everything, rotations conjugated by S, and a camera that sees each point where the left-handed one did.</summary>
public class RightHandedFrameTests
{
    private const float Tolerance = 1e-5f;

    private static readonly ClassicCameraPose Pose = new(
        new Vector3(20.5f, 9f, -31f), Vector3.Normalize(new Vector3(0.35f, -0.8f, 0.45f)),
        Vector3.Normalize(new Vector3(0.2f, 0.55f, 0.3f)), 40f, 30f, 55f, 45f, 0.16f);

    [Fact]
    public void PointsNormalsAndDirectionsNegateZ()
    {
        Assert.Equal(new Vector3(1, 2, -3), RightHandedFrame.Point(new Vector3(1, 2, 3)));
        Assert.Equal(new Vector3(-1, 0.5f, 0.25f), RightHandedFrame.Normal(new Vector3(-1, 0.5f, -0.25f)));
        Assert.Equal(new Vector3(0, -1, 4), RightHandedFrame.Direction(new Vector3(0, -1, -4)));
        // Tile (3, 5)'s centre, (3.5, 0, -5.5) in the shared frame, is north-positive in the right-handed one.
        Assert.Equal(new Vector3(3.5f, 0, 5.5f), RightHandedFrame.Point(RenderWorld.Ground(3.5f, 5.5f)));
    }

    /// <summary>S·R·S applied to a mirrored vector is the mirror of R applied to the vector, for the matrix and the
    /// quaternion forms alike; a turn about Y reverses its sense.</summary>
    [Fact]
    public void ARotationIsConjugatedByTheMirror()
    {
        var q = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.7f, -0.4f, 1.1f));
        var m = Matrix4x4.CreateFromQuaternion(q);
        var v = new Vector3(0.3f, -1.2f, 2.5f);
        Vector3 expected = RightHandedFrame.Point(Vector3.Transform(v, q));

        AssertNear(expected, Vector3.Transform(RightHandedFrame.Point(v), RightHandedFrame.Rotation(q)));
        AssertNear(expected, Vector3.Transform(RightHandedFrame.Point(v), RightHandedFrame.Rotation(m)));
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        AssertNear(Vector3.Transform(Vector3.UnitX, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f)),
            Vector3.Transform(Vector3.UnitX, RightHandedFrame.Rotation(yaw)));
    }

    /// <summary>The camera basis is orthonormal and right-handed, looks down -Z along the mirrored forward, and keeps
    /// the left-handed look rotation's right (up × forward) and up, mirrored.</summary>
    [Fact]
    public void TheCameraLooksDownMinusZAlongTheMirroredForward()
    {
        RightHandedCamera camera = RightHandedFrame.Camera(Pose);
        var right = Vector3.Normalize(Vector3.Cross(Pose.Up, Pose.Forward));
        var up = Vector3.Cross(Pose.Forward, right);

        Assert.Equal(RightHandedFrame.Point(Pose.Position), camera.Origin);
        AssertNear(-RightHandedFrame.Direction(Pose.Forward), camera.Z);
        AssertNear(RightHandedFrame.Direction(right), camera.X);
        AssertNear(RightHandedFrame.Direction(up), camera.Y);
        AssertNear(camera.Z, Vector3.Cross(camera.X, camera.Y));
        Assert.Equal(1f, camera.X.Length(), 5);
        Assert.Equal(0f, Vector3.Dot(camera.X, camera.Y), 5);
    }

    /// <summary>Every point has the same view coordinates in both cameras, so the image is not mirrored.</summary>
    [Fact]
    public void APointKeepsItsViewCoordinates()
    {
        RightHandedCamera camera = RightHandedFrame.Camera(Pose);
        foreach (Vector3 p in new[] { new Vector3(21, 1, -35), new Vector3(15.25f, 0, -40), new Vector3(30, 2, -28) })
        {
            (float x, float y, float depth) left = LeftView(p);
            Vector3 d = RightHandedFrame.Point(p) - camera.Origin;
            Assert.Equal(left.x, Vector3.Dot(d, camera.X), 4);
            Assert.Equal(left.y, Vector3.Dot(d, camera.Y), 4);
            Assert.Equal(left.depth, -Vector3.Dot(d, camera.Z), 4);
        }
    }

    /// <summary>Indices pass through unchanged: a triangle clockwise on the left-handed screen is clockwise on the
    /// right-handed one, and its left-handed front normal, cross(b - a, c - a), mirrors to the right-handed engine's
    /// front normal for the same order, cross(c - a, b - a).</summary>
    [Fact]
    public void TheTriangleOrderKeepsItsFrontFace()
    {
        RightHandedCamera camera = RightHandedFrame.Camera(Pose);
        Vector3 a = new(21, 0, -35), b = new(21, 1, -35), c = new(22, 0, -35.5f);
        float leftArea = SignedArea(LeftScreen(a), LeftScreen(b), LeftScreen(c));
        float rightArea = SignedArea(RightScreen(camera, a), RightScreen(camera, b), RightScreen(camera, c));
        Assert.NotEqual(0f, leftArea);
        Assert.Equal(leftArea, rightArea, 4);

        var leftFront = Vector3.Cross(b - a, c - a);
        Vector3 ra = RightHandedFrame.Point(a), rb = RightHandedFrame.Point(b), rc = RightHandedFrame.Point(c);
        AssertNear(RightHandedFrame.Normal(leftFront), Vector3.Cross(rc - ra, rb - ra));
    }

    /// <summary>A ray leaving the right-handed engine comes back through the same mirror, and hits what it hit.</summary>
    [Fact]
    public void ARayComesBackThroughTheSameMirror()
    {
        var target = new Vector3(18.5f, 0, -33.5f);
        RightHandedCamera camera = RightHandedFrame.Camera(Pose);
        Vector3 rightTarget = RightHandedFrame.Point(target);
        (Vector3 origin, Vector3 direction) = RightHandedFrame.Ray(camera.Origin, Vector3.Normalize(rightTarget - camera.Origin));

        Assert.Equal(Pose.Position, origin);
        AssertNear(Vector3.Normalize(target - Pose.Position), direction);
    }

    /// <summary>Unity's projection with <c>[1, 2] = -shift</c> puts the view axis at NDC y = +shift (above the
    /// middle). A frustum whose centre sits <see cref="RightHandedFrame.Frustum"/>'s offset above the axis does the
    /// same: its NDC y for a point is 2 (y_near - centre) / height.</summary>
    [Theory]
    [InlineData(40f, 0.3f, 0.16f)]
    [InlineData(55f, 0.05f, -0.2f)]
    [InlineData(30f, 1f, 0f)]
    public void TheFrustumOffsetPutsTheAxisWhereUnitysSkewDid(float fov, float near, float shift)
    {
        (float height, float offset) = RightHandedFrame.Frustum(fov, near, shift);
        Assert.Equal(2f * near * MathF.Tan(fov * MathF.PI / 360f), height, 5);
        // On the view axis the point at the near plane has y = 0.
        Assert.Equal(shift, 2f * (0f - offset) / height, 5);
        // Unity's skewed matrix: y_ndc = (P11 y + P12 z) / -z with P12 = -shift, z = -near.
        float p11 = 1f / MathF.Tan(fov * MathF.PI / 360f), yNear = 0.07f;
        float unity = ((p11 * yNear) + (-shift * -near)) / near;
        Assert.Equal(unity, 2f * (yNear - offset) / height, 4);
    }

    private static (float X, float Y, float Depth) LeftView(Vector3 p)
    {
        var right = Vector3.Normalize(Vector3.Cross(Pose.Up, Pose.Forward));
        var up = Vector3.Cross(Pose.Forward, right);
        Vector3 d = p - Pose.Position;
        return (Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, Pose.Forward));
    }

    private static Vector2 LeftScreen(Vector3 p)
    {
        (float x, float y, float depth) = LeftView(p);
        return new Vector2(x / depth, y / depth);
    }

    private static Vector2 RightScreen(RightHandedCamera camera, Vector3 p)
    {
        Vector3 d = RightHandedFrame.Point(p) - camera.Origin;
        float depth = -Vector3.Dot(d, camera.Z);
        return new Vector2(Vector3.Dot(d, camera.X) / depth, Vector3.Dot(d, camera.Y) / depth);
    }

    private static float SignedArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) <= Tolerance, $"expected {expected}, got {actual}");
    }
}
