// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

[Flags]
public enum ClassicCameraKeys
{
    None = 0, Left = 1, Right = 2, Up = 4, Down = 8,
    Fast = 16, TurnLeft = 32, TurnRight = 64,
}

public readonly record struct CameraFocusBounds(Vector3 Minimum, Vector3 Maximum)
{
    public Vector3 Clamp(Vector3 focus) => Vector3.Clamp(focus, Minimum, Maximum);
}

/// <summary>Ordinary-view keyboard and edge scrolling; evidence in classic-camera.md §7.</summary>
public sealed class ClassicCameraControls
{
    private readonly ClassicCamera _camera;
    private readonly int _ticksPerSecond, _scrollSpeed;
    private readonly CameraFocusBounds? _bounds;
    private int _horizontalCoast, _verticalCoast;
    private long _fractionalMillisecondTicks;
    private bool _mouseMotionArmed;
    private readonly record struct ScreenPan(int XRaw, int YRaw);

    public ClassicCameraControls(ClassicCamera camera,
        int ticksPerSecond = SimulationTiming.OriginalTicksPerGameSecond, int scrollSpeed = 10,
        CameraFocusBounds? bounds = null)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        if (scrollSpeed < 0) throw new ArgumentOutOfRangeException(nameof(scrollSpeed));
        _ticksPerSecond = ticksPerSecond;
        _scrollSpeed = scrollSpeed;
        _bounds = bounds;
    }

    public Vector3 Advance(Vector3 focus, ClassicCameraKeys keys, TimeSpan elapsed,
        ClassicCameraKeys edgeScroll = ClassicCameraKeys.None)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        long milliseconds = elapsed.Ticks / TimeSpan.TicksPerMillisecond;
        _fractionalMillisecondTicks += elapsed.Ticks % TimeSpan.TicksPerMillisecond;
        milliseconds += _fractionalMillisecondTicks / TimeSpan.TicksPerMillisecond;
        _fractionalMillisecondTicks %= TimeSpan.TicksPerMillisecond;
        int panMilliseconds = (int)Math.Min(1000, milliseconds);
        _horizontalCoast -= Math.Sign(_horizontalCoast);
        _verticalCoast -= Math.Sign(_verticalCoast);
        PanKeys(edgeScroll, fast: false);
        PanKeys(keys, (keys & ClassicCameraKeys.Fast) != 0);
        if (_horizontalCoast != 0 || _verticalCoast != 0)
            ApplyPan(new ScreenPan(
                unchecked(_scrollSpeed * _horizontalCoast * 64) / 10,
                unchecked(_scrollSpeed * _verticalCoast * 64) / 10));

        int turn = (int)(milliseconds & 2047);
        if ((keys & ClassicCameraKeys.TurnLeft) != 0) _camera.Rotate(new Dk2Angle(unchecked((ushort)-turn)));
        if ((keys & ClassicCameraKeys.TurnRight) != 0) _camera.Rotate(new Dk2Angle((ushort)turn));
        return focus;

        void ApplyPan(ScreenPan pan)
        {
            if (_camera.PanLocked) return;
            focus += TransformPan(pan);
            if (_bounds is { } bounds) focus = bounds.Clamp(focus);
        }

        void PanKeys(ClassicCameraKeys directions, bool fast)
        {
            if ((directions & ClassicCameraKeys.Left) != 0) Pan(-1, 0);
            if ((directions & ClassicCameraKeys.Right) != 0) Pan(1, 0);
            if ((directions & ClassicCameraKeys.Up) != 0) Pan(0, -1);
            if ((directions & ClassicCameraKeys.Down) != 0) Pan(0, 1);

            void Pan(int horizontal, int vertical)
            {
                int x = unchecked(_ticksPerSecond * panMilliseconds * horizontal * 512) / 1000;
                int y = unchecked(_ticksPerSecond * panMilliseconds * vertical * 512) / 1000;
                if (fast) { x = unchecked(x * 8); y = unchecked(y * 8); }
                ApplyPan(new ScreenPan(x, y));
                _horizontalCoast = Math.Clamp(_horizontalCoast + horizontal * 2, -6, 6);
                _verticalCoast = Math.Clamp(_verticalCoast + vertical * 2, -6, 6);
            }
        }
    }

    /// <summary>Native screen deltas (right/down), after host sensitivity. X takes priority over Z;
    /// the first held frame arms the motion latch. Ordinary presets lock the vertical X angle.</summary>
    public void AdvancePointer(Vector2 delta, bool rotateHeld, bool zoomHeld)
    {
        if (!rotateHeld && !zoomHeld) { _mouseMotionArmed = false; return; }
        if (!_mouseMotionArmed) { _mouseMotionArmed = true; return; }
        if (rotateHeld) _camera.Rotate(new Dk2Angle(unchecked((ushort)(-8 * (int)delta.X))));
        else if (delta.Y > 0) _camera.ZoomIn();
        else if (delta.Y < 0) _camera.ZoomOut();
    }

    private Vector3 TransformPan(ScreenPan pan)
    {
        if (_camera.PanLocked) return Vector3.Zero;
        int sine = TrapRules.Sine(_camera.Yaw.Value), cosine = TrapRules.Cosine(_camera.Yaw.Value);
        int x = unchecked(((pan.XRaw * cosine) >> 16) - ((pan.YRaw * sine) >> 16));
        int y = unchecked(((pan.YRaw * cosine) >> 16) + ((pan.XRaw * sine) >> 16));
        x = unchecked(_camera.Zoom.Raw * x) >> 13;
        y = unchecked(_camera.Zoom.Raw * y) >> 13;
        // Ordinary presets fix focus height; the fast-scroll packet's vertical bit clamps away.
        return new Vector3(x / (float)Fp32.One, 0, -y / (float)Fp32.One);
    }
}
