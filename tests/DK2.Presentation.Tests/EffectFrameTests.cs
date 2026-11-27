// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>A frame's effect requests in Unity's runner order: present, scene, advance.</summary>
public sealed class EffectFrameTests
{
    private static readonly KwdArtResource EmptyArt = new("", 0, new byte[12], 0, 0, 0, 0);

    private static EffectDrawList List()
    {
        return new(new GameDatabase([], [], [], new GlobalVariables(), effects:
        [new EffectDef([])
        {
            Name = "mana", EffectId = 320, ArtResource = EmptyArt, Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0),
            GenerateIds = [], GenerationType = 4, ElementsPerTurn = 1, MinHp = 1, MaxHp = 1, Flags = 0x8000,
        }]), 7, clock: () => default, effectVisible: _ => true, cameraVisible: _ => true, floorHeight: _ => default);
    }

    private static ViewEffectRequest Mana()
    {
        return new(new(320), PlayerIds.FirstKeeper, new FpPosition3(new(0x3000), new(0x5000), new(0x1000)));
    }

    [Fact]
    public void AFrameInOrderPassesTheScenesRequestsToTheModelAndAdvances()
    {
        EffectDrawList list = List();
        var created = new List<ViewEffectRequest>();
        list.Model.EffectCreated = created.Add;
        var frame = new EffectFrame(list, gems: null);
        for (int f = 0; f < 3; f++)
        {
            frame.AfterPresent(null!);
            frame.RequestFromScene(Mana());
            frame.RequestFromScene(Mana());
            frame.Advance([], 0.5f);
        }
        Assert.Equal(6, created.Count);
    }

    [Fact]
    public void AStepOutOfOrderThrows()
    {
        var frame = new EffectFrame(List(), gems: null);
        Assert.Throws<InvalidOperationException>(() => frame.RequestFromScene(Mana()));
        Assert.Throws<InvalidOperationException>(() => frame.Advance([], 0f));
        frame.AfterPresent(null!);
        frame.AfterPresent(null!); // a frame that never advanced is abandoned, not a wedge
        frame.Advance([], 0f);
        Assert.Throws<InvalidOperationException>(() => frame.Advance([], 0f));
    }
}
