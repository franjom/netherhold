// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The terrain material twins moved out of Unity's ClassicMeshes (godot-step3-audit.md, 3c). The added light
/// is pinned against the old arithmetic (<see cref="Legacy"/>: Unity's Color sums, as floats); the rest is each
/// variant's look.</summary>
public class TerrainMaterialLookTests
{
    private static readonly ClassicMaterialRecipe Opaque = new("Brick", KmfBlend.Opaque, 0.25f);
    private static readonly ClassicMaterialRecipe Alpha = new("Glass", KmfBlend.AlphaBlend, 0f);
    private static readonly ClassicMaterialRecipe Additive = new("Glow", KmfBlend.Additive, 1f);

    [Fact]
    public void TheColoursAreDk2sBytes()
    {
        Assert.Equal((128, 128, 200), Bytes(TerrainMaterialLook.TagTint));
        Assert.Equal((63, 64, 255), Bytes(TerrainMaterialLook.TagOverlayColour));
        Assert.Equal((255, 64, 65), Bytes(TerrainMaterialLook.HoverOverlayColour));
        Assert.Equal(new Vector3(1, 0, 0), TerrainMaterialLook.RedLight);
    }

    private static (int, int, int) Bytes(Vector3 v) =>
        ((int)MathF.Round(v.X * 255f), (int)MathF.Round(v.Y * 255f), (int)MathF.Round(v.Z * 255f));

    [Theory]
    [InlineData(0f, false)]
    [InlineData(0.25f, false)]
    [InlineData(0.6f, true)]
    [InlineData(1f, true)]
    public void ATaggedTwinAddsTheTagToItsOwnLightAsUnityDid(float own, bool red) =>
        Assert.Equal(Legacy.Tagged(own, red), TerrainMaterialLook.TaggedLight(new Vector3(own), red));

    [Theory]
    [InlineData(0f, 10, 20, 30)]
    [InlineData(0.25f, 255, 0, 1)]
    [InlineData(0.5f, 300, 512, 7)]
    public void ATintedTwinAddsThePieceColourAsUnityDid(float own, int r, int g, int b) =>
        Assert.Equal(Legacy.Tinted(own, new TerrainLightColour(r, g, b)),
            TerrainMaterialLook.TintedLight(new Vector3(own), new TerrainLightColour(r, g, b)));

    [Theory]
    [InlineData(0)]
    [InlineData(96)]
    [InlineData(255)]
    public void TheFogAlphaIsThePackedByteOver255(byte packed) =>
        Assert.Equal(packed / 255f, TerrainMaterialLook.FogAlpha(packed));

    [Fact]
    public void ThePlainLookIsTheRecipesOwn()
    {
        TerrainMaterialLook look = TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Plain));
        Assert.Equal(new TerrainMaterialLook("Brick", KmfBlend.Opaque, true, false, Vector4.One, new Vector3(0.25f)), look);
        Assert.Equal(new TerrainMaterialLook("Glass", KmfBlend.AlphaBlend, false, false, Vector4.One, Vector3.Zero),
            TerrainMaterialLook.For(new TerrainMaterialKey(Alpha, TerrainMaterialVariant.Plain)));
        // Additive: full light, and its self-illumination adds nothing.
        Assert.Equal(new TerrainMaterialLook("Glow", KmfBlend.Additive, false, true, Vector4.One, Vector3.Zero),
            TerrainMaterialLook.For(new TerrainMaterialKey(Additive, TerrainMaterialVariant.Plain)));
        Assert.Equal("", TerrainMaterialLook.For(new TerrainMaterialKey(default, TerrainMaterialVariant.Plain)).Texture);
    }

    [Fact]
    public void AnEngineMaterialsOwnLightReplacesTheRecipesGrey()
    {
        var own = new Vector3(0.125f, 0.25f, 0.375f);
        var tint = new TerrainLightColour(40, 50, 60);
        TerrainMaterialLook Look(TerrainMaterialVariant variant, TerrainLightColour t = default) =>
            TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, variant) { Tint = t }, own);

        Assert.Equal(own, Look(TerrainMaterialVariant.Plain).AddedLight);
        Assert.Equal(own, Look(TerrainMaterialVariant.Fog).AddedLight);
        Assert.Equal(TerrainMaterialLook.TagTint + own, Look(TerrainMaterialVariant.Tagged).AddedLight);
        Assert.Equal(TerrainMaterialLook.RedLight + own, Look(TerrainMaterialVariant.Red).AddedLight);
        Assert.Equal(TerrainMaterialLook.TintedLight(own, tint), Look(TerrainMaterialVariant.Tinted, tint).AddedLight);
        Assert.NotEqual(own, TerrainMaterialLook.TintedLight(own, tint));
    }

    [Fact]
    public void TheTaggedRedAndTintedTwinsChangeOnlyTheAddedLight()
    {
        TerrainMaterialLook plain = TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Plain));
        var tint = new TerrainLightColour(40, 50, 60);
        Assert.Equal(plain with { AddedLight = TerrainMaterialLook.TagTint + new Vector3(0.25f) },
            TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Tagged)));
        Assert.Equal(plain with { AddedLight = new Vector3(1.25f, 0.25f, 0.25f) },
            TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Red)));
        Assert.Equal(plain with { AddedLight = new Vector3(0.25f) + (new Vector3(40, 50, 60) / 255f) },
            TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Tinted, tint)));
    }

    /// <summary>Added light leaves an additive material as it is, and a tint cut to zero adds nothing
    /// (ClassicMeshes' short cuts).</summary>
    [Fact]
    public void AnAdditiveRecipeAndAZeroTintKeepThePlainLook()
    {
        TerrainMaterialLook additive = TerrainMaterialLook.For(new TerrainMaterialKey(Additive, TerrainMaterialVariant.Plain));
        foreach (TerrainMaterialVariant variant in new[] { TerrainMaterialVariant.Tagged, TerrainMaterialVariant.Red, TerrainMaterialVariant.Tinted })
            Assert.Equal(additive, TerrainMaterialLook.For(new TerrainMaterialKey(Additive, variant, new TerrainLightColour(9, 9, 9))));
        Assert.Equal(TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Plain)),
            TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Tinted, new TerrainLightColour(256, 512, 0))));
    }

    [Fact]
    public void TheSightFogIsAnAlphaBlendedTwinWithoutDepthKeepingItsOwnLight()
    {
        Assert.Equal(new TerrainMaterialLook("Brick", KmfBlend.AlphaBlend, false, false, new Vector4(1, 1, 1, 96 / 255f), new Vector3(0.25f)),
            TerrainMaterialLook.For(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Fog, FogOpacity: 96)));
        Assert.Equal(new TerrainMaterialLook("Glow", KmfBlend.AlphaBlend, false, true, new Vector4(1, 1, 1, 1f), Vector3.Zero),
            TerrainMaterialLook.For(new TerrainMaterialKey(Additive, TerrainMaterialVariant.Fog, FogOpacity: 255)));
    }

    [Fact]
    public void TheOverlaysAreFlatAdditiveColours()
    {
        Assert.Equal(new TerrainMaterialLook("", KmfBlend.Additive, false, true, new Vector4(TerrainMaterialLook.TagOverlayColour, 1f), Vector3.Zero),
            TerrainMaterialLook.For(TerrainMaterialKey.Overlay(red: false)));
        Assert.Equal(new TerrainMaterialLook("", KmfBlend.Additive, false, true, new Vector4(TerrainMaterialLook.HoverOverlayColour, 1f), Vector3.Zero),
            TerrainMaterialLook.For(TerrainMaterialKey.Overlay(red: true)));
    }

    /// <summary>ClassicMeshes.Tagged and TerrainTinted at 0906dff6, with Unity's Color as its float channels.</summary>
    private static class Legacy
    {
        private static readonly (float R, float G, float B) TagTint = (128f / 255f, 128f / 255f, 200f / 255f);

        public static Vector3 Tagged(float own, bool red)
        {
            (float r, float g, float b) = red ? (1f, 0f, 0f) : TagTint;
            return new Vector3(r + own, g + own, b + own);
        }

        public static Vector3 Tinted(float own, TerrainLightColour tint)
        {
            tint = ClassicTerrainTint.PieceColour(tint);
            return new Vector3(own + (tint.Red / 255f), own + (tint.Green / 255f), own + (tint.Blue / 255f));
        }
    }
}
