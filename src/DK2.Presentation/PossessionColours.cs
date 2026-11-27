// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
namespace DK2.Presentation;

/// <summary>Session events choose the Q745 ramp changes; the adapter only uploads their tables.</summary>
public sealed class PossessionColours
{
    private ScreenColourKind _own;
    private long? _entryEnd;
    public ScreenColourChange Change { get; } = new ScreenColourChange();
    private readonly ushort[] _normal = new ushort[ScreenColourTables.Length];
    private double _normalGamma = double.NaN;
    // Native ramp application has a separate enable flag whose nonzero writer is UNREAD.
    // Developer-only switch, default off; no player setting exposes it (ADR-0822).
    public bool ApplyColourChanges { get; set; }
    public IReadOnlyList<ushort> Output
    {
        get
        {
            if (ApplyColourChanges)
            {
                return Change.Current;
            }

            if (_normalGamma != Change.Gamma)
            {
                _normalGamma = Change.Gamma;
                ScreenColourTables.Build(ScreenColourKind.Normal, _normalGamma, _normal);
            }
            return _normal;
        }
    }
    public void Enter(ScreenColourKind own, long clockMilliseconds, long flyInEnds)
    {
        _own = own;
        _entryEnd = flyInEnds;
        Step(clockMilliseconds);
    }
    public void Step(long clockMilliseconds)
    {
        Change.Step(clockMilliseconds);
        if (_entryEnd is long end && clockMilliseconds >= end - 256)
        {
            _entryEnd = null;
            Change.Start(_own, clockMilliseconds, 256);
        }
    }
    public void Damage(long clockMilliseconds)
    {
        Change.Start(ScreenColourKind.Damage, clockMilliseconds, 125);
        Change.Queue(_own, 250);
    }
    public void Thaw(long clockMilliseconds)
    {
        Change.Start(_own, clockMilliseconds, 500);
    }

    public void Death(long clockMilliseconds)
    {
        _entryEnd = null;
        Change.Start(ScreenColourKind.Death, clockMilliseconds, 1500);
        Change.Queue(ScreenColourKind.Black, 1500);
    }
    public void Leave(long clockMilliseconds)
    {
        _entryEnd = null;
        Change.Start(ScreenColourKind.Normal, clockMilliseconds, 256);
    }
}
