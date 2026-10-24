// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Where a camera path puts the view this frame, in Unity's axes (x east, y up,
/// z north negated), as <see cref="ClassicCameraPose"/> gives them.</summary>
/// <param name="FieldOfView">DK2's horizontal field of view, radians: the focal length is
/// <c>screenWidth / (2 tan(fov / 2))</c> pixels.</param>
/// <param name="ViewDistance">The renderer's view distance, tiles.</param>
public readonly record struct CameraPathPose(
    Vector3 Position, Vector3 Forward, Vector3 Up, float FieldOfView, float ViewDistance)
{
    /// <summary>The vertical field of view, degrees, for a drawable area of
    /// <paramref name="viewportHeight"/> pixels on a screen <paramref name="screenWidth"/> wide.</summary>
    public float VerticalFieldOfView(int screenWidth, int viewportHeight)
    {
        double focal = screenWidth / (2 * Math.Tan(FieldOfView * 0.5));
        return (float)(2 * Math.Atan(viewportHeight / (2 * focal)) * 180 / Math.PI);
    }
}

/// <summary>One scripted camera path, played as DK2's camera mode 0x12 plays it
/// (camera-paths.md section 3): 30 frames a second from a remainder of 0.5, every quantity
/// lerped between the frame and the next.</summary>
/// <remarks>DK2 counts those seconds on the wall clock and its condition 72 asks the camera
/// whether it is still on the path. Ours asks <see cref="CameraPathRules.Playing"/>, which counts
/// game ticks, so the path is placed by the drawn world's tick, at the nominal 250 ms a tick:
/// the camera and the script then agree on the tick the path ends, a slow frame cannot skip
/// the path's opening, and the next path never arrives while this one is still drawn.</remarks>
public sealed class CameraPathPlayback
{
    /// <summary>30 frames a second at <see cref="SimulationTiming.TicksPerSecond"/>.</summary>
    public const double FramesPerTick = 30d / SimulationTiming.TicksPerSecond;

    private readonly IReadOnlyList<KcsFrame> _frames;
    private readonly float _originX;
    private readonly float _originY;
    private readonly int _first;
    private int _frame;
    private float _remainder = 0.5f;

    /// <param name="tile">The action point centre the path is drawn from; <c>0x0044A370</c>
    /// adds half a tile to each coordinate.</param>
    /// <param name="startTick">The tick action 31 ran on; the world drawn after it is at
    /// <c>startTick + 1</c>, where the path is on its first frame.</param>
    public CameraPathPlayback(int pathId, IReadOnlyList<KcsFrame> frames, TilePos tile, long startTick = 0)
    {
        if (frames is null || frames.Count == 0) throw new ArgumentException("A path needs a frame.", nameof(frames));
        PathId = pathId;
        StartTick = startTick;
        _frames = frames;
        _originX = tile.X + 0.5f;
        _originY = tile.Y + 0.5f;
        _first = _frame = CameraPathRules.FirstFrame(pathId);
        Finished = _frame >= _frames.Count - 1;
    }

    public int PathId { get; }
    public long StartTick { get; }
    public bool Finished { get; private set; }

    /// <summary><c>Camera_Update</c> mode 0x12 with the elapsed time read off the drawn world:
    /// the frame is the first plus the remainder's 0.5 plus <see cref="FramesPerTick"/> a tick
    /// since <c>StartTick + 1</c>; the path ends on its last frame, which it reaches by
    /// <c>StartTick + 1 + </c><see cref="CameraPathRules.DurationTicks"/>.</summary>
    /// <param name="renderTick">The drawn world's tick plus its fraction toward the next.</param>
    public void Seek(double renderTick)
    {
        if (Finished) return;
        double position = _first + 0.5 + Math.Max(0, renderTick - (StartTick + 1)) * FramesPerTick;
        // The camera never runs backwards, as DK2's accumulator cannot.
        if (position <= _frame + _remainder) return;
        int frame = (int)position;
        _frame = frame;
        _remainder = (float)(position - frame);
        if (_frame >= _frames.Count - 1) Finished = true;
    }

    /// <summary>The renderer's camera-path branch (<c>0x00598880</c>): frame i and i + 1 lerped by
    /// the remainder, the rotation element by element and not re-normalised, the eye sent as
    /// <c>z - 1</c> with no optical-axis offset, the view distance times 0.002.</summary>
    public CameraPathPose Pose()
    {
        int i = Math.Min(_frame, _frames.Count - 1);
        KcsFrame a = _frames[i];
        KcsFrame b = _frames[Math.Min(i + 1, _frames.Count - 1)];
        float t = _remainder;

        var engine = new Vector3(
            _originX + Lerp(a.X, b.X, t), _originY + Lerp(a.Y, b.Y, t), Lerp(a.Z, b.Z, t) - 1f);
        KcsRotation ra = a.Rotation, rb = b.Rotation;
        var view = new Matrix4x4(
            Lerp(ra.M00, rb.M00, t), Lerp(ra.M01, rb.M01, t), Lerp(ra.M02, rb.M02, t), 0,
            Lerp(ra.M10, rb.M10, t), Lerp(ra.M11, rb.M11, t), Lerp(ra.M12, rb.M12, t), 0,
            Lerp(ra.M20, rb.M20, t), Lerp(ra.M21, rb.M21, t), Lerp(ra.M22, rb.M22, t), 0,
            0, 0, 0, 1);
        // As ClassicCamera.Pose: the native matrix in memory order is the row-major view, and
        // its transpose takes camera axes to the world.
        Matrix4x4 inverseView = Matrix4x4.Transpose(view);
        Vector3 forward = ToUnity(Vector3.TransformNormal(Vector3.UnitZ, inverseView));
        Vector3 up = ToUnity(Vector3.TransformNormal(-Vector3.UnitY, inverseView));
        return new CameraPathPose(ToUnity(engine), forward, up,
            Lerp(a.FieldOfView, b.FieldOfView, t), Lerp(a.ViewDistance, b.ViewDistance, t) * 0.002f);
    }

    private static float Lerp(float a, float b, float t) => (b - a) * t + a;
    private static Vector3 ToUnity(Vector3 engine) => new(engine.X, -engine.Z, -engine.Y);
}

/// <summary>The camera's counted letterbox (<c>0x0044D320</c>, camera-paths.md section 4): each
/// raise animates the drawable area over 2000 ms to the screen less an eighth of its height
/// top and bottom; the last lower animates it back.</summary>
public sealed class CinematicLetterbox
{
    public const double BlendMilliseconds = 2000;
    public const float BarFraction = 0.125f;

    private int _count;
    public int RequestCount => _count;
    private float _from;
    private float _to;
    private double _start;
    private bool _blending;

    /// <summary>The bar height, a fraction of the screen height, at <paramref name="now"/>.</summary>
    public float Bar(double now)
    {
        if (!_blending) return _to;
        double elapsed = now - _start;
        if (elapsed >= BlendMilliseconds)
        {
            _blending = false;
            return _to;
        }
        return _from + (_to - _from) * (float)(elapsed / BlendMilliseconds);
    }

    public bool Raised => _count > 0;

    public void Raise(double now)
    {
        _count++;
        BlendTo(BarFraction, now);
    }

    public void Lower(double now)
    {
        if (_count > 0) _count--;
        if (_count == 0) BlendTo(0, now);
    }

    private void BlendTo(float target, double now)
    {
        // 0x0044D320 starts no blend when the destination rect already is the target.
        if (target == _to) return;
        _from = Bar(now);
        _to = target;
        _start = now;
        _blending = true;
    }
}

/// <summary>The Mentor's subtitle queue as <c>CSpeechSystem</c> keeps it with speech audio off
/// (<c>0x00568390</c>, <c>0x005681D0</c>; camera-paths.md section 7): sixteen slots, each line
/// shown for 96 ms a character, a second's gap after one clears before the next.</summary>
public sealed class SubtitleQueue
{
    public const int Slots = 16;
    public const double MillisecondsPerCharacter = 96;
    public const double GapMilliseconds = 1000;

    private readonly Queue<(int Line, double Duration)> _queue = new();
    private double _lastCleared;
    private double _until;

    /// <summary>The level-text index on screen, or -1.</summary>
    public int Current { get; private set; } = -1;

    public void Enqueue(int line, int characters)
    {
        if (_queue.Count < Slots) _queue.Enqueue((line, characters * MillisecondsPerCharacter));
    }

    /// <summary>The skip's clear: the waiting lines and the gap's clock go (after <c>0x00568140</c>,
    /// which zeroes the subtitle count <c>+0x14C</c> and time <c>+0x15C</c>). With
    /// <paramref name="endShown"/> the line on screen goes too, as the owner sees in DK2 with
    /// speech on (2026-10-20); what the call does to the playing line is unread.</summary>
    public void Clear(bool endShown = false)
    {
        _queue.Clear();
        _lastCleared = 0;
        if (endShown) Current = -1;
    }

    public void Update(double now)
    {
        if (Current >= 0 && _until < now)
        {
            Current = -1;
            _lastCleared = now;
        }
        if (Current < 0 && _queue.Count > 0 && (_lastCleared == 0 || _lastCleared + GapMilliseconds < now))
        {
            (int line, double duration) = _queue.Dequeue();
            Current = line;
            _until = now + duration;
        }
    }
}
