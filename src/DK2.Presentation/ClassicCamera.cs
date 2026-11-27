// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public enum ClassicCameraMode : ushort { Dungeon = 3, Overhead = 4, TopDown = 5 }

public readonly record struct CameraViewport(int ScreenWidth, int Width, int Height);

/// <param name="ProjectionCentreShift">How far above the viewport's middle the view axis meets the
/// screen, as a fraction of half the viewport height (normalised device units, up positive).</param>
public readonly record struct ClassicCameraPose(
    Vector3 Position, Vector3 Forward, Vector3 Up,
    float VerticalFieldOfView, float FarClipDistance, float PitchDegrees, float YawDegrees,
    float ProjectionCentreShift = 0);

/// <summary>Authored dungeon camera projection and controls; classic-camera.md owns the evidence.</summary>
public sealed class ClassicCamera
{
    private readonly CameraThing _profile;
    private Dk2Angle _yaw;
    internal Dk2Angle Yaw
    {
        get
        {
            return _yaw;
        }
    }

    internal bool PanLocked
    {
        get
        {
            return (_profile.Flags & 0x40) != 0;
        }
    }

    public Fp32 Zoom { get; private set; }

    /// <summary>The bottom panel's top, virtual units of 1920, expanded and collapsed (0x00417170).</summary>
    public const int ExpandedPanelTop = 0x5B4, CollapsedPanelTop = 0x6EC;

    /// <summary>Where the view axis meets the screen, as a fraction of the height from the top: the
    /// middle of the 3D viewport, which 0x00417170 ends 0x98 below the panel's top (at detail 4 and
    /// up; lower detail ends it 0x84 higher), the whole screen once that passes the bottom. Expanded
    /// that is 806 of 1920, 504 rows of 1200, matching the owner's captures (classic-camera.md §2).</summary>
    public static float ProjectionCentreFromTop(int panelTop)
    {
        return Math.Min(1920, panelTop + 0x98) / 2f / 1920f;
    }

    /// <summary>The current panel's <see cref="ProjectionCentreFromTop"/>; the expanded panel's by default.</summary>
    public float ProjectionCentre { get; set; } = ProjectionCentreFromTop(ExpandedPanelTop);

    /// <summary>When set, keeps the reference screen's camera distance and horizontal field of view
    /// across resolutions. A wide viewport crops vertically instead of extending peripheral coverage.
    /// Null keeps DK2's fixed pixel lens and resolution-dependent distance. The runner uses1600,
    /// matching the owner's native recording; see classic-camera.md section8.</summary>
    public int? ReferenceScreenWidth { get; init; }
    public float NativeViewDistance
    {
        get
        {
            return Math.Max(_profile.ViewDistance / (float)Fp32.One, 2f * (float)Zoom.ToDouble());
        }
    }

    /// <summary>The reset camera's yaw, preserved on entry to dungeon mode (classic-camera.md section4).</summary>
    public static readonly Dk2Angle LevelStartYaw = new(256);

    /// <summary>Startup command7's requested focus, before active-bound clamping (classic-camera.md section4).</summary>
    public static Vector3 LevelStartFocus(TilePos tile)
    {
        return new(tile.X + .5f, 1f, -tile.Y - .5f);
    }

    /// <summary>The level's starting focus for a viewer comes from the player record's camera
    /// tile (<see cref="LevelStartFocus(TilePos)"/>), else, with no records or none for the viewer, the map's centre.</summary>
    public static Vector3 LevelStartFocus(IReadOnlyList<PlayerDef>? players, PlayerId viewer, int mapWidth, int mapHeight)
    {
        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].PlayerId == viewer.Value)
                {
                    return LevelStartFocus(new TilePos(players[i].CameraX, players[i].CameraY));
                }
            }
        }

        return new Vector3(mapWidth / 2f, 1, -mapHeight / 2f);
    }

    public ClassicCamera(CameraThing profile, Dk2Angle? initialYaw = null)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (profile.Lens <= 0 || profile.MinZoom <= 0 || profile.MaxZoom <= profile.MinZoom)
        {
            throw new ArgumentException("A dungeon camera needs a positive lens and zoom interval.", nameof(profile));
        }

        _profile = profile;
        _yaw = initialYaw ?? new Dk2Angle(profile.AngleYaw);
        SetZoom(new Fp32(profile.Zoom));
    }

    public void SetZoom(Fp32 zoom)
    {
        Zoom = new Fp32(Math.Clamp(zoom.Raw, _profile.MinZoom, _profile.MaxZoom - 1));
    }

    public void ZoomIn()
    {
        StepZoom(242);
    }

    public void ZoomOut()
    {
        StepZoom(270);
    }

    private void StepZoom(int numerator)
    {
        if ((_profile.Flags & 0x08) == 0)
        {
            SetZoom(new Fp32((int)(((long)Zoom.Raw * numerator) >> 8)));
        }
    }

    public void Rotate(Dk2Angle delta)
    {
        if ((_profile.Flags & 1) == 0)
        {
            _yaw = new Dk2Angle((ushort)((_yaw.Value + delta.Value) & 2047));
        }
    }

    public Vector3 PanDirection(Vector2 screenDirection)
    {
        if (screenDirection.LengthSquared() > 1)
        {
            screenDirection = Vector2.Normalize(screenDirection);
        }

        float angle = -Radians(_yaw);
        float sine = (float)Math.Sin(angle), cosine = (float)Math.Cos(angle);
        return new Vector3(cosine * screenDirection.X - sine * screenDirection.Y, 0,
            sine * screenDirection.X + cosine * screenDirection.Y);
    }

    public ClassicCameraPose Pose(Vector3 focus, CameraViewport viewport)
    {
        return Pose(focus, State(focus), viewport, 0);
    }

    /// <summary>The camera's own values at <paramref name="focus"/>, as a timed move starts from them.</summary>
    public CameraFlightState State(Vector3 focus)
    {
        return new(
        (int)MathF.Round(focus.X * Fp32.One), (int)MathF.Round(-focus.Z * Fp32.One), (int)MathF.Round(focus.Y * Fp32.One),
        Zoom.Raw, _profile.Lens, _profile.ViewDistance, _yaw.Value, _profile.AnglePitch, _profile.AngleRoll);
    }

    /// <summary>Where <c>Camera_SetMode</c>'s ordinary arm (<c>0x0044AFA0</c>-<c>0x0044B0F0</c>) sends a
    /// camera coming from <paramref name="from"/>: over the same point, at the zoom the ordinary mode
    /// was left with, the yaw rounded to a multiple of 8 (0x800 for the last four, as in DK2), the
    /// preset's pitch, roll, lens and view distance. The focus height is the caller's; DK2 takes the preset's.</summary>
    public CameraFlightState Return(in CameraFlightState from, int focusHeight)
    {
        return new(
        from.X, from.Y, focusHeight, Zoom.Raw, _profile.Lens, _profile.ViewDistance,
        (from.Yaw + 4) & 0xFFF8, _profile.AnglePitch, _profile.AngleRoll);
    }

    /// <summary>Takes the yaw a timed move ended on, whatever the preset locks.</summary>
    public void Face(Dk2Angle yaw)
    {
        _yaw = new Dk2Angle((ushort)(yaw.Value & 2047));
    }

    /// <summary>The pose of any camera state. The field of view is the state's lens through DK2's one
    /// projection (possession-warp.md §2). <paramref name="firstPersonShare"/> runs from 0, this
    /// camera's own view axis above the screen's middle, to 1, the first-person one at the middle:
    /// that offset is the only difference a move between them blends.</summary>
    public ClassicCameraPose Pose(in CameraFlightState state, CameraViewport viewport, float firstPersonShare)
    {
        return Pose(
        new Vector3(state.X / (float)Fp32.One, state.Z / (float)Fp32.One, -state.Y / (float)Fp32.One), state, viewport, firstPersonShare);
    }

    private ClassicCameraPose Pose(Vector3 focus, in CameraFlightState state, CameraViewport viewport, float firstPersonShare)
    {
        if (viewport.ScreenWidth <= 0 || viewport.Width <= 0 || viewport.Height <= 0
            || viewport.Width > viewport.ScreenWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        var yaw = new Dk2Angle((ushort)(state.Yaw & 2047));
        var pitch = new Dk2Angle((ushort)(state.Pitch & 2047));
        Matrix4x4 nativeView = Matrix4x4.CreateRotationZ(-Radians(yaw))
            * Matrix4x4.CreateRotationX(-Radians(pitch))
            * Matrix4x4.CreateRotationZ(-Radians(new Dk2Angle((ushort)(state.Roll & 2047))));
        var inverseView = Matrix4x4.Transpose(nativeView);
        int framedWidth = ReferenceScreenWidth ?? viewport.ScreenWidth;
        float distance = (float)new Fp32(state.Zoom).ToDouble() * 640f / framedWidth;
        Vector3 offset = ToUnity(Vector3.TransformNormal(new Vector3(0, 0, -distance), inverseView));
        Vector3 forward = ToUnity(Vector3.TransformNormal(Vector3.UnitZ, inverseView));
        Vector3 up = ToUnity(Vector3.TransformNormal(-Vector3.UnitY, inverseView));
        float fov = VerticalFieldOfView(state.Lens, viewport, ReferenceScreenWidth);
        return new ClassicCameraPose(focus + offset, forward, up, fov,
            Math.Max(state.ViewDistance / (float)Fp32.One, 2f * (float)new Fp32(state.Zoom).ToDouble()),
            90f - pitch.Value * (360f / 2048),
            yaw.Value * (360f / 2048),
            (1f - 2f * ProjectionCentre) * (1 - firstPersonShare));
    }

    /// <summary>The vertical field of view, in degrees, of a lens. DK2's focal length is lens x 320
    /// pixels whatever the screen (<c>0x00598D3C</c>, possession-warp.md §2), so its angle grows with the
    /// resolution; given <paramref name="referenceScreenWidth"/>, the angle is the one DK2 shows on a
    /// screen that wide, kept across resolutions (a remake choice).</summary>
    public static float VerticalFieldOfView(int lens, CameraViewport viewport, int? referenceScreenWidth)
    {
        float focalPixels = (float)(new Fp32(lens) * new Fp32(320 * Fp32.One)).ToDouble();
        // Match the reference horizontal coverage before applying viewport cropping.
        if (referenceScreenWidth is int reference)
        {
            focalPixels *= viewport.ScreenWidth / (float)reference;
        }

        return (float)(2 * Math.Atan(viewport.Height / (2.0 * focalPixels)) * 180 / Math.PI);
    }

    private static float Radians(Dk2Angle angle)
    {
        return angle.Value * ((float)Math.PI / 1024);
    }

    private static Vector3 ToUnity(Vector3 engine)
    {
        return new(engine.X, -engine.Z, -engine.Y);
    }
}
