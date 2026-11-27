// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
namespace DK2.Presentation;

/// <summary>Q745's real-time table blend and eight-entry queue. No simulation clock or state.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/screen-colour-changes.md")]
public sealed class ScreenColourChange
{
    private readonly ushort[] _current = new ushort[ScreenColourTables.Length];
    private readonly ushort[] _start = new ushort[ScreenColourTables.Length];
    private readonly ushort[] _target = new ushort[ScreenColourTables.Length];
    private readonly (ScreenColourKind Kind, int Length)[] _queue = new (ScreenColourKind, int)[8];
    private int _read, _count, _length;
    private long _startMs;
    public double Gamma { get; private set; } = 1;
    public IReadOnlyList<ushort> Current
    {
        get
        {
            return _current;
        }
    }

    public bool Running { get; private set; }
    public int QueuedCount
    {
        get
        {
            return _count - _read;
        }
    }

    public ScreenColourChange()
    {
        ScreenColourTables.Build(ScreenColourKind.Normal, Gamma, _current);
        Array.Copy(_current, _start, _current.Length);
        Array.Copy(_current, _target, _current.Length);
    }
    public void SetGamma(double gamma)
    {
        double value = Math.Clamp(gamma, 0.5, 2);
        if (Gamma == value)
        {
            return;
        }

        Gamma = value;
        Start(ScreenColourKind.Normal, 0, 0);
    }
    public void Start(ScreenColourKind kind, long clockMilliseconds, int lengthMilliseconds)
    {
        _read = _count = 0;
        Begin(kind, clockMilliseconds, lengthMilliseconds);
    }
    private void Begin(ScreenColourKind kind, long clockMilliseconds, int lengthMilliseconds, bool queued = false)
    {
        Array.Copy(_current, _start, _current.Length);
        ScreenColourTables.Build(kind, Gamma, _target);
        _startMs = clockMilliseconds;
        _length = lengthMilliseconds;
        Running = true;
        if (_length != 0 || queued)
        {
            return;
        }

        Array.Copy(_target, _current, _current.Length);
        Array.Copy(_target, _start, _start.Length);
        Running = false;
    }
    public bool Queue(ScreenColourKind kind, int lengthMilliseconds)
    {
        if (_count == _queue.Length)
        {
            return false;
        }

        _queue[_count++] = (kind, lengthMilliseconds);
        return true;
    }
    public void Step(long clockMilliseconds)
    {
        if (!Running)
        {
            return;
        }

        int weight = unchecked((int)(clockMilliseconds - _startMs) << 16) / _length;
        if (weight < 65536)
        {
            for (int i = 0; i < _current.Length; i++)
            {
                _current[i] = unchecked((ushort)((weight * _target[i] + (65536 - weight) * _start[i]) >> 16));
            }

            return;
        }
        Array.Copy(_target, _current, _current.Length);
        if (_read < _count)
        {
            (ScreenColourKind Kind, int Length) next = _queue[_read++];
            Begin(next.Kind, clockMilliseconds, next.Length, queued: true);
        }
        else
        {
            Running = false;
        }
    }
}
