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
/// camera takes, and reads camera preset 2 (possession-client.md §3). How DK2's renderer uses that
/// preset's lens and view distance is unread: taking them as the ordinary camera's are taken is a
/// HYPOTHESIS.</summary>
public static class PossessionCamera
{
    /// <summary>The first-person preset in <c>GlobalVariables.kwd</c>.</summary>
    public const ushort PresetId = 2;

    public static PossessionCameraPose Pose(PossessionView view) => new(
        ToHost(view.X, view.Y, view.Z), Forward(view.Yaw, view.Pitch),
        (view.Roll >= 0x400 ? view.Roll - 0x800 : view.Roll) * (360f / 2048));

    /// <summary>The view direction: the heading the movement step walks along (yaw 0 is north, a
    /// quarter turn east), level at a pitch of 0x200 and down below it.</summary>
    public static Vector3 Forward(int yaw, int pitch)
    {
        double a = yaw * (Math.PI / 1024), p = pitch * (Math.PI / 1024);
        double level = Math.Sin(p);
        return new Vector3((float)(Math.Sin(a) * level), (float)-Math.Cos(p), (float)(Math.Cos(a) * level));
    }

    /// <summary>A point in 1/4096 tile, height last, in host axes (<see cref="PartyPopSound"/>'s convention).</summary>
    public static Vector3 ToHost(int x, int y, int z) => new(x / (float)Fp32.One, z / (float)Fp32.One, -y / (float)Fp32.One);

    /// <summary>The vertical field of view, in degrees, the preset's lens gives DK2's own 640 by 480
    /// screen (a focal length of 320 pixels to a lens of one): 73.7 degrees for preset 2, 90 across.
    /// Keeping that vertical angle on a wider screen, so the sides gain, is a remake choice.</summary>
    public static float VerticalFieldOfView(CameraThing preset) =>
        ClassicCamera.VerticalFieldOfView(preset.Lens, new CameraViewport(640, 640, 480), null);

    /// <summary>The preset's view distance in tiles: 12 for preset 2.</summary>
    public static float FarClipDistance(CameraThing preset) => preset.ViewDistance / (float)Fp32.One;
}

/// <summary>The footfalls the possessed view's step asks for (<c>0x0040E910</c>, possession-client.md §2
/// step 14): 0x332 for the left foot and 0x333 for the right, 0x337 and 0x338 on wet ground, in the
/// creature's own category at the foot's place. The handle DK2 plays them on is unread: none is
/// given here, a remake choice, so the sound stays where the foot fell.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public static class PossessionFootsteps
{
    public const uint Left = 0x332, Right = 0x333, WetOffset = 5;

    public static SoundRequest? For(string category, in PossessionFootstep step) => category.Length == 0 ? null
        : new SoundRequest(new SoundCategory(category.ToLowerInvariant()),
            new SoundId((step.Right ? Right : Left) + (step.Wet ? WetOffset : 0)),
            PossessionCamera.ToHost(step.X, step.Y, step.Z));
}
