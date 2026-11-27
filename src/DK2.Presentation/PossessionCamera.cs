// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The possessed view's camera in host axes: X east, Y up, Z north, one unit a tile.</summary>
/// <param name="RollDegrees">The roll about <paramref name="Forward"/>, between -180 and 180.</param>
public readonly record struct PossessionCameraPose(Vector3 Position, Vector3 Forward, float RollDegrees);

/// <summary>Turns <see cref="PossessionView"/>'s fixed-point place and 11-bit angles into what a host
/// camera takes, and reads camera preset 2 (possession-client.md §3). The lens and view distance
/// go through the same projection as the ordinary camera's (possession-warp.md).</summary>
public static class PossessionCamera
{
    /// <summary>The first-person preset in <c>GlobalVariables.kwd</c>.</summary>
    public const ushort PresetId = 2;

    public static PossessionCameraPose Pose(PossessionView view)
    {
        return new PossessionCameraPose(
        ToHost(view.X, view.Y, view.Z), Forward(view.Yaw, view.Pitch),
        (view.Roll >= 0x400 ? view.Roll - 0x800 : view.Roll) * (360f / 2048));
    }

    /// <summary>The whole first-person picture for a host camera: the pose, the preset's field of
    /// view and far plane, and the view axis at the screen's middle (a remake choice). The roll turns
    /// the camera about its own view axis; the side a positive roll leans to is not read, so its sign
    /// is a HYPOTHESIS.</summary>
    public static ClassicCameraPose Picture(PossessionView view, CameraThing preset, CameraViewport viewport, int? referenceScreenWidth)
    {
        PossessionCameraPose pose = Pose(view);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, pose.Forward));
        var level = Vector3.Cross(pose.Forward, right);
        float roll = pose.RollDegrees * (MathF.PI / 180);
        return new ClassicCameraPose(pose.Position, pose.Forward, level * MathF.Cos(roll) - right * MathF.Sin(roll),
            VerticalFieldOfView(preset, viewport, referenceScreenWidth), FarClipDistance(preset),
            90f - view.Pitch * (360f / 2048), view.Yaw * (360f / 2048));
    }

    /// <summary>The view direction: the heading the movement step walks along (yaw 0 is north, a
    /// quarter turn east), level at a pitch of 0x200 and down below it.</summary>
    public static Vector3 Forward(int yaw, int pitch)
    {
        double a = yaw * (Math.PI / 1024), p = pitch * (Math.PI / 1024);
        double level = Math.Sin(p);
        return new Vector3((float)(Math.Sin(a) * level), (float)-Math.Cos(p), (float)(Math.Cos(a) * level));
    }

    /// <summary>A point in 1/4096 tile, height last, in host axes (<see cref="PartyPopSound"/>'s convention).</summary>
    public static Vector3 ToHost(int x, int y, int z)
    {
        return new Vector3(x / (float)Fp32.One, z / (float)Fp32.One, -y / (float)Fp32.One);
    }

    /// <summary>The vertical field of view, in degrees, of the preset's lens, by the ordinary camera's
    /// own rule (<see cref="ClassicCamera.VerticalFieldOfView"/>): DK2 has one projection and the
    /// first-person lens is half the ordinary one, so the view is very wide. For preset 2 on DK2's
    /// 1600 by 1200 that is 123.9 degrees, 136.4 across (possession-warp.md §2).</summary>
    public static float VerticalFieldOfView(CameraThing preset, CameraViewport viewport, int? referenceScreenWidth)
    {
        return ClassicCamera.VerticalFieldOfView(preset.Lens, viewport, referenceScreenWidth);
    }

    /// <summary>The preset's view distance in tiles: 12 for preset 2.</summary>
    public static float FarClipDistance(CameraThing preset)
    {
        return preset.ViewDistance / (float)Fp32.One;
    }

    /// <summary>The near plane a host gives the possession cameras, in tiles. DK2 has none: its clipper cuts a
    /// triangle only at the four sides of the view, which meet at the eye (<c>0x0058BB60</c>,
    /// possession-warp.md §2), so a wall the creature stands against is drawn right up to the eye. An
    /// engine needs a plane; this one is a remake choice.</summary>
    public const float NearClipDistance = 0.05f;
}

/// <summary>The footfalls the possessed view's step asks for (<c>0x0040E910</c>, possession-client.md §2
/// step 14): 0x332 for the left foot and 0x333 for the right, 0x337 and 0x338 on wet ground, in the
/// creature's own category at the foot's place. The handle DK2 plays them on is unread: none is
/// given here, a remake choice, so the sound stays where the foot fell.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public static class PossessionFootsteps
{
    public const uint Left = 0x332, Right = 0x333, WetOffset = 5;

    public static SoundRequest? For(string category, in PossessionFootstep step)
    {
        return category.Length == 0 ? null
        : new SoundRequest(new SoundCategory(category.ToLowerInvariant()),
            new SoundId((step.Right ? Right : Left) + (step.Wet ? WetOffset : 0)),
            PossessionCamera.ToHost(step.X, step.Y, step.Z));
    }
}
