// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public readonly record struct DungeonHeartVisual(Fp32 PhaseStep, Fp32 VerticalScale, Fp32 DecayPhase)
{
    public EffectId ManaEffect { get; init; }

    public static DungeonHeartVisual FromHealth(int currentHealth, int maximumHealth)
    {
        int percent = maximumHealth > 0 ? (int)Math.Clamp((long)currentHealth * 100 / maximumHealth, 0, 100) : 0;
        int stage = (100 - percent) / 5;
        int ratePercent = stage <= 10 ? 50 + stage * 5 : 100 - (stage - 10) * 10;
        int fps = Math.Max(1, ratePercent * 3 / 5);
        int duration = (int)((long)(70 * Fp32.One) * 4 * Fp32.One / (fps * Fp32.One));
        int step = (int)Math.Min(Fp32.One - 1, (long)Fp32.One * Fp32.One / duration);
        int scalePercent = stage <= 10 ? 100 + stage * 5 : 150 - (stage - 10) * 12;
        return new(new Fp32(step), new Fp32(scalePercent * Fp32.One / 100),
            new Fp32(Math.Min(Fp32.One - 1, (100 - percent) * Fp32.One / 100)));
    }

    public static int TextureIndex(Fp32 phase, int textureCount)
    {
        if (textureCount <= 0) return 0;
        float rounded = (float)((double)textureCount * phase.Raw / Fp32.One + 1d / 1024 - (double)0.49999f + 12582912);
        int index = (BitConverter.SingleToInt32Bits(rounded) & 0x7FFFFF) - 0x400000;
        return Math.Clamp(index, 0, textureCount - 1);
    }
}
