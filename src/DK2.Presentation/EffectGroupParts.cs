// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The members of a KMF group an effect flings (art type 8) as <see cref="EffectGroupPart"/>s.</summary>
public static class EffectGroupParts
{
    /// <summary>Each member with its offset in position units, truncated: the KMF's x and y are the world's and its z
    /// points down, a unit a tile (<see cref="DK2.Art.RenderMesh.ToRenderFrame"/>). HYPOTHESIS, from the mesh convention;
    /// the loader that fills DK2's part array is unread (heart-intro.md §3).</summary>
    public static EffectGroupPart[] Of(KmfGroup group)
    {
        if (group is null) throw new ArgumentNullException(nameof(group));
        var parts = new EffectGroupPart[group.Elements.Count];
        for (int i = 0; i < parts.Length; i++)
        {
            KmfGroupElement element = group.Elements[i];
            parts[i] = new EffectGroupPart(element.Name, (int)(element.Position.X * Fp32.One),
                (int)(element.Position.Y * Fp32.One), (int)(-element.Position.Z * Fp32.One));
        }
        return parts;
    }
}
