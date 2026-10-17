// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later

namespace DK2.Presentation;

/// <summary>One shared torch phase, advanced once per accepted presentation update.</summary>
public sealed class ClassicTorchFlameAnimation
{
    private int _phaseRaw;

    public void Advance() => _phaseRaw = (_phaseRaw + 4096 / 20) & 4095;

    public int Frame(int storedFrames, int phaseOffsetRaw = 0)
    {
        if (storedFrames <= 0) throw new System.ArgumentOutOfRangeException(nameof(storedFrames));
        int phase = (_phaseRaw + phaseOffsetRaw) & 4095;
        return (int)((long)phase * storedFrames / 4096);
    }
}
