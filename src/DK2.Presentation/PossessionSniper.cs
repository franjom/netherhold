// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
namespace DK2.Presentation;
/// <summary>Sniper's action 14: an immediate distance change and a 300 ms incremental lens move.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-special-abilities.md")]
public sealed class PossessionSniper(CameraThing preset)
{
    public int Lens { get; private set; } = preset.Lens;
    public int ViewDistance { get; private set; } = preset.ViewDistance;
    private int _target;
    private long _previousMilliseconds, _endMilliseconds;
    public void Reset()
    {
        Lens = preset.Lens;
        ViewDistance = preset.ViewDistance;
        _endMilliseconds = 0;
    }
    public void Toggle(Fp32 multiplier, bool currentlyOn, long clockMilliseconds)
    {
        Step(clockMilliseconds);
        _target = currentlyOn ? preset.Lens : unchecked(preset.Lens * (int)((uint)multiplier.Raw >> 12));
        if (_target != Lens)
        {
            int requested = ViewDistance + (_target > Lens ? 0xA000 : -0xA000);
            ViewDistance = Math.Clamp(requested, preset.MinViewDistance, preset.MaxViewDistance - 1);
        }
        _previousMilliseconds = clockMilliseconds;
        _endMilliseconds = clockMilliseconds + 300;
    }
    public void Step(long clockMilliseconds)
    {
        if (_endMilliseconds == 0) return;
        if (clockMilliseconds >= _endMilliseconds)
        {
            Lens = _target;
            _endMilliseconds = 0;
            return;
        }
        int elapsed = (int)(clockMilliseconds - _previousMilliseconds);
        int remaining = (int)(_endMilliseconds - _previousMilliseconds);
        Lens += unchecked((_target - Lens) * elapsed) / remaining;
        _previousMilliseconds = clockMilliseconds;
    }
}
