// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The KMF group part offsets moved out of the Unity adapter's <c>ClassicMeshes.GroupParts</c> (plan step 3f).</summary>
public sealed class EffectGroupPartsTests
{
    [Fact]
    public void OffsetsAreTheOldOnesTruncatedWithTheHeightUp()
    {
        var random = new Random(11);
        var elements = new List<KmfGroupElement>();
        for (int i = 0; i < 200; i++)
            elements.Add(new KmfGroupElement("part" + i, new Vector3(Value(random), Value(random), Value(random))));
        elements.Add(new KmfGroupElement("edge", new Vector3(-0.0001f, 1.99999f, -0.5f)));
        EffectGroupPart[] parts = EffectGroupParts.Of(new KmfGroup(elements));
        Assert.Equal(elements.Count, parts.Length);
        for (int i = 0; i < elements.Count; i++)
        {
            KmfGroupElement element = elements[i];
            // The old adapter: (int)(x * One), (int)(y * One), (int)(-z * One).
            Assert.Equal(new EffectGroupPart(element.Name, (int)(element.Position.X * Fp32.One),
                (int)(element.Position.Y * Fp32.One), (int)(-element.Position.Z * Fp32.One)), parts[i]);
        }
        Assert.Equal(new EffectGroupPart("edge", 0, 8191, 2048), parts[^1]);
    }

    [Fact]
    public void AnEmptyGroupHasNoParts() => Assert.Empty(EffectGroupParts.Of(new KmfGroup([])));

    private static float Value(Random random) => (float)(random.NextDouble() * 6 - 3);
}
