// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation.Tests;

/// <summary><see cref="RenderRotation"/> against the Unity adapter's <c>RenderEulerRotation</c> it came from (plan step 3f):
/// <c>Euler(0, yaw, 0) · AngleAxis(roll, forward) · Euler(pitch, 0, 0)</c>, multiplied as Unity's operator does.</summary>
public sealed class RenderRotationTests
{
    [Fact]
    public void TheCompositionIsUnitysOverManyTurns()
    {
        var random = new Random(7);
        for (int round = 0; round < 500; round++)
        {
            var turn = new RenderEuler(Angle(random), Angle(random), Angle(random));
            Quaternion shared = RenderRotation.Of(turn);
            Quaternion legacy = Legacy(turn);
            Assert.True(Same(shared, legacy), $"{turn}: {shared} against {legacy}");
        }
    }

    /// <summary>Unity's own answers: a yaw of 90 turns right to back, a roll of 90 right to up, a pitch of 90 up to forward.</summary>
    [Theory]
    [InlineData(90f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, -1f)]
    [InlineData(0f, 90f, 0f, 1f, 0f, 0f, 0f, 1f, 0f)]
    [InlineData(0f, 0f, 90f, 0f, 1f, 0f, 0f, 0f, 1f)]
    // The pitch turns first and the yaw last: up goes forward, then forward goes right.
    [InlineData(90f, 0f, 90f, 0f, 1f, 0f, 1f, 0f, 0f)]
    // The roll before the yaw: right goes up and stays up.
    [InlineData(90f, 90f, 0f, 1f, 0f, 0f, 0f, 1f, 0f)]
    // The pitch before the roll: up goes forward, which the roll keeps.
    [InlineData(0f, 90f, 90f, 0f, 1f, 0f, 0f, 0f, 1f)]
    public void EachTurnIsAboutItsAxisInItsPlace(float yaw, float roll, float pitch, float x, float y, float z,
        float ex, float ey, float ez)
    {
        var turned = Vector3.Transform(new Vector3(x, y, z), RenderRotation.Of(new RenderEuler(yaw, roll, pitch)));
        Assert.True(Vector3.Distance(new Vector3(ex, ey, ez), turned) < 1e-6f, turned.ToString());
    }

    [Fact]
    public void AHeadingIsTheYawAlone()
    {
        Assert.True(Same(RenderRotation.Of(new RenderEuler(33f, 0, 0)), RenderRotation.Heading(33f)));
        Assert.True(Same(Legacy(new RenderEuler(-170f, 0, 0)), RenderRotation.Heading(-170f)));
    }

    /// <summary>A right-handed engine takes the rotation through <see cref="RightHandedFrame"/>: the mirrored turn moves a
    /// mirrored point to the mirror of the turned point.</summary>
    [Fact]
    public void TheMirroredRotationTurnsMirroredPoints()
    {
        var turn = new RenderEuler(37f, -12f, 64f);
        var point = new Vector3(0.3f, 1.2f, -0.7f);
        Vector3 expected = RightHandedFrame.Point(Vector3.Transform(point, RenderRotation.Of(turn)));
        var mirrored = Vector3.Transform(RightHandedFrame.Point(point), RightHandedFrame.Rotation(RenderRotation.Of(turn)));
        Assert.True(Vector3.Distance(expected, mirrored) < 1e-5f);
    }

    private static float Angle(Random random)
    {
        return (float)(random.NextDouble() * 720 - 360);
    }

    private static bool Same(Quaternion a, Quaternion b)
    {
        return Math.Abs(a.X - b.X) < 1e-6f && Math.Abs(a.Y - b.Y) < 1e-6f && Math.Abs(a.Z - b.Z) < 1e-6f && Math.Abs(a.W - b.W) < 1e-6f;
    }

    // The old adapter: Unity's single-axis Euler and AngleAxis quaternions, and its operator * written out.
    private static Quaternion Legacy(RenderEuler turn)
    {
        Quaternion yaw = Axis(0, 1, 0, turn.Yaw), roll = Axis(0, 0, 1, turn.Roll), pitch = Axis(1, 0, 0, turn.Pitch);
        return Multiply(Multiply(yaw, roll), pitch);
    }

    private static Quaternion Axis(float x, float y, float z, float degrees)
    {
        double half = degrees * Math.PI / 360d;
        float s = (float)Math.Sin(half);
        return new Quaternion(x * s, y * s, z * s, (float)Math.Cos(half));
    }

    private static Quaternion Multiply(Quaternion lhs, Quaternion rhs)
    {
        return new(
        lhs.W * rhs.X + lhs.X * rhs.W + lhs.Y * rhs.Z - lhs.Z * rhs.Y,
        lhs.W * rhs.Y + lhs.Y * rhs.W + lhs.Z * rhs.X - lhs.X * rhs.Z,
        lhs.W * rhs.Z + lhs.Z * rhs.W + lhs.X * rhs.Y - lhs.Y * rhs.X,
        lhs.W * rhs.W - lhs.X * rhs.X - lhs.Y * rhs.Y - lhs.Z * rhs.Z);
    }
}
