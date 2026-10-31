// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>How long the last sound-manager frame took: through 300 ms, 301-499 ms, 500 ms or more.</summary>
public enum SoundFrameLoad : byte { Normal, Slow, Heavy }

/// <summary>DK2's frequent-event throttle (state at <c>0x0075B470</c>). On a slow frame each of 29
/// listed events plays at most once per a rotating gap of frames; on a heavy frame they do not
/// play at all. Every other event, and every event on a normal frame, passes.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/sound-effects.md#4-slow-frame-throttle-and-unverified-playback-budgets")]
public sealed class SoundThrottle
{
    private const int Counters = 1024;

    /// <summary>The throttled events, <c>0x0066F738..0x0066F7AB</c>.</summary>
    private static readonly ushort[] Listed =
    [
        0xE5, 0x314, 0x332, 0x315, 0x333, 0x331, 0x337, 0x338, 0x147, 0x33F, 0xDC, 0x150, 0xD6, 0xDB, 0x114,
        0x1BB, 0x1BC, 0xD7, 0xD8, 0x334, 0x109, 0xF1, 0xF4, 0x110, 0xCB, 0xCC, 0xCA, 0xD0, 0x30F,
    ];

    /// <summary>The gaps, <c>0x0066F7B0</c>.</summary>
    private static readonly byte[] Gaps = [4, 1, 5, 2, 6, 3, 1, 3, 5, 4, 2, 4, 5, 2, 6, 2, 1];

    private readonly byte[] _counters = new byte[Counters];
    private uint _stamp;
    private int _rotation;

    /// <param name="nowMs">The wall clock in milliseconds; it may wrap.</param>
    public SoundThrottle(uint nowMs) => Reset(nowMs);

    public SoundFrameLoad Load { get; private set; }

    /// <summary><c>0x00567080</c>: clears every counter and marks the listed events ready. The
    /// rotation is not reset.</summary>
    public void Reset(uint nowMs)
    {
        Array.Clear(_counters, 0, Counters);
        foreach (ushort id in Listed) _counters[id] = 1;
        Load = SoundFrameLoad.Normal;
        _stamp = nowMs;
    }

    /// <summary><c>0x00566FC0</c>, at the start of the manager's update after the world frame:
    /// classifies the time since the last call (unsigned, so a clock that runs backwards is
    /// heavy) and ages the listed counters towards 1.</summary>
    public void BeginFrame(uint nowMs)
    {
        uint elapsed = nowMs - _stamp;
        Load = elapsed <= 300 ? SoundFrameLoad.Normal : elapsed < 500 ? SoundFrameLoad.Slow : SoundFrameLoad.Heavy;
        _stamp = nowMs;
        foreach (ushort id in Listed)
            if (_counters[id] > 1) _counters[id]--;
    }

    /// <summary>The gate of both request slots, <c>0x00567810</c> and <c>0x005678F0</c>.</summary>
    public bool Admit(SoundId id)
    {
        if (id.Value >= Counters || Load == SoundFrameLoad.Normal) return true;
        byte counter = _counters[id.Value];
        if (counter == 0) return true;
        if (Load == SoundFrameLoad.Heavy || counter > 1) return false;
        _rotation = _rotation + 1 == Gaps.Length ? 0 : _rotation + 1;
        _counters[id.Value] = (byte)(Gaps[_rotation] + 1);
        return true;
    }
}
