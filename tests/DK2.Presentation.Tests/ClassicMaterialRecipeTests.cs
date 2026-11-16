// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The material recipe moved out of the Unity adapter's ClassicMeshes, and the key a terrain triangle group
/// draws with (godot-step3-audit.md, 3a).</summary>
public class ClassicMaterialRecipeTests
{
    private static KmfMaterial Material(uint flags, float brightness, string environment, params string[] textures) =>
        new("m", textures, flags, brightness, 0.5f, environment);

    [Fact]
    public void TheTextureIsTheFirstNamedOneElseTheEnvironmentTexture()
    {
        Assert.Equal("Brick", ClassicMaterialRecipe.FromKmf(Material(0, 0, "Env", "Brick", "Brick_DECAY1")).Texture);
        Assert.Equal("Env", ClassicMaterialRecipe.FromKmf(Material(0, 0, "Env")).Texture);
        Assert.Equal("", ClassicMaterialRecipe.FromKmf(new KmfMaterial("m", [], 0, 0, 0, null!)).Texture);
    }

    /// <summary>A texture alternative picks from the list, clamped to its ends.</summary>
    [Theory]
    [InlineData(-3, "Brick_DECAY3")]
    [InlineData(1, "Brick_DECAY2")]
    [InlineData(9, "Brick")]
    public void ATextureAlternativeIsClamped(int index, string texture) =>
        Assert.Equal(texture, ClassicMaterialRecipe.FromKmf(
            Material(0, 0, "", "Brick_DECAY3", "Brick_DECAY2", "Brick"), index).Texture);

    [Fact]
    public void NoMaterialIsOpaqueAndUntextured() =>
        Assert.Equal(new ClassicMaterialRecipe("", KmfBlend.Opaque, 0f), ClassicMaterialRecipe.FromKmf(null));

    /// <summary>Opaque writes depth; alpha-blended and additive do not, and additive adds no light of its own.</summary>
    [Fact]
    public void BlendDepthWriteAndSelfIlluminationFollowTheFlags()
    {
        ClassicMaterialRecipe opaque = ClassicMaterialRecipe.FromKmf(Material(0, 0.25f, "", "A"));
        ClassicMaterialRecipe alpha = ClassicMaterialRecipe.FromKmf(Material(KmfMaterial.TranslucentFlags, 2f, "", "A"));
        ClassicMaterialRecipe additive = ClassicMaterialRecipe.FromKmf(Material(KmfMaterial.AdditiveFlag, 0.1f, "", "A"));

        Assert.Equal((KmfBlend.Opaque, true, false, 0.25f), (opaque.Blend, opaque.WritesDepth, opaque.IsBlended, opaque.AddedLight));
        Assert.Equal((KmfBlend.AlphaBlend, false, true, 1f), (alpha.Blend, alpha.WritesDepth, alpha.IsBlended, alpha.AddedLight));
        Assert.Equal((KmfBlend.Additive, false, true, 0f), (additive.Blend, additive.WritesDepth, additive.IsBlended, additive.AddedLight));
        Assert.Equal(1f, additive.SelfIllumination);
        Assert.True(opaque.TakesAddedLight && alpha.TakesAddedLight && !additive.TakesAddedLight);
    }

    private static readonly ClassicMaterialRecipe Opaque = new("A", KmfBlend.Opaque, 0f);
    private static readonly ClassicMaterialRecipe Additive = new("B", KmfBlend.Additive, 1f);

    /// <summary>The fog's faded twin first, then red, then the tag, then the tint, as the old adapter chose.</summary>
    [Fact]
    public void TheVariantFollowsFogThenRedThenTagThenTint()
    {
        var tint = new TerrainLightColour(10, 20, 30);
        Assert.Equal(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Fog, default, 77),
            TerrainMaterialKey.For(Opaque, red: true, tagged: true, tint, 77));
        Assert.Equal(TerrainMaterialVariant.Red, TerrainMaterialKey.For(Opaque, true, true, tint, null).Variant);
        Assert.Equal(TerrainMaterialVariant.Tagged, TerrainMaterialKey.For(Opaque, false, true, tint, null).Variant);
        Assert.Equal(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Tinted, tint),
            TerrainMaterialKey.For(Opaque, false, false, tint, null));
    }

    /// <summary>A tint is cut to a piece's bytes, and a tint cut to zero is no tint.</summary>
    [Fact]
    public void ATintIsCutToAPiecesBytes()
    {
        Assert.Equal(new TerrainLightColour(44, 1, 0), TerrainMaterialKey.For(Opaque, false, false, new(300, 257, 0), null).Tint);
        Assert.Equal(new TerrainMaterialKey(Opaque, TerrainMaterialVariant.Plain),
            TerrainMaterialKey.For(Opaque, false, false, new(256, 512, 768), null));
    }

    /// <summary>Added light leaves an additive material as it is, but its fog twin is still faded.</summary>
    [Fact]
    public void AnAdditiveMaterialTakesNoAddedLight()
    {
        var plain = new TerrainMaterialKey(Additive, TerrainMaterialVariant.Plain);
        Assert.Equal(plain, TerrainMaterialKey.For(Additive, true, false, default, null));
        Assert.Equal(plain, TerrainMaterialKey.For(Additive, false, true, default, null));
        Assert.Equal(plain, TerrainMaterialKey.For(Additive, false, false, new(10, 10, 10), null));
        Assert.Equal(TerrainMaterialVariant.Fog, TerrainMaterialKey.For(Additive, false, false, default, 5).Variant);
    }

    [Fact]
    public void TheOverlayIsKeyedByItsColourAlone()
    {
        Assert.Equal(TerrainMaterialVariant.TagOverlay, TerrainMaterialKey.Overlay(false).Variant);
        Assert.Equal(TerrainMaterialVariant.RedTagOverlay, TerrainMaterialKey.Overlay(true).Variant);
        Assert.True(TerrainMaterialKey.Overlay(false).IsOverlay && TerrainMaterialKey.Overlay(true).IsOverlay);
        Assert.Equal(default, TerrainMaterialKey.Overlay(true).Recipe);
    }
}
