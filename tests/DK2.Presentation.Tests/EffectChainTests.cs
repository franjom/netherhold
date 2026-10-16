// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

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

    [Fact]
    public void AGroupPartIsBornBelowTheFloorWithoutDying_AndTakesTheGroupRecordsScale()
    {
        // Like the shipped 215 and its element 396: the part dies at the floor (flag 1) and has no
        // scale range, and the Heart's KMF offsets put every member just below the lid's height.
        // 0x004347E0 allocates each member through 0x004352E0 and 0x00435F20, neither of which
        // tests the floor; the member's resource record carries scale 0x1000 (0x004358C0).
        var model = new RoomConstructionParticles(Database(0, partFlags: 1, partScale: 0), 7,
            () => new EffectTime(0), floorHeight: _ => new Fp32(8192),
            groupParts: name => name == "Group_test" ? [new EffectGroupPart("PartA", 0, 0, -400)] : null);
        model.Request(new ViewEffectRequest(new EffectId(Grouped), PlayerIds.FirstKeeper, Point(8192)));
        ConstructionParticle part = Assert.Single(model.Particles);
        Assert.Equal(8192 - 400, part.HeightRaw);
        Assert.Equal(4096, part.ScaleRaw);
    }

    [Theory]
    // Flag 0x800: lit at the base on creation (0x0043A1E0), then 0/4, 1/4 ... 4/4, held (0x0043A2E0).
    [InlineData(0x800u, new[] { 4, 0, 1, 2, 3, 4, 4 })]
    // Flag 0x1000: the base, then 4/4, 3/4 ... 0/4, held at 0.
    [InlineData(0x1000u, new[] { 4, 4, 3, 2, 1, 0, 0 })]
    // Flag 0x2000: half the duration each way, 0/2 to 2/2 then down to 0/2.
    [InlineData(0x2000u, new[] { 2, 0, 1, 2, 2, 1, 0 })]
    public void AnEffectsLightRampsByItsFadeDuration(uint fadeFlag, int[] shownSteps)
    {
        var model = new RoomConstructionParticles(LitDatabase(fadeFlag, fade: 4),
            7, () => new EffectTime(0), floorHeight: _ => new Fp32(0));
        model.Request(new ViewEffectRequest(new EffectId(Lit), PlayerIds.FirstKeeper, Point(8192)));
        var lights = new List<EffectLightView>();
        for (int update = 0; update < shownSteps.Length; update++)
        {
            if (update > 0) model.AdvanceTo(new EffectTime(update * 50 + 1));
            model.CollectLights(lights);
            EffectLightView light = Assert.Single(lights);
            Assert.Equal(shownSteps[update], light.RampStep);
        }
    }

    [Fact]
    public void AnEffectsLightGoesWithTheEffect_AndIsLitAtIntensity128()
    {
        // HP 1 and no elements: removed on its first update, as the shipped 208 is.
        var model = new RoomConstructionParticles(LitDatabase(0x1000, fade: 120, hp: 1, generates: false), 7,
            () => new EffectTime(0), floorHeight: _ => new Fp32(0));
        model.Request(new ViewEffectRequest(new EffectId(Lit), PlayerIds.FirstKeeper, Point(8192)));
        var lights = new List<EffectLightView>();
        model.CollectLights(lights);
        ClassicPointLight light = ClassicPointLight.FromEffect(Assert.Single(lights))!.Value;
        Assert.Equal(1f, light.Intensity);
        // Offset z 4096 over the instance; radius 10240 × 2 / 3; flag 0x1000 adds 256 to green.
        Assert.Equal(new System.Numerics.Vector3(2.5f, 3f, -3.5f), light.Position);
        Assert.Equal(6826f / 4096, light.Radius);
        Assert.Equal(new System.Numerics.Vector3(30f / 255, 400f / 255, 255f / 255), light.Colour);
        model.AdvanceTo(new EffectTime(51));
        model.CollectLights(lights);
        Assert.Empty(lights);
    }

    [Fact]
    public void ALightMadeByAWaveIsSteppedInTheSameUpdate_OneMadeByARemovalOnTheNext()
    {
        // 0x0043BFE0 updates every effect, then every light, then removes the spent effects. A
        // generator's wave (as 209 makes 210) comes before the light pass: its rising light shows 0
        // after that update. A death effect (as 224 makes 208) comes after it: it shows the base
        // colour (0x0043A1E0) until the next update steps it.
        var lights = new List<EffectLightView>();
        var waved = new RoomConstructionParticles(LitDatabase(0x800, fade: 4), 7, () => new EffectTime(0),
            floorHeight: _ => new Fp32(0));
        waved.Request(new ViewEffectRequest(new EffectId(LitGenerator), PlayerIds.FirstKeeper, Point(8192)));
        waved.AdvanceTo(new EffectTime(51));
        waved.CollectLights(lights);
        Assert.Equal(0, Assert.Single(lights).RampStep);

        var died = new RoomConstructionParticles(LitDatabase(0x800, fade: 4), 7, () => new EffectTime(0),
            floorHeight: _ => new Fp32(0));
        died.Request(new ViewEffectRequest(new EffectId(LitParent), PlayerIds.FirstKeeper, Point(8192)));
        died.AdvanceTo(new EffectTime(51));
        died.CollectLights(lights);
        Assert.Equal(4, Assert.Single(lights).RampStep);
        died.AdvanceTo(new EffectTime(101));
        died.CollectLights(lights);
        Assert.Equal(0, Assert.Single(lights).RampStep);
    }

    [InstallFact]
    public void TheHeartPlugsNinePiecesFlyOnTheirFirstUpdate_IfKmfGroupZPointsDown()
    {
        // Effect 215 at the plug's height with the floor there: every member of Group_plugbroke is
        // born just under it (its KMF z read as down, INFERRED) and its first step (element 396's
        // vz 16384..22528 less mass 1638) lifts it clear (heart-intro.md section 3).
        string install = Dk2InstallGate.Path;
        GameDatabase database = DK2.Data.GameDatabaseLoader.Load(install);
        using var art = DK2.Art.InstallArtProvider.Open(install, database);
        IReadOnlyList<EffectGroupPart>? Parts(string name) => art.TryLoadGroup(name, out DK2.Art.Kmf.KmfGroup group)
            ? group.Elements.Select(member => new EffectGroupPart(member.Name, (int)(member.Position.X * Fp32.One),
                (int)(member.Position.Y * Fp32.One), (int)(-member.Position.Z * Fp32.One))).ToList()
            : null;
        var model = new RoomConstructionParticles(database, 7, () => new EffectTime(0),
            floorHeight: _ => new Fp32(Fp32.One), groupParts: Parts);
        model.Request(new ViewEffectRequest(new EffectId(215), PlayerIds.FirstKeeper, Point(Fp32.One)));
        Assert.Equal(9, model.Particles.Count(p => p.MeshEntry != null));
        Assert.All(model.Particles, p => Assert.True(p.HeightRaw < Fp32.One));
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(9, model.Particles.Count(p => p.MeshEntry != null));
        Assert.All(model.Particles, p => Assert.True(p.HeightRaw > Fp32.One && p.ScaleRaw == Fp32.One));
    }

    private const ushort Lit = 30, LitGenerator = 31, LitParent = 32;

    private static GameDatabase LitDatabase(uint fadeFlag, ushort fade, ushort hp = 20, bool generates = true)
    {
        var sparkle = new EffectElementDef([])
        {
            Name = "sparkle", EffectElementId = Sparkle, ArtResource = new KwdArtResource("Sparkle-03g", 0, new byte[12], 3, 0, 0, 0),
            MinHp = 2, MaxHp = 2, MinScale = 1024, MaxScale = 1024,
        };
        var lit = new EffectDef([])
        {
            Name = "lit", EffectId = Lit, ArtResource = new KwdArtResource("", 0, new byte[12], 0, 0, 0, 0),
            // Effect 210's light: rgb 30,144,255, radius 10240, z 4096, flags 0x1002.
            Light = new(0, 0, 4096, 10240, 0x1002, 30, 144, 255, 0),
            GenerateIds = generates ? [Sparkle] : [], Flags = 0x80 | fadeFlag, FadeDuration = fade,
            GenerationType = 4, ElementsPerTurn = 1, MinHp = hp, MaxHp = hp,
        };
        var unlit = new EffectDef([])
        {
            Name = "unlit", ArtResource = new KwdArtResource("", 0, new byte[12], 0, 0, 0, 0),
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), GenerationType = 4, ElementsPerTurn = 1, MinHp = 1, MaxHp = 1,
            GenerateIds = [],
        };
        var generator = unlit with { Name = "lit generator", EffectId = LitGenerator, GenerateIds = [Lit], Flags = 0x80 | 0x40 };
        var parent = unlit with { Name = "lit parent", EffectId = LitParent, DeathEffectId = Lit, Flags = 0x80 };
        return new([], [], [], new GlobalVariables(), effects: [lit, generator, parent], effectElements: [sparkle]);
    }

    private static FpPosition3 Point(int height) => new(new(10240), new(14336), new(height));

    private static RoomConstructionParticles Model(out GameDatabase database, ushort randomness = 0,
        Func<string, IReadOnlyList<EffectGroupPart>?>? groupParts = null, ushort hp = 3)
    {
        database = Database(randomness, hp);
        return new RoomConstructionParticles(database, 7, () => new EffectTime(0),
            floorHeight: _ => new Fp32(0), groupParts: groupParts);
    }

    private static GameDatabase Database(ushort randomness, ushort nextHp = 3, uint partFlags = 0, int partScale = 1024)
    {
        var art = new KwdArtResource("Sparkle-03g", 0, new byte[12], 3, 0, 0, 0);
        var sparkle = new EffectElementDef([])
        {
            Name = "sparkle", EffectElementId = Sparkle, ArtResource = art, MinHp = 2, MaxHp = 2,
            MinScale = 1024, MaxScale = 1024, Red = 10, Green = 255, Blue = 200
        };
        var flake = sparkle with { Name = "flake", EffectElementId = Flake, MinHp = 10, MaxHp = 10 };
        var part = sparkle with { Name = "part", EffectElementId = Part, MinHp = 10, MaxHp = 10, ArtResource = new KwdArtResource("", 0, new byte[12], 0, 0, 0, 0),
            Flags = partFlags, MinScale = partScale, MaxScale = partScale };
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
