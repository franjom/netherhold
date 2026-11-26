// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>One key of a <see cref="CameraScript"/>: on frame <paramref name="Frame"/> the focus is
/// (<paramref name="X"/>, <paramref name="Y"/>) tiles east and south of the script's origin.</summary>
public readonly record struct CameraScriptKey(int Frame, float X, float Y);

/// <summary>A focus path keyed by frame, so a performance run renders the same views whatever its frame rate (the
/// performance harness, migration plan step 3o), as offsets from an origin (the focus the run starts at), so one script
/// suits every level. Between two keys the focus moves in a straight line; before the first it rests at the first and
/// after the last at the last, so two equal keys hold the camera still. Text form: <see cref="Parse"/>.</summary>
public sealed class CameraScript
{
    private readonly CameraScriptKey[] _keys;

    /// <exception cref="ArgumentException">No keys, or frames not strictly increasing from zero or more.</exception>
    public CameraScript(IReadOnlyList<CameraScriptKey> keys)
    {
        if (keys is null)
        {
            throw new ArgumentNullException(nameof(keys));
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException("A camera script needs a key.", nameof(keys));
        }

        _keys = new CameraScriptKey[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Frame < 0 || (i > 0 && keys[i].Frame <= keys[i - 1].Frame))
            {
                throw new ArgumentException("Camera script frames must be non-negative and strictly increasing.", nameof(keys));
            }

            _keys[i] = keys[i];
        }
    }

    public IReadOnlyList<CameraScriptKey> Keys => _keys;

    /// <summary>The frame after which the focus no longer moves.</summary>
    public int LastFrame => _keys[_keys.Length - 1].Frame;

    /// <summary>The focus on <paramref name="frame"/> from <paramref name="origin"/>, in <see cref="CameraFocus"/>'s axes
    /// (y up, z north negated) and kept inside a map <paramref name="mapWidth"/> by <paramref name="mapHeight"/> tiles.
    /// Allocation-free.</summary>
    public Vector3 FocusAt(int frame, Vector3 origin, int mapWidth, int mapHeight)
    {
        int after = 0;
        while (after < _keys.Length && _keys[after].Frame <= frame)
        {
            after++;
        }

        float x, y;
        if (after == 0)
        {
            (x, y) = (_keys[0].X, _keys[0].Y);
        }
        else if (after == _keys.Length)
        {
            (x, y) = (_keys[after - 1].X, _keys[after - 1].Y);
        }
        else
        {
            CameraScriptKey from = _keys[after - 1], to = _keys[after];
            float t = (frame - from.Frame) / (float)(to.Frame - from.Frame);
            x = from.X + (to.X - from.X) * t;
            y = from.Y + (to.Y - from.Y) * t;
        }
        CameraFocusBounds bounds = CameraFocus.Bounds(mapWidth, mapHeight);
        return new Vector3(CameraFocus.Clamp(origin.X + x, bounds.Minimum.X, bounds.Maximum.X), origin.Y,
            CameraFocus.Clamp(origin.Z - y, bounds.Minimum.Z, bounds.Maximum.Z));
    }

    /// <summary>A script from its text, or null when the text is empty or any key is malformed: keys separated by
    /// <c>;</c>, each <c>frame:x,y</c>, frames plain decimals strictly increasing, x and y finite signed invariant decimals.</summary>
    public static CameraScript? Parse(string? text)
    {
        if (text is null || text.Trim().Length == 0)
        {
            return null;
        }

        string[] entries = text.Split(';');
        var keys = new CameraScriptKey[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            string[] parts = entries[i].Trim().Split(':');
            if (parts.Length != 2)
            {
                return null;
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int frame))
            {
                return null;
            }

            if (i > 0 && frame <= keys[i - 1].Frame)
            {
                return null;
            }

            string[] xy = parts[1].Split(',');
            if (xy.Length != 2 || !Coordinate(xy[0], out float x) || !Coordinate(xy[1], out float y))
            {
                return null;
            }

            keys[i] = new CameraScriptKey(frame, x, y);
        }
        return new CameraScript(keys);
    }

    private static bool Coordinate(string text, out float value) =>
        float.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
