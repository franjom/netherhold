// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>How one effect instance begets others (docs/research/heart-intro.md §3): a Next effect
/// is created with its parent (<c>0x00440350</c>), a death effect when the parent is removed after
/// its last element dies (<c>0x0043C041</c>, <c>0x004354F0</c>), flag 0x40 generates effects instead
/// of elements (<c>0x004386F0</c>), GenerateRandomness gates a wave, and a group art's members are
/// flung as elements at the instance's birth (<c>0x004347E0</c>).</summary>
public class EffectChainTests
{
    private const ushort First = 10, Next = 11, Death = 12, Generator = 13, Grouped = 14;
    private const ushort Sparkle = 20, Flake = 21, Part = 22;

    [Fact]
    public void TheNextEffectIsCreatedWithItsParent_AndTheDeathEffectOnlyAfterTheLastElementDies()
    {
        var model = Model(out _);
        model.Request(new ViewEffectRequest(new EffectId(First), PlayerIds.FirstKeeper, Point(8192)));
        model.AdvanceTo(new EffectTime(51));
        // First (HP 1) and Next (HP 3) each emitted a wave on the first update.
        Assert.Contains(model.Particles, p => p.Definition.EffectElementId == Sparkle);
        Assert.Contains(model.Particles, p => p.Definition.EffectElementId == Flake);
        Assert.DoesNotContain(model.Particles, p => p.Definition.EffectElementId == Part);
        // First's HP is gone but its sparkle (HP 2) lives: no death effect yet.
        model.AdvanceTo(new EffectTime(101));
        Assert.DoesNotContain(model.Particles, p => p.Definition.EffectElementId == Part);
        // The sparkle dies on the update after; First is removed and Death (HP 1, element Part) begins.
        model.AdvanceTo(new EffectTime(151));
        model.AdvanceTo(new EffectTime(201));
        ConstructionParticle part = Assert.Single(model.Particles, p => p.Definition.EffectElementId == Part);
        Assert.Equal(8192, part.HeightRaw);
    }

    [Fact]
    public void AGeneratorEffectCreatesEffectsNotElements()
    {
        var model = Model(out _);
        model.Request(new ViewEffectRequest(new EffectId(Generator), PlayerIds.FirstKeeper, Point(8192)));
        model.AdvanceTo(new EffectTime(51));
        // Generator (HP 1, flag 0x40, generates First) made a First instance, and First's own Next.
        Assert.Empty(model.Particles);
        model.AdvanceTo(new EffectTime(101));
        Assert.Contains(model.Particles, p => p.Definition.EffectElementId == Sparkle);
        Assert.Contains(model.Particles, p => p.Definition.EffectElementId == Flake);
    }

    [Fact]
    public void GenerateRandomnessLetsOnlySomeWavesThrough()
    {
        // Randomness 7 lets a wave through one update in eight: over 240 updates of a long-lived
        // effect, about 30 waves; every update emits without it.
        var model = Model(out _, randomness: 7, hp: 240);
        model.Request(new ViewEffectRequest(new EffectId(Next), PlayerIds.FirstKeeper, Point(8192)));
        int waves = 0;
        for (int call = 1; call <= 240; call++)
        {
            int before = model.Particles.Count(p => p.VisualUpdates == 0);
            model.AdvanceTo(new EffectTime(call * 50 + 1));
            if (model.Particles.Any(p => p.VisualUpdates == 0)) waves++;
        }
        Assert.InRange(waves, 12, 60);
        var every = Model(out _);
        every.Request(new ViewEffectRequest(new EffectId(Next), PlayerIds.FirstKeeper, Point(8192)));
        for (int call = 1; call <= 3; call++) every.AdvanceTo(new EffectTime(call * 50 + 1));
        Assert.Equal(3, every.Particles.Count(p => p.Definition.EffectElementId == Flake));
    }

    [Fact]
    public void AGroupArtsMembersAreFlungAtBirth_EachDrawingItsOwnMeshHalfATurnFromTheInstance()
    {
        var model = Model(out _, groupParts: name => name == "Group_test"
            ? [new EffectGroupPart("PartA", 1000, -2000, 300), new EffectGroupPart("PartB", -500, 0, 0)] : null);
        model.Request(new ViewEffectRequest(new EffectId(Grouped), PlayerIds.FirstKeeper, Point(8192),
            new Dk2Angle(100)));
        Assert.Equal(2, model.Particles.Count);
        ConstructionParticle a = model.Particles.Single(p => p.MeshEntry == "PartA");
        Assert.Equal(Part, a.Definition.EffectElementId);
        Assert.Equal((10240 + 1000, 14336 - 2000, 8192 + 300), (a.XRaw, a.YRaw, a.HeightRaw));
        Assert.Equal((100 + 0x400) & 2047, a.HeadingRaw);
        Assert.Equal("PartB", model.Particles.Single(p => p.MeshEntry == "PartB").MeshEntry);
        // The members are the instance's children: it is not removed until they die.
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(2, model.Particles.Count);
    }

    private static FpPosition3 Point(int height) => new(new(10240), new(14336), new(height));

    private static RoomConstructionParticles Model(out GameDatabase database, ushort randomness = 0,
        Func<string, IReadOnlyList<EffectGroupPart>?>? groupParts = null, ushort hp = 3)
    {
        database = Database(randomness, hp);
        return new RoomConstructionParticles(database, 7, () => new EffectTime(0),
            floorHeight: _ => new Fp32(0), groupParts: groupParts);
    }

    private static GameDatabase Database(ushort randomness, ushort nextHp = 3)
    {
        var art = new KwdArtResource("Sparkle-03g", 0, new byte[12], 3, 0, 0, 0);
        var sparkle = new EffectElementDef([])
        {
            Name = "sparkle", EffectElementId = Sparkle, ArtResource = art, MinHp = 2, MaxHp = 2,
            MinScale = 1024, MaxScale = 1024, Red = 10, Green = 255, Blue = 200
        };
        var flake = sparkle with { Name = "flake", EffectElementId = Flake, MinHp = 10, MaxHp = 10 };
        var part = sparkle with { Name = "part", EffectElementId = Part, MinHp = 10, MaxHp = 10, ArtResource = new KwdArtResource("", 0, new byte[12], 0, 0, 0, 0) };
        EffectDef Effect(ushort id, string name, ushort hp, ushort[] generates, ushort next = 0, ushort death = 0,
            uint flags = 0x80, KwdArtResource? ownArt = null, byte perTurn = 1) => new([])
        {
            Name = name, EffectId = id, ArtResource = ownArt ?? new KwdArtResource("", 0, new byte[12], 0, 0, 0, 0),
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), GenerateIds = generates, Flags = flags, GenerationType = 4,
            ElementsPerTurn = perTurn, MinHp = hp, MaxHp = hp, NextEffectId = next, DeathEffectId = death,
            GenerateRandomness = randomness,
        };
        var first = Effect(First, "first", 1, [Sparkle], next: Next, death: Death);
        var nextEffect = Effect(Next, "next", nextHp, [Flake]);
        var death = Effect(Death, "death", 1, [Part]);
        var generator = Effect(Generator, "generator", 1, [First], flags: 0x80 | 0x40);
        // Like the shipped 215: a group art and no wave of its own (ElementsPerTurn 0).
        var grouped = Effect(Grouped, "grouped", 1, [Part], ownArt: new KwdArtResource("Group_test", 0, new byte[12], 8, 0, 0, 0), perTurn: 0);
        return new([], [], [], new GlobalVariables(), effects: [first, nextEffect, death, generator, grouped],
            effectElements: [sparkle, flake, part]);
    }
}
