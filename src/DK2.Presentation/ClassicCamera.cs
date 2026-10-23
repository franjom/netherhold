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
    internal Dk2Angle Yaw => _yaw;
    internal bool PanLocked => (_profile.Flags & 0x40) != 0;
    public Fp32 Zoom { get; private set; }

    /// <summary>The bottom panel's top, virtual units of 1920, expanded and collapsed (0x00417170).</summary>
    public const int ExpandedPanelTop = 0x5B4, CollapsedPanelTop = 0x6EC;

    /// <summary>Where the view axis meets the screen, as a fraction of the height from the top: the
    /// middle of the 3D viewport, which 0x00417170 ends 0x98 below the panel's top (at detail 4 and
    /// up; lower detail ends it 0x84 higher), the whole screen once that passes the bottom. Expanded
    /// that is 806 of 1920, 504 rows of 1200, matching the owner's captures (classic-camera.md §2).</summary>
    public static float ProjectionCentreFromTop(int panelTop) => Math.Min(1920, panelTop + 0x98) / 2f / 1920f;

    /// <summary>The current panel's <see cref="ProjectionCentreFromTop"/>; the expanded panel's by default.</summary>
    public float ProjectionCentre { get; set; } = ProjectionCentreFromTop(ExpandedPanelTop);

    /// <summary>When set, frames every screen as DK2 frames a 4:3 screen this wide: the same camera
    /// distance and vertical view angle at any resolution, so a wider screen sees more at the sides
    /// (owner, 2026-10-20). Null keeps DK2's own rule, a fixed focal length in pixels, under which a
    /// larger screen sees ever more ground (at 2560×1440 a 97° view whose far edge is 100 tiles wide).
    /// The runner uses 1600, DK2's largest mode, which the owner plays (ADR-0522).</summary>
    public int? ReferenceScreenWidth { get; init; }
    public float NativeViewDistance => Math.Max(_profile.ViewDistance / (float)Fp32.One, 2f * (float)Zoom.ToDouble());

    /// <summary>The reset camera's yaw, preserved on entry to dungeon mode (classic-camera.md section4).</summary>
    public static readonly Dk2Angle LevelStartYaw = new(256);

    /// <summary>Startup command7's requested focus, before active-bound clamping (classic-camera.md section4).</summary>
    public static Vector3 LevelStartFocus(TilePos tile) => new(tile.X + .5f, 1f, -tile.Y - .5f);

    public ClassicCamera(CameraThing profile, Dk2Angle? initialYaw = null)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (profile.Lens <= 0 || profile.MinZoom <= 0 || profile.MaxZoom <= profile.MinZoom)
            throw new ArgumentException("A dungeon camera needs a positive lens and zoom interval.", nameof(profile));
        _profile = profile;
        _yaw = initialYaw ?? new Dk2Angle(profile.AngleYaw);
        SetZoom(new Fp32(profile.Zoom));
    }

    public void SetZoom(Fp32 zoom) =>
        Zoom = new Fp32(Math.Clamp(zoom.Raw, _profile.MinZoom, _profile.MaxZoom - 1));

    public void ZoomIn() => StepZoom(242);
    public void ZoomOut() => StepZoom(270);

    private void StepZoom(int numerator)
    {
        if ((_profile.Flags & 0x08) == 0)
            SetZoom(new Fp32((int)(((long)Zoom.Raw * numerator) >> 8)));
    }

    public void Rotate(Dk2Angle delta)
    {
        if ((_profile.Flags & 1) == 0)
            _yaw = new Dk2Angle((ushort)((_yaw.Value + delta.Value) & 2047));
    }

    public Vector3 PanDirection(Vector2 screenDirection)
    {
        if (screenDirection.LengthSquared() > 1) screenDirection = Vector2.Normalize(screenDirection);
        float angle = -Radians(_yaw);
        float sine = (float)Math.Sin(angle), cosine = (float)Math.Cos(angle);
        return new Vector3(cosine * screenDirection.X - sine * screenDirection.Y, 0,
            sine * screenDirection.X + cosine * screenDirection.Y);
    }

    public ClassicCameraPose Pose(Vector3 focus, CameraViewport viewport)
    {
        if (viewport.ScreenWidth <= 0 || viewport.Width <= 0 || viewport.Height <= 0
            || viewport.Width > viewport.ScreenWidth)
            throw new ArgumentOutOfRangeException(nameof(viewport));

        Matrix4x4 nativeView = Matrix4x4.CreateRotationZ(-Radians(_yaw))
            * Matrix4x4.CreateRotationX(-Radians(new Dk2Angle(_profile.AnglePitch)))
            * Matrix4x4.CreateRotationZ(-Radians(new Dk2Angle(_profile.AngleRoll)));
        Matrix4x4 inverseView = Matrix4x4.Transpose(nativeView);
        int framedWidth = ReferenceScreenWidth ?? viewport.ScreenWidth;
        float distance = (float)Zoom.ToDouble() * 640f / framedWidth;
        Vector3 offset = ToUnity(Vector3.TransformNormal(new Vector3(0, 0, -distance), inverseView));
        Vector3 forward = ToUnity(Vector3.TransformNormal(Vector3.UnitZ, inverseView));
        Vector3 up = ToUnity(Vector3.TransformNormal(-Vector3.UnitY, inverseView));
        float focalPixels = (float)(new Fp32(_profile.Lens) * new Fp32(320 * Fp32.One)).ToDouble();
        if (ReferenceScreenWidth is int reference)
            focalPixels *= viewport.Height / (reference * .75f);
        float fov = (float)(2 * Math.Atan(viewport.Height / (2.0 * focalPixels)) * 180 / Math.PI);
        return new ClassicCameraPose(focus + offset, forward, up, fov,
            NativeViewDistance,
            90f - new Dk2Angle(_profile.AnglePitch).Value * (360f / 2048),
            _yaw.Value * (360f / 2048),
            1f - 2f * ProjectionCentre);
    }

    private static float Radians(Dk2Angle angle) => angle.Value * ((float)Math.PI / 1024);
    private static Vector3 ToUnity(Vector3 engine) => new(engine.X, -engine.Z, -engine.Y);
}
