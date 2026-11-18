// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class ModernLightSelectorTests
{
    private static ClassicPointLight Torch(float x, float y, int red = 255, int green = 128, int blue = 0, int intensity = 128,
        int radius = 6144) =>
        ClassicPointLight.FromTorch(new TorchView(0, new TilePos((int)x, (int)y), (int)(x * 4096) + 2048, (int)(y * 4096) + 2048,
            6144, 0, red, green, blue, (byte)intensity, radius, default), ClassicLightFlags.NormalTorch);

    private static ClassicPointLight Terrain(int x, int y) =>
        ClassicPointLight.FromTerrain(new TilePos(x, y), new Fp32(6144), new TerrainLightColour(200, 80, 35));

    private static ClassicPointLight Effect(int step, int steps) => ClassicPointLight.FromEffect(new EffectLightView(1,
        new KwdLight(0, 0, 0, 6144, 1, 200, 160, 120, 0), new PlayerId(1), 5 * 4096, 5 * 4096, 4096, step, steps))!.Value;

    private static ModernPointLight[] Active(ModernLightSelector selector) =>
        selector.Slots.ToArray().Where(light => light.Active).ToArray();

    [Fact]
    public void FittedMappingKeepsPositionScalesRangeAndRaisesIntensityToTheFittedPower()
    {
        ClassicPointLight torch = Torch(3, 4, intensity: 112);
        ModernPointLight light = ModernLightMapping.Fitted.Map(torch, castsShadow: true);
        Assert.Equal(torch.Position, light.Position);
        Assert.Equal(torch.Radius * 0.886f, light.Range, 4);
        Assert.Equal(1.202f * MathF.Pow(112f / 128f, 2.2f), light.Energy, 4);
        Assert.True(light.CastsShadow);
        Assert.True(light.Active);
        Assert.Equal(ModernLightRole.Torch, light.Role);
        Assert.Equal(ClassicLightFlags.NormalTorch, light.Flags);
        ModernLightMapping tuned = ModernLightMapping.Fitted.Scaled(range: 2f, energy: 3f);
        Assert.Equal(torch.Radius * 0.886f * 2f, tuned.Map(torch, false).Range, 4);
        Assert.Equal(3f * light.Energy, tuned.Map(torch, false).Energy, 4);
        // The Hand's 255/128 is cut to the most intensity that becomes energy.
        ClassicPointLight hand = ClassicPointLight.FromHand(5.5f, 5.5f, false, true, 20, 20)!.Value;
        Assert.Equal(1.202f * MathF.Pow(ModernLightMapping.MaximumIntensity, 2.2f), ModernLightMapping.Fitted.Map(hand, false).Energy, 4);
    }

    [Fact]
    public void AColourWithinOneKeepsItsMagnitude()
    {
        ClassicPointLight dim = Torch(1, 1, 200, 100, 50);
        Assert.Equal(dim.Colour, ModernLightMapping.Fitted.Map(dim, false).Colour);
    }

    [Fact]
    public void AColourOverOneIsTakenAtTheTypicalFactorCutAtOneAndBroughtToAPeakOfOne()
    {
        ClassicPointLight hot = Torch(1, 1, 400, 160, 40);
        Vector3 colour = ModernLightMapping.Fitted.Map(hot, false).Colour;
        Vector3 cut = Vector3.Min(hot.Colour * ModernLightMapping.TypicalFactor, Vector3.One);
        Assert.Equal(1f, colour.X, 5);
        Assert.Equal(cut.Y / cut.X, colour.Y, 5);
        Assert.Equal(cut.Z / cut.X, colour.Z, 5);
    }

    [Fact]
    public void AFadingEffectIsDimAtItsFirstRampStep()
    {
        ModernPointLight first = ModernLightMapping.Fitted.Map(Effect(1, 16), false);
        ModernPointLight full = ModernLightMapping.Fitted.Map(Effect(16, 16), false);
        Assert.Equal(full.Energy, first.Energy);
        Assert.True(first.Colour.Length() < full.Colour.Length() / 8, $"{first.Colour} against {full.Colour}");
    }

    [Theory]
    [InlineData(ClassicLightFlags.Terrain, ModernLightRole.Terrain)]
    [InlineData(ClassicLightFlags.NormalTorch, ModernLightRole.Torch)]
    [InlineData(ClassicLightFlags.CheapTorch, ModernLightRole.Torch)]
    [InlineData(ClassicLightFlags.NormalObject, ModernLightRole.Dynamic)]
    [InlineData(ClassicLightFlags.CheapObject, ModernLightRole.Dynamic)]
    public void RolesFollowTheClassicFlags(ClassicLightFlags flags, ModernLightRole role) =>
        Assert.Equal(role, ModernLightMapping.RoleOf(flags));

    /// <summary>The documented fit: Godot's omni falloff with the fitted mapping against DK2's factor to the power 2.2,
    /// facing the light, over the intensities and radii the doc names.</summary>
    [Theory]
    [InlineData(64, 4608)]
    [InlineData(64, 10112)]
    [InlineData(96, 6144)]
    [InlineData(128, 6144)]
    [InlineData(128, 10112)]
    [InlineData(160, 7168)]
    [InlineData(160, 4608)]
    public void FittedFalloffFollowsDk2sLinearEquivalent(int intensity, int radius)
    {
        ClassicPointLight dk2 = Torch(0, 0, 255, 255, 255, intensity, radius);
        ModernPointLight godot = ModernLightMapping.Fitted.Map(dk2, false);
        double squares = 0;
        int samples = 0;
        for (float d = 0.3f; d < dk2.Radius; d += 0.05f)
        {
            Vector3 vertex = dk2.Position + new Vector3(0, -d, 0);
            float classic = dk2.ContributionAt(vertex, Vector3.UnitY).X;
            float nd = MathF.Pow(d / godot.Range, 4);
            float omni = godot.Energy * MathF.Pow(MathF.Max(1 - nd, 0), 2) * MathF.Pow(d, -ModernLightMapping.Fitted.Attenuation);
            squares += Math.Pow(Math.Min(omni, 1f) - Math.Pow(classic, 2.2), 2);
            samples++;
        }
        Assert.True(Math.Sqrt(squares / samples) < 0.08, $"RMS {Math.Sqrt(squares / samples)}");
    }

    [Fact]
    public void TerrainAndDarkSourcesAreLeftOutAndTheCapKeepsTheSourcesNearestTheFocus()
    {
        var selector = new ModernLightSelector(maxLights: 2, maxShadows: 1, ModernLightMapping.Fitted);
        ClassicPointLight[] sources =
        {
            Terrain(0, 0), Torch(9, 0), Torch(1, 0), Torch(0, 1), Torch(0, 0, 0, 0, 0), Torch(0, 0, intensity: 0),
            Torch(0, 0, radius: 0),
        };
        selector.Select(sources, new Vector3(0.5f, 0, -0.5f));
        Assert.Equal(3, selector.SourceCount);
        Assert.Equal(2, selector.Count);
        Assert.Equal(1, selector.ShadowCount);
        ModernPointLight[] active = Active(selector);
        Assert.Equal(new[] { sources[2].Position, sources[3].Position }.OrderBy(p => p.X), active.Select(l => l.Position).OrderBy(p => p.X));
    }

    [Fact]
    public void NaNAndZeroScoresGetNoSlot()
    {
        var selector = new ModernLightSelector(4, 2, ModernLightMapping.Fitted);
        // A focus at NaN scores every source NaN; one at infinity scores every source zero.
        selector.Select(new[] { Torch(1, 1), Torch(2, 2) }, new Vector3(float.NaN));
        Assert.Equal(0, selector.Count);
        selector.Select(new[] { Torch(1, 1), Torch(2, 2) }, new Vector3(float.PositiveInfinity));
        Assert.Equal(0, selector.Count);
        Assert.Equal(0, selector.SlotCount);
    }

    [Fact]
    public void ALightKeepsItsSlotWhenAnotherEntersOrLeaves()
    {
        var selector = new ModernLightSelector(4, 4, ModernLightMapping.Fitted);
        Vector3 focus = new(3.5f, 0, -0.5f);
        ClassicPointLight a = Torch(1, 0), b = Torch(3, 0), c = Torch(5, 0);
        selector.Select(new[] { a, b, c }, focus);
        int SlotOf(ClassicPointLight light)
        {
            ReadOnlySpan<ModernPointLight> slots = selector.Slots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Active && slots[i].Position == light.Position) return i;
            return -1;
        }
        int slotA = SlotOf(a), slotC = SlotOf(c);
        // A new, brighter light ahead of them in the source order, and b gone.
        selector.Select(new[] { Torch(3, 1, intensity: 255), a, c }, focus);
        Assert.Equal(slotA, SlotOf(a));
        Assert.Equal(slotC, SlotOf(c));
        // A light that moved a little keeps its slot too.
        ClassicPointLight moved = Torch(1.2f, 0);
        selector.Select(new[] { moved, c }, focus);
        Assert.Equal(slotA, SlotOf(moved));
    }

    [Fact]
    public void AShadowStaysUntilAChallengerOutscoresItByTheHysteresis()
    {
        var selector = new ModernLightSelector(4, 1, ModernLightMapping.Fitted);
        ClassicPointLight left = Torch(0, 0), right = Torch(4, 0);
        bool Shadowed(ClassicPointLight light) => Active(selector).Single(l => l.Position == light.Position).CastsShadow;
        selector.Select(new[] { left, right }, new Vector3(1.9f, 1.5f, -0.5f));
        Assert.True(Shadowed(left));
        // The focus a little past the middle: right outscores left, but not by the hysteresis.
        selector.Select(new[] { left, right }, new Vector3(2.6f, 1.5f, -0.5f));
        Assert.True(Shadowed(left));
        Assert.False(Shadowed(right));
        selector.Select(new[] { left, right }, new Vector3(2.6f, 1.5f, -0.5f));
        Assert.True(Shadowed(left));
        // Close to right: the challenger wins.
        selector.Select(new[] { left, right }, new Vector3(4.5f, 1.5f, -0.5f));
        Assert.False(Shadowed(left));
        Assert.True(Shadowed(right));
        Assert.Equal(1, selector.ShadowCount);
    }

    [Fact]
    public void EqualScoresAreBrokenByTheSourceOrder()
    {
        var selector = new ModernLightSelector(maxLights: 1, maxShadows: 0, ModernLightMapping.Fitted);
        ClassicPointLight[] sources = { Torch(2, 0), Torch(0, 0) };
        selector.Select(sources, new Vector3(1.5f, 1.5f, -0.5f));
        Assert.Equal(sources[0].Position, Active(selector)[0].Position);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheChosenSetIsTheBestByScoreAsAFullSortWouldPickIt(int seed)
    {
        var random = new Random(seed);
        var sources = new ClassicPointLight[30];
        for (int i = 0; i < sources.Length; i++)
            sources[i] = Torch(random.Next(0, 20) + 0.5f * random.Next(0, 2), random.Next(0, 20), intensity: random.Next(40, 160),
                radius: random.Next(2048, 10112));
        var focus = new Vector3(10, 0, -10);
        var selector = new ModernLightSelector(12, 0, ModernLightMapping.Fitted);
        selector.Select(sources, focus);
        Vector3[] expected = sources.Select((light, index) => (light, index))
            .OrderByDescending(entry => ModernLightSelector.Score(entry.light, focus)).ThenBy(entry => entry.index)
            .Take(12).Select(entry => entry.light.Position).OrderBy(p => p.X).ThenBy(p => p.Z).ToArray();
        Assert.Equal(expected, Active(selector).Select(l => l.Position).OrderBy(p => p.X).ThenBy(p => p.Z));
    }

    [Fact]
    public void ASpanOfSourcesGrowsTheBuffers()
    {
        var selector = new ModernLightSelector(64, 2, ModernLightMapping.Fitted);
        selector.Select(new[] { Torch(1, 1) }, Vector3.Zero);
        ClassicPointLight[] many = Enumerable.Range(0, 40).Select(i => Torch(i % 8, i / 8)).ToArray();
        selector.Select(many, new Vector3(4, 0, -2));
        Assert.Equal(40, selector.SourceCount);
        Assert.Equal(40, selector.Count);
        Assert.Equal(2, selector.ShadowCount);
    }

    [Fact]
    public void TheCacheHandsOverThePreparedCellsSourcesInOrderAndReusesTheBuffer()
    {
        var cache = new ClassicWorldLightCache(20, 20);
        cache.SetTorch(new TilePos(5, 5), Torch(5, 5));
        cache.SetTorch(new TilePos(7, 5), Torch(7, 5));
        cache.SetTerrain(new TilePos(5, 5), Terrain(5, 5));
        cache.SetTorch(new TilePos(15, 15), Torch(15, 15));
        cache.UpdateHand(ClassicPointLight.FromHand(5.5f, 6.5f, false, true, 20, 20));
        cache.PrepareFrame(new[] { new TilePos(5, 5) });
        ClassicPointLight[] buffer = Array.Empty<ClassicPointLight>();
        int count = cache.CopyPreparedSources(ref buffer);
        // The visible tile first (its terrain then its torch, static before dynamic), then its margin in preparation
        // order: the far torch is not prepared.
        Assert.Equal(4, count);
        Assert.Equal(Terrain(5, 5), buffer[0]);
        Assert.Equal(Torch(5, 5), buffer[1]);
        Assert.Contains(Torch(7, 5), buffer.Take(count));
        Assert.DoesNotContain(Torch(15, 15), buffer.Take(count));
        ClassicPointLight[] kept = buffer;
        Assert.Equal(4, cache.CopyPreparedSources(ref buffer));
        Assert.Same(kept, buffer);
    }

    [Fact]
    public void ASteadySelectionAllocatesNothing()
    {
        var cache = new ClassicWorldLightCache(20, 20);
        for (int i = 0; i < 6; i++) cache.SetTorch(new TilePos(3 + i, 5), Torch(3 + i, 5));
        TilePos[] visible = { new(5, 5), new(6, 5) };
        var selector = new ModernLightSelector(4, 2, ModernLightMapping.Fitted);
        for (int i = 0; i < 3; i++)
        {
            cache.PrepareFrame(visible);
            selector.Select(cache, new Vector3(5.5f, 0, -5.5f));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            cache.PrepareFrame(visible);
            selector.Select(cache, new Vector3(5.5f, 0, -5.5f));
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(4, selector.Count);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModernLightSelector(-1, 0, ModernLightMapping.Fitted));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModernLightSelector(2, 3, ModernLightMapping.Fitted));
        Assert.Throws<ArgumentNullException>(() =>
            new ModernLightSelector(2, 1, ModernLightMapping.Fitted).Select((ClassicWorldLightCache)null!, Vector3.Zero));
    }
}
