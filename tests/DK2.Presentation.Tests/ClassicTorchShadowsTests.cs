// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;
using DK2.Presentation;

namespace DK2.Presentation.Tests;

public class ClassicTorchShadowsTests
{
    [Fact]
    public void SelectorKeepsTheThreeNearestWithinRangeAndFadesFromTheFourth()
    {
        var selector = new ClassicShadowSourceSelector();
        selector.Begin(Vector3.Zero);
        selector.Offer(1, new(2, 0, 0));
        selector.Offer(2, new(0, 0, 1));
        selector.Offer(3, new(0, 3, 0));      // beyond 2.5 in 3D
        selector.Offer(4, new(0, 0, 1.5f));
        selector.Offer(5, new(.5f, 0, 0));
        Assert.Equal(3, selector.Count);
        Assert.Equal(5, selector.SourceKey(0));
        Assert.Equal(2, selector.SourceKey(1));
        Assert.Equal(4, selector.SourceKey(2));
        Assert.Equal(2f, selector.FadeDistance);
        Assert.Equal(2f, selector.FadeFor(4));
        Assert.Equal(0f, selector.FadeFor(1));
        Assert.Equal(0f, selector.FadeFor(3));
    }

    [Fact]
    public void SelectorFadesOverTheRangeWithThreeOrFewerAndBreaksTiesByKey()
    {
        var selector = new ClassicShadowSourceSelector();
        selector.Begin(Vector3.Zero);
        selector.Offer(9, new(1, 0, 0));
        selector.Offer(7, new(-1, 0, 0));
        Assert.Equal(2, selector.Count);
        Assert.Equal(7, selector.SourceKey(0));
        Assert.Equal(ClassicMeshShadow.MaximumDistance, selector.FadeDistance);
        selector.Begin(new(10, 0, 0));
        Assert.Equal(0, selector.Count);
        Assert.Equal(0f, selector.FadeFor(7));
    }

    [Fact]
    public void SlotsTakeTheTorchesNearestTheFocusHorizontally()
    {
        int[] keys = [10, 11, 12, 13, 14, 15];
        Vector3[] positions = [new(5, 0, 0), new(1, 9, 0), new(0, 0, 3), new(-3, 0, 0), new(30, 0, 0), new(0, 0, -1)];
        Span<int> chosen = stackalloc int[4];
        int count = ClassicTorchShadowSlots.Choose(Vector3.Zero, keys, positions, 20f, chosen);
        Assert.Equal(4, count);
        Assert.Equal([1, 5, 2, 3], chosen.ToArray());
        Assert.Equal(2, ClassicTorchShadowSlots.Choose(Vector3.Zero, keys, positions, 1.5f, chosen));
    }

    [Fact]
    public void HeldTorchesCountNearerAndReachFarther()
    {
        int[] keys = [1, 2, 3];
        Vector3[] positions = [new(2, 0, 0), new(3, 0, 0), new(15, 0, 0)];
        Span<int> chosen = stackalloc int[1];
        Assert.Equal(1, ClassicTorchShadowSlots.Choose(Vector3.Zero, keys, positions, 14f, chosen));
        Assert.Equal(0, chosen[0]);
        // Torch 2 held: 3 - 1.5 beats 2, so the map stays with it.
        Assert.Equal(1, ClassicTorchShadowSlots.Choose(Vector3.Zero, keys, positions, 14f, [2], 1.5f, chosen));
        Assert.Equal(1, chosen[0]);
        // Torch 3 held: 15 - 1.5 is within 14, beyond it without the hold.
        Span<int> two = stackalloc int[3];
        Assert.Equal(3, ClassicTorchShadowSlots.Choose(Vector3.Zero, keys, positions, 14f, [3], 1.5f, two));
        Assert.Equal(2, ClassicTorchShadowSlots.Choose(Vector3.Zero, keys, positions, 14f, two));
    }

    [Fact]
    public void StrengthIsTheProjectionsStrength()
    {
        Assert.True(ClassicMeshShadow.TryProject(new(new(0, 1, 0), .25f, 1), new(new(-1.2f, 1, 0), 2f), out var shadow));
        Assert.Equal(shadow.Strength, ClassicMeshShadow.Strength(1.2f, 2f));
        Assert.Equal(.7f, ClassicMeshShadow.Strength(0f, 2.5f));
    }
}
