// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Where the ordinary camera's focus goes, in Unity's axes (x east, y up, z north negated), as
/// <see cref="ClassicCamera.Pose(Vector3, CameraViewport)"/> takes it (migration plan step 2i).</summary>
public static class CameraFocus
{
    /// <summary>One raw fixed-point unit, in tiles: the bounds stop this short of the map's far edges.</summary>
    private const float RawUnit = 1f / Fp32.One;

    /// <summary>A tile's centre at <paramref name="focusHeight"/>, kept inside a map
    /// <paramref name="mapWidth"/> by <paramref name="mapHeight"/> tiles: the target of a glide (action 26, 0x00442650)
    /// and of the full map's left press. On the room-goto path (0x00415B30) DK2 drops an off-map point first; ours clamps.</summary>
    public static Vector3 TileCentre(TilePos tile, float focusHeight, float mapWidth, float mapHeight) => new(
        Clamp(tile.X + 0.5f, 0, mapWidth - RawUnit), focusHeight,
        Clamp(-tile.Y - 0.5f, -mapHeight + RawUnit, 0));

    /// <summary>The focus over a fixed-point map point (a creature's position), at <paramref name="focusHeight"/>.</summary>
    public static Vector3 Over(Fp32 x, Fp32 y, float focusHeight) =>
        new(x.Raw / (float)Fp32.One, focusHeight, -y.Raw / (float)Fp32.One);

    /// <summary>The ordinary camera's pan bounds on a map of that size: the focus height fixed at raw 4095, the maximum
    /// edges exclusive by one raw unit (classic-camera.md §7).</summary>
    public static CameraFocusBounds Bounds(int mapWidth, int mapHeight) => new(
        new Vector3(0, 4095f / Fp32.One, -mapHeight + RawUnit),
        new Vector3(mapWidth - RawUnit, 4095f / Fp32.One, 0));

    /// <summary>Clamps as Unity's <c>Mathf.Clamp</c> does: the minimum first, so an empty range gives the minimum, not a throw.</summary>
    internal static float Clamp(float value, float minimum, float maximum)
    {
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }
}

/// <summary>Action 26's camera glide (game action 4, <c>0x00442650</c>): the focus moves to a tile's centre, kept inside
/// the map, over <see cref="ScriptedViewDirector.GlideMilliseconds"/>; the angles are unchanged. The clock is the caller's
/// wall clock in milliseconds. Allocation-free.</summary>
public sealed class CameraGlide
{
    private Vector3 _from, _to;
    private double _start, _milliseconds;

    /// <summary>Whether a glide is still running.</summary>
    public bool Active => _milliseconds > 0;

    /// <summary>Where the last glide started goes.</summary>
    public Vector3 Target => _to;

    /// <summary>Starts a glide from <paramref name="focus"/> to <paramref name="tile"/>'s centre
    /// (<see cref="CameraFocus.TileCentre"/> at the focus's height) on a map <paramref name="mapWidth"/> by
    /// <paramref name="mapHeight"/> tiles.</summary>
    public void Start(Vector3 focus, TilePos tile, float mapWidth, float mapHeight, double nowMilliseconds)
    {
        _from = focus;
        _to = CameraFocus.TileCentre(tile, focus.Y, mapWidth, mapHeight);
        _start = nowMilliseconds;
        _milliseconds = ScriptedViewDirector.GlideMilliseconds(_to.X - _from.X, _to.Z - _from.Z);
    }

    /// <summary>Ends the glide where the focus is (a jump or a follow takes the focus over).</summary>
    public void Stop() => _milliseconds = 0;

    /// <summary>The focus at <paramref name="nowMilliseconds"/>: <paramref name="focus"/> unchanged with no glide, else
    /// the straight line from the start to the target; the glide ends on reaching it.</summary>
    public Vector3 Advance(Vector3 focus, double nowMilliseconds)
    {
        if (_milliseconds <= 0) return focus;
        double t = (nowMilliseconds - _start) / _milliseconds;
        Vector3 next = Lerp(_from, _to, (float)Math.Min(t, 1d));
        if (t >= 1d) _milliseconds = 0;
        return next;
    }

    /// <summary>Unity's <c>Vector3.Lerp</c>, term for term (the fraction clamped to [0, 1], then a + (b - a) t), so the
    /// adapters' focus matches the one Unity's runner computed.</summary>
    private static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        t = CameraFocus.Clamp(t, 0f, 1f);
        return new Vector3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }
}
