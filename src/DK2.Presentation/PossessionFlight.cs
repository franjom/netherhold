// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>A camera as DK2 holds it: the focus in 1/4096 tile (height last), the zoom the camera
/// stands back from it along its view, the lens and view distance, and the three angles, 2048 to a
/// turn.</summary>
public readonly record struct CameraFlightState(
    int X, int Y, int Z, int Zoom, int Lens, int ViewDistance, int Yaw, int Pitch, int Roll);

/// <summary>The camera's timed move between two modes (<c>Camera_SetMode</c> <c>0x0044ACE0</c> arms
/// it, <c>Camera_Update</c> <c>0x0044C875</c>-<c>0x0044C9DB</c> runs it, <c>0x0044BD70</c> lands it):
/// 2000 ms between the ordinary camera and the first-person one. Each frame every value closes the
/// share of its remaining gap that the frame is of the remaining time, in whole numbers, and the
/// angles turn the short way (possession-client.md §6).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public sealed class PossessionFlight
{
    /// <summary>The length of a move between mode classes; within one class it is 500.</summary>
    public const int BetweenClasses = 2000;

    private const int AngleMask = 0x7FF;

    private CameraFlightState _state, _target;
    private long _start, _last, _end;

    public bool Active { get; private set; }
    public CameraFlightState State
    {
        get
        {
            return _state;
        }
    }

    public CameraFlightState Target
    {
        get
        {
            return _target;
        }
    }

    /// <summary>The share of the move's time that has passed, 0 to 1.</summary>
    public float Progress
    {
        get
        {
            return !Active ? 1 : (_last - _start) / (float)(_end - _start);
        }
    }

    public void Start(in CameraFlightState from, in CameraFlightState to, long nowMilliseconds, int duration = BetweenClasses)
    {
        _state = from;
        _target = to;
        _start = _last = nowMilliseconds;
        _end = nowMilliseconds + duration;
        Active = duration > 0;
        if (!Active)
        {
            _state = to;
        }
    }

    public void Cancel()
    {
        Active = false;
    }

    /// <summary>One frame. True on the frame the move lands, when the state is the target's.</summary>
    public bool Step(long nowMilliseconds)
    {
        if (!Active)
        {
            return false;
        }

        if (nowMilliseconds >= _end)
        {
            _state = _target;
            Active = false;
            return true;
        }
        int elapsed = (int)(nowMilliseconds - _last), remaining = (int)(_end - _last);
        if (elapsed <= 0)
        {
            return false;
        }

        _last = nowMilliseconds;
        _state = new CameraFlightState(
            Toward(_state.X, _target.X, elapsed, remaining), Toward(_state.Y, _target.Y, elapsed, remaining),
            Toward(_state.Z, _target.Z, elapsed, remaining), Toward(_state.Zoom, _target.Zoom, elapsed, remaining),
            Toward(_state.Lens, _target.Lens, elapsed, remaining),
            Toward(_state.ViewDistance, _target.ViewDistance, elapsed, remaining),
            Turn(_state.Yaw, _target.Yaw, elapsed, remaining), Turn(_state.Pitch, _target.Pitch, elapsed, remaining),
            Turn(_state.Roll, _target.Roll, elapsed, remaining));
        return false;
    }

    private static int Toward(int value, int target, int elapsed, int remaining)
    {
        return unchecked((target - value) * elapsed / remaining + value);
    }

    // The gap is the 11-bit difference with its sign: at most half a turn either way.
    private static int Turn(int value, int target, int elapsed, int remaining)
    {
        return (value + (((target - value) << 21) >> 21) * elapsed / remaining) & AngleMask;
    }

    /// <summary>The first-person camera at <paramref name="view"/>: on the eye, with the preset's
    /// zoom, lens and view distance (preset 2: zoom 0). The preset's own limits on the three and the
    /// halved view distance of a creature whose byte <c>+0x36F</c> is 0x2D are not applied.</summary>
    public static CameraFlightState FirstPerson(PossessionView view, CameraThing preset)
    {
        return new CameraFlightState(
        view.X, view.Y, view.Z, preset.Zoom, preset.Lens, preset.ViewDistance, view.Yaw, view.Pitch, view.Roll);
    }
}
