// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>The button changes a <see cref="PointerScript"/> key makes on its frame.</summary>
[Flags]
public enum PointerScriptButtons : byte
{
    None = 0,
    PrimaryDown = 1,
    PrimaryUp = 2,
    SecondaryDown = 4,
    SecondaryUp = 8,
}

/// <summary>One key of a <see cref="PointerScript"/>: on frame <paramref name="Frame"/> the pointer is at
/// (<paramref name="X"/>, <paramref name="Y"/>), screen pixels from the top left, and the buttons change.</summary>
public readonly record struct PointerScriptKey(int Frame, float X, float Y, PointerScriptButtons Buttons);

/// <summary>The pointer on one frame of a script: where it is, the presses and releases on that frame, and the buttons
/// held after them.</summary>
public readonly record struct PointerScriptSample(Vector2 Position, PointerScriptButtons Changes, bool PrimaryHeld,
    bool SecondaryHeld)
{
    public bool PrimaryDown
    {
        get
        {
            return (Changes & PointerScriptButtons.PrimaryDown) != 0;
        }
    }

    public bool PrimaryUp
    {
        get
        {
            return (Changes & PointerScriptButtons.PrimaryUp) != 0;
        }
    }

    public bool SecondaryDown
    {
        get
        {
            return (Changes & PointerScriptButtons.SecondaryDown) != 0;
        }
    }

    public bool SecondaryUp
    {
        get
        {
            return (Changes & PointerScriptButtons.SecondaryUp) != 0;
        }
    }
}

/// <summary>A scripted pointer path that stands in for the OS pointer, so a capture never moves the owner's cursor (step-3
/// audit open point 10). Keys are in frame order; between two keys the pointer moves in a straight line, before the first
/// it rests at the first and after the last at the last. Text form: keys separated by <c>;</c>, each
/// <c>frame:x,y</c> with an optional <c>:buttons</c> (<see cref="LaunchArguments.ParsePointerPath"/>).</summary>
public sealed class PointerScript
{
    private readonly PointerScriptKey[] _keys;

    /// <exception cref="ArgumentException">No keys, or frames not strictly increasing.</exception>
    public PointerScript(IReadOnlyList<PointerScriptKey> keys)
    {
        if (keys is null)
        {
            throw new ArgumentNullException(nameof(keys));
        }

        if (keys.Count == 0)
        {
            throw new ArgumentException("A pointer script needs a key.", nameof(keys));
        }

        _keys = new PointerScriptKey[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Frame < 0 || (i > 0 && keys[i].Frame <= keys[i - 1].Frame))
            {
                throw new ArgumentException("Pointer script frames must be non-negative and strictly increasing.", nameof(keys));
            }

            _keys[i] = keys[i];
        }
    }

    public IReadOnlyList<PointerScriptKey> Keys
    {
        get
        {
            return _keys;
        }
    }

    /// <summary>The pointer on <paramref name="frame"/>. Allocation-free.</summary>
    public PointerScriptSample At(int frame)
    {
        bool primary = false, secondary = false;
        PointerScriptButtons changes = PointerScriptButtons.None;
        int after = 0;
        while (after < _keys.Length && _keys[after].Frame <= frame)
        {
            PointerScriptButtons buttons = _keys[after].Buttons;
            if ((buttons & PointerScriptButtons.PrimaryDown) != 0)
            {
                primary = true;
            }

            if ((buttons & PointerScriptButtons.PrimaryUp) != 0)
            {
                primary = false;
            }

            if ((buttons & PointerScriptButtons.SecondaryDown) != 0)
            {
                secondary = true;
            }

            if ((buttons & PointerScriptButtons.SecondaryUp) != 0)
            {
                secondary = false;
            }

            if (_keys[after].Frame == frame)
            {
                changes = buttons;
            }

            after++;
        }
        Vector2 position;
        if (after == 0)
        {
            position = new Vector2(_keys[0].X, _keys[0].Y);
        }
        else if (after == _keys.Length)
        {
            position = new Vector2(_keys[^1].X, _keys[^1].Y);
        }
        else
        {
            PointerScriptKey from = _keys[after - 1], to = _keys[after];
            float t = (frame - from.Frame) / (float)(to.Frame - from.Frame);
            position = new Vector2(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }
        return new PointerScriptSample(position, changes, primary, secondary);
    }
}
