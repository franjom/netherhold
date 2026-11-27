// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using Xunit;

namespace DK2.Presentation.Tests;

public sealed class ViewFrustumTests
{
    private static ClassicCameraPose Pose(float shift)
    {
        return new(new Vector3(20f, 9f, -30f),
        Vector3.Normalize(new Vector3(.3f, -.8f, .5f)), Vector3.UnitY, 50f, 40f, 55f, 31f, shift);
    }

    private static float Value(in Plane plane, Vector3 point)
    {
        return Vector3.Dot(plane.Normal, point) + plane.D;
    }

    /// <summary>The corners of the near and far rectangles, cast as <see cref="MinimapCameraView.RayDirection"/> casts
    /// Unity's viewport rays, lie on the side planes, and each rectangle on its own plane.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.16f)]
    [InlineData(-.3f)]
    public void The_side_planes_pass_through_the_off_centre_viewport_corners(float shift)
    {
        ClassicCameraPose pose = Pose(shift);
        const float aspect = 16f / 9f, near = .3f;
        var frustum = ViewFrustum.FromPose(pose, aspect, near);
        var view = new MinimapCameraView(pose.Position, pose.Forward, pose.Up, pose.VerticalFieldOfView, aspect, shift);
        var forward = Vector3.Normalize(pose.Forward);
        (float X, float Y, int[] Planes)[] corners = [(0, 0, [0, 2]), (1, 0, [1, 2]), (0, 1, [0, 3]), (1, 1, [1, 3])];
        foreach ((float x, float y, int[] planes) in corners)
        {
            Vector3 ray = view.RayDirection(x, y);
            foreach (float depth in new[] { near, pose.FarClipDistance })
            {
                Vector3 point = pose.Position + ray * (depth / Vector3.Dot(ray, forward));
                foreach (int plane in planes)
                {
                    Assert.InRange(Value(frustum[plane], point), -2e-3f, 2e-3f);
                }

                Assert.InRange(Value(frustum[depth == near ? 4 : 5], point), -2e-3f, 2e-3f);
            }
        }
        Vector3 inside = pose.Position + view.RayDirection(.5f, .5f) * 10f;
        for (int plane = 0; plane < 6; plane++)
        {
            Assert.True(Value(frustum[plane], inside) > 0, $"plane {plane}");
            Assert.InRange(frustum[plane].Normal.Length(), .9999f, 1.0001f);
        }
    }

    [Fact]
    public void A_triangle_is_excluded_only_when_all_three_corners_lie_outside_one_plane()
    {
        ClassicCameraPose pose = Pose(0f);
        var frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        Vector3 behind = pose.Position - Vector3.Normalize(pose.Forward) * 2f;
        Vector3 inside = pose.Position + Vector3.Normalize(pose.Forward) * 5f;
        Assert.True(frustum.Excludes(behind, behind + Vector3.UnitX * .1f, behind + Vector3.UnitZ * .1f));
        Assert.False(frustum.Excludes(behind, behind + Vector3.UnitX * .1f, inside));
        // Within the margin of the near plane counts as inside.
        Vector3 onNear = pose.Position + Vector3.Normalize(pose.Forward) * (.3f - ViewFrustum.CullMargin / 2);
        Assert.False(frustum.Excludes(onNear, onNear, onNear));
    }

    [Fact]
    public void One_corner_inside_a_plane_by_less_than_the_margin_keeps_the_triangle()
    {
        ClassicCameraPose pose = Pose(0f);
        var frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        Plane near = frustum[4];
        var forward = Vector3.Normalize(pose.Forward);
        // Corner a lies half a margin outside the near plane; b and c are well outside it.
        Vector3 onPlane = pose.Position + forward * .3f;
        Vector3 a = onPlane - near.Normal * (Value(near, onPlane) + ViewFrustum.CullMargin / 2);
        Vector3 b = pose.Position + forward * .1f, c = pose.Position + forward * .05f;
        Assert.False(frustum.Excludes(a, b, c));
        Vector3 a2 = pose.Position + forward * .2f;
        Assert.True(frustum.Excludes(a2, b, c));
    }

    [Fact]
    public void The_cull_margin_is_unitys_ten_thousandth_on_every_plane()
    {
        Assert.Equal(.0001f, ViewFrustum.CullMargin);
        ClassicCameraPose pose = Pose(.16f);
        var frustum = ViewFrustum.FromPose(pose, 16f / 9f, .3f);
        Vector3 inside = pose.Position + Vector3.Normalize(pose.Forward) * 10f;
        for (int index = 0; index < 6; index++)
        {
            Plane plane = frustum[index];
            Vector3 onPlane = inside - plane.Normal * Value(plane, inside);
            var t = Vector3.Normalize(Vector3.Cross(plane.Normal, MathF.Abs(plane.Normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX));
            Vector3 Outside(float distance, Vector3 offset)
            {
                return onPlane + offset - plane.Normal * distance;
            }
            // Corners 5e-5 outside stay; 5e-4 outside (between 1e-4 and 1e-3) go.
            Assert.False(frustum.Excludes(Outside(5e-5f, default), Outside(5e-5f, t * .01f), Outside(5e-5f, -t * .01f)), $"plane {index}");
            Assert.True(frustum.Excludes(Outside(5e-4f, default), Outside(5e-4f, t * .01f), Outside(5e-4f, -t * .01f)), $"plane {index}");
        }
    }

    [Fact]
    public void The_same_view_is_unity_matrix_equality_within_its_epsilon()
    {
        var a = ViewFrustum.FromPose(Pose(.1f), 1.5f, .3f);
        Matrix4x4 nudged = a.ViewProjection;
        nudged.M11 += 5e-6f;
        Assert.True(a.SameView(ViewFrustum.FromViewProjection(nudged)));
        nudged.M11 += 2e-5f;
        Assert.False(a.SameView(ViewFrustum.FromViewProjection(nudged)));
        Assert.False(a.SameView(default));
        Matrix4x4 moved = a.ViewProjection;
        moved.M34 += 1e-4f;
        Assert.False(a.SameView(ViewFrustum.FromViewProjection(moved)));
        Matrix4x4 lastRow = a.ViewProjection;
        lastRow.M43 += 1e-4f;
        Assert.False(a.SameView(ViewFrustum.FromViewProjection(lastRow)));
        foreach (int column in new[] { 0, 1, 3 })
        {
            Matrix4x4 w = a.ViewProjection;
            w[3, column] += 1e-4f;
            Assert.False(a.SameView(ViewFrustum.FromViewProjection(w)), $"w of column {column}");
        }
    }
}
