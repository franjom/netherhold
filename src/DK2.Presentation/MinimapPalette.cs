// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public readonly record struct MinimapColourClass
{
    public const int Count = 0x225;

    public MinimapColourClass(ushort index)
    {
        if (index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index;
    }

    public ushort Index { get; }
    public static MinimapColourClass Unknown => new(1);
    public static MinimapColourClass Tagged => new(13);
}

/// <summary>Native opaque ARGB class banks; class 1 uses the separate rock backdrop.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/minimap-rendering.md")]
public sealed class MinimapPalette
{
    private readonly uint[] _colours = new uint[MinimapColourClass.Count];

    public MinimapPalette(ReadOnlySpan<uint> sourceArgbPixels)
    {
        if (sourceArgbPixels.Length < 37)
            throw new ArgumentException("The minimap palette needs 37 source pixels.", nameof(sourceArgbPixels));
        for (int i = 0; i < _colours.Length; i++) _colours[i] = 0xFFFFFFFF;
        _colours[0] = sourceArgbPixels[0] | 0xFF000000;
        for (int i = 1; i <= 15; i++) _colours[i + 1] = sourceArgbPixels[i] | 0xFF000000;
        for (int i = 0; i < 7; i++)
        {
            _colours[18 + i] = sourceArgbPixels[16 + i] | 0xFF000000;
            _colours[27 + i] = sourceArgbPixels[23 + i] | 0xFF000000;
            _colours[36 + i] = sourceArgbPixels[30 + i] | 0xFF000000;
        }
    }

    public ReadOnlySpan<uint> Colours => _colours;

    public bool AdvanceNeutralCycle(GameTick tick, PlayerId neutralPlayer)
    {
        if (tick.Value < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (neutralPlayer.Value > 7) throw new ArgumentOutOfRangeException(nameof(neutralPlayer));
        int phase = (int)(tick.Value % 7);
        bool changed = false;
        // Native copies the current bank, including its neutral slot on a self-copy phase.
        for (int bank = 17; bank <= 35; bank += 9)
        {
            int target = bank + neutralPlayer.Value;
            uint colour = _colours[bank + phase];
            changed |= _colours[target] != colour;
            _colours[target] = colour;
        }
        return changed;
    }
}
