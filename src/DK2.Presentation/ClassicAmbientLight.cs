// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The native persisted graphics-setting word, distinct from normalized renderer RGB.</summary>
public readonly record struct ClassicAmbientSetting(int Raw)
{
    public static readonly ClassicAmbientSetting Default = new(512);
}

/// <summary>Command87's ambient-key update; see classic-lighting.md section8.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/classic-lighting.md")]
public sealed class ClassicAmbientLight
{
    public ClassicAmbientLight(ClassicAmbientSetting? initialSetting = null)
        => StoredSetting = initialSetting ?? ClassicAmbientSetting.Default;

    public ClassicAmbientSetting StoredSetting { get; private set; }
    public float Normalized { get; private set; }

    public void Increase() => Adjust(512);
    public void Decrease() => Adjust(-512);

    private void Adjust(int delta)
    {
        // The renderer starts at zero; the first key still reads the loaded setting.
        int units = Math.Max(0, Math.Min(2048, (StoredSetting.Raw >> 12) + delta));
        Normalized = units / 4096f;
        StoredSetting = new ClassicAmbientSetting(units << 12);
    }
}
