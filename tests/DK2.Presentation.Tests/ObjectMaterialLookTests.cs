// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The rest of the object material recipe moved out of the Unity adapter's <c>ClassicMeshes</c> (plan step 3f):
/// the environment pass, the texture phase, the fade, the plan, the instance tint and the hover twins, each pinned to what
/// the old adapter's materials did.</summary>
public sealed class ObjectMaterialLookTests
{
    private static KmfMaterial Material(uint flags, float brightness, float shininess, string environment,
        params string[] textures)
    {
        return new("m", textures, flags, brightness, shininess, environment);
    }

    private static readonly ObjectMaterialLook Opaque = ObjectMaterialLook.Own(new ClassicMaterialRecipe("A", KmfBlend.Opaque, 0.25f));
    private static readonly ObjectMaterialLook Alpha = ObjectMaterialLook.Own(new ClassicMaterialRecipe("B", KmfBlend.AlphaBlend, 0.5f));
    private static readonly ObjectMaterialLook Additive = ObjectMaterialLook.Own(new ClassicMaterialRecipe("C", KmfBlend.Additive, 1f));
    private static readonly ObjectMaterialLook Environment =
        ObjectMaterialLook.EnvironmentPass(Material(KmfMaterial.EnvironmentMapFlag, 0, 0.6f, "Env", "A"))!.Value;

    /// <summary>The old ForTexture: an additive material unlit at white, any other lit with its self-illumination added.</summary>
    [Fact]
    public void TheOwnLookIsTheRecipesMaterial()
    {
        Assert.Equal(new ObjectMaterialLook("A", KmfBlend.Opaque, true, false, Vector4.One, new Vector3(0.25f)), Opaque);
        Assert.Equal(new ObjectMaterialLook("B", KmfBlend.AlphaBlend, false, false, Vector4.One, new Vector3(0.5f)), Alpha);
        Assert.Equal(new ObjectMaterialLook("C", KmfBlend.Additive, false, true, Vector4.One, Vector3.Zero), Additive);
    }

    /// <summary>The old EnvironmentPass: an opaque environment-mapped material's texture, ONE/ONE, no depth write, lit by
    /// the thing's light times the shininess floored at 0; none for a blended material or a missing texture name.</summary>
    [Fact]
    public void TheEnvironmentPassIsAdditiveLitAndScaledByTheShininess()
    {
        Assert.Equal(new ObjectMaterialLook("Env", KmfBlend.Additive, false, false, new Vector4(0.6f, 0.6f, 0.6f, 1f),
            Vector3.Zero, EnvironmentMapped: true), Environment);
        Assert.Equal(Vector4.UnitW,
            ObjectMaterialLook.EnvironmentPass(Material(KmfMaterial.EnvironmentMapFlag, 0, -2f, "Env"))!.Value.Colour);
        Assert.Null(ObjectMaterialLook.EnvironmentPass(Material(0, 0, 0.6f, "Env", "A")));
        Assert.Null(ObjectMaterialLook.EnvironmentPass(Material(KmfMaterial.EnvironmentMapFlag | KmfMaterial.AdditiveFlag, 0, 0.6f, "Env")));
        Assert.Null(ObjectMaterialLook.EnvironmentPass(Material(KmfMaterial.EnvironmentMapFlag | KmfMaterial.MaskFlag, 0, 0.6f, "Env")));
        Assert.Null(ObjectMaterialLook.EnvironmentPass(Material(KmfMaterial.EnvironmentMapFlag, 0, 0.6f, "")));
        Assert.Null(ObjectMaterialLook.EnvironmentPass(null));
    }

    /// <summary>The old SetAlpha: 255 keeps the materials, 0 hides the model, and between, alpha-blended twins that keep
    /// writing depth; an environment pass's twin draws black and stays additive.</summary>
    [Fact]
    public void AFadeBlendsByAlphaKeepsDepthAndBlackensTheEnvironmentPass()
    {
        Assert.Equal(Opaque, Opaque.Faded(255));
        Assert.Equal(Opaque with { Blend = KmfBlend.AlphaBlend, Colour = new Vector4(1f, 1f, 1f, 100 / 255f) }, Opaque.Faded(100));
        Assert.Equal(Alpha with { WritesDepth = true, Colour = new Vector4(1f, 1f, 1f, 1 / 255f) }, Alpha.Faded(1));
        Assert.Equal(Additive with { Blend = KmfBlend.AlphaBlend, WritesDepth = true, Colour = new Vector4(1f, 1f, 1f, 254 / 255f) },
            Additive.Faded(254));
        Assert.Equal(Environment with { Colour = Vector4.Zero }, Environment.Faded(128));
        Assert.Equal(Environment, Environment.Faded(255));
        Assert.False(ObjectMaterialLook.IsDrawn(0));
        Assert.True(ObjectMaterialLook.IsDrawn(1));
    }

    /// <summary>The old Blueprint: every slot the dig tag's overlay, blue or the tutorial's red.</summary>
    [Fact]
    public void APlanIsTheTagOverlay()
    {
        Assert.Equal(new ObjectMaterialLook("", KmfBlend.Additive, false, true, new Vector4(TerrainMaterialLook.TagOverlayColour, 1f),
            Vector3.Zero), ObjectMaterialLook.Blueprint(red: false));
        Assert.Equal(new ObjectMaterialLook("", KmfBlend.Additive, false, true, new Vector4(TerrainMaterialLook.HoverOverlayColour, 1f),
            Vector3.Zero), ObjectMaterialLook.Blueprint(red: true));
    }

    /// <summary>The old TerrainTinted: the tint cut to a piece's bytes added to the material's own light, but not on an
    /// additive material, an environment pass or a tint cut to nothing.</summary>
    [Fact]
    public void ATerrainTintAddsToTheOwnLight()
    {
        var tint = new TerrainLightColour(40, 80, 120);
        Assert.Equal(Opaque with { AddedLight = TerrainMaterialLook.TintedLight(new Vector3(0.25f), tint) }, Opaque.Tinted(tint));
        Assert.Equal(Alpha with { AddedLight = TerrainMaterialLook.TintedLight(new Vector3(0.5f), tint) }, Alpha.Tinted(tint));
        Assert.Equal(Additive, Additive.Tinted(tint));
        Assert.Equal(Environment, Environment.Tinted(tint));
        Assert.Equal(Opaque, Opaque.Tinted(default));
        Assert.Equal(Opaque, Opaque.Tinted(new TerrainLightColour(256, 512, 0)));
        // A channel past a byte keeps its low byte: 300 adds 44.
        Assert.Equal(Opaque with { AddedLight = new Vector3(0.25f + 44 / 255f, 0.25f + 40 / 255f, 0.25f) },
            Opaque.Tinted(new TerrainLightColour(300, 40, 0)));
    }

    /// <summary><see cref="ObjectMaterialLook.Faded"/> replaces the colour's alpha, an engine multiplying it per drawn thing
    /// multiplies it; the two agree only because every look a model surface can fade with has a whole alpha: a KMF
    /// material's own look, its hover twin and a plan. The fade's blend is <see cref="ObjectMaterialLook.FadeShape"/>.</summary>
    [Fact]
    public void EveryLookThatFadesHasAWholeAlpha()
    {
        foreach (KmfBlend blend in new[] { KmfBlend.Opaque, KmfBlend.AlphaBlend, KmfBlend.Additive })
        {
            Assert.Equal(1f, ObjectMaterialLook.Own(new ClassicMaterialRecipe("T", blend, 0.5f)).Colour.W);
        }

        Assert.Equal(1f, ObjectMaterialLook.Blueprint(true).Colour.W);
        Assert.Equal(1f, ObjectMaterialLook.Blueprint(false).Colour.W);
        Assert.Equal(1f, ObjectMaterialLook.HoverTwin(Material(KmfMaterial.TranslucentFlags, 0.25f, 0, "", "G0", "G1"))!.Value.Colour.W);
        foreach (ObjectMaterialLook look in new[] { Opaque, Alpha, Additive })
        {
            Assert.Equal(look.Faded(100) with { Colour = look.Colour }, look.FadeShape());
            Assert.Equal(KmfBlend.AlphaBlend, look.FadeShape().Blend);
            Assert.True(look.FadeShape().WritesDepth);
        }
        Assert.Equal(Environment, Environment.FadeShape());
        Assert.Equal(1f, ObjectMaterialLook.FadeAlpha(255));
        Assert.Equal(100 / 255f, ObjectMaterialLook.FadeAlpha(100));
    }

    /// <summary>A tint split the same way: the light it adds, summed onto the own light, is <see cref="ObjectMaterialLook.Tinted"/>'s
    /// and <see cref="ObjectMaterialLook.InstanceTinted"/>'s added light to the bit, zero where they keep the look.</summary>
    [Fact]
    public void ATintIsTheOwnLightPlusItsTintLight()
    {
        foreach (TerrainLightColour tint in new[] { default, new TerrainLightColour(300, 40, 0), new TerrainLightColour(256, 512, 0),
                     new TerrainLightColour(1000, 700, 90) })
        {
            foreach (ObjectMaterialLook look in new[] { Opaque, Alpha })
            {
                Assert.Equal(look.Tinted(tint).AddedLight, look.AddedLight + ObjectMaterialLook.TintLight(tint));
            }
        }

        Assert.Equal(Vector3.Zero, ObjectMaterialLook.TintLight(new TerrainLightColour(256, 512, 0)));
        for (int v = 0; v < 256; v += 17)
        {
            Assert.Equal(Opaque.InstanceTinted((byte)v, 0, (byte)(255 - v)).AddedLight,
                Opaque.AddedLight + ObjectMaterialLook.InstanceTintLight((byte)v, 0, (byte)(255 - v)));
        }
    }

    /// <summary>The old ElementTinted and InstanceTinted: the RenderInfo tint over 255 added to the own light; an
    /// additive material and the environment pass keep theirs.</summary>
    [Fact]
    public void AnInstanceTintAddsTheTintOver255()
    {
        Assert.Equal(Opaque with { AddedLight = new Vector3(0.25f + 128 / 255f) },
            Opaque.InstanceTinted(WorldView.BuiltDoorTint, WorldView.BuiltDoorTint, WorldView.BuiltDoorTint));
        Assert.Equal(Opaque with { AddedLight = new Vector3(0.25f + 1 / 255f, 0.25f, 0.25f + 255 / 255f) }, Opaque.InstanceTinted(1, 0, 255));
        Assert.Equal(Opaque, Opaque.InstanceTinted(0, 0, 0));
        // A tint with no red still adds its green.
        Assert.Equal(Opaque with { AddedLight = new Vector3(0.25f, 0.25f + 128 / 255f, 0.25f) }, Opaque.InstanceTinted(0, 128, 0));
        Assert.Equal(Additive, Additive.InstanceTinted(128, 128, 128));
        Assert.Equal(Environment, Environment.InstanceTinted(128, 128, 128));
        Assert.Equal(ObjectMaterialLook.Blueprint(false), ObjectMaterialLook.Blueprint(false).InstanceTinted(128, 128, 128));
    }

    /// <summary>The old HoverTextures: a material's last texture, one queue later; none where that is its first.</summary>
    [Fact]
    public void AHoverTwinIsTheLastTextureOneQueueLater()
    {
        Assert.Null(ObjectMaterialLook.HoverTwin(Material(0, 0.25f, 0, "", "Gold")));
        Assert.Null(ObjectMaterialLook.HoverTwin(Material(0, 0.25f, 0, "")));
        ObjectMaterialLook twin = ObjectMaterialLook.HoverTwin(Material(KmfMaterial.TranslucentFlags, 0.25f, 0, "", "G0", "G1", "G2"))!.Value;
        Assert.Equal(new ObjectMaterialLook("G2", KmfBlend.AlphaBlend, false, false, Vector4.One, new Vector3(0.25f), QueueOffset: 1), twin);
        Assert.Equal(1, ObjectMaterialLook.HoverTwinQueueOffset);
    }

    /// <summary>The old TryBuild and ConfigureTexturePhase: a draw per submesh, the phase's texture where given, then the
    /// environment passes in submesh order after them; a submesh past the materials draws opaque and untextured.</summary>
    [Fact]
    public void AModelDrawsItsSubmeshesThenItsEnvironmentPasses()
    {
        KmfMaterial[] materials =
        [
            Material(KmfMaterial.EnvironmentMapFlag, 0.1f, 0.4f, "EnvA", "A0", "A1", "A2", "A3"),
            Material(0, 0.2f, 0, "", "B"),
            Material(KmfMaterial.EnvironmentMapFlag, 0.3f, 0.8f, "EnvC", "C"),
        ];
        var passes = new List<ObjectPass> { new(9, Opaque) };
        ObjectModelPasses.Build(materials, 4, null, passes);
        Assert.Equal(
        [
            new ObjectPass(0, ObjectMaterialLook.Own(ClassicMaterialRecipe.FromKmf(materials[0]))),
            new ObjectPass(1, ObjectMaterialLook.Own(ClassicMaterialRecipe.FromKmf(materials[1]))),
            new ObjectPass(2, ObjectMaterialLook.Own(ClassicMaterialRecipe.FromKmf(materials[2]))),
            new ObjectPass(3, ObjectMaterialLook.Own(new ClassicMaterialRecipe("", KmfBlend.Opaque, 0f))),
            new ObjectPass(0, ObjectMaterialLook.EnvironmentPass(materials[0])!.Value),
            new ObjectPass(2, ObjectMaterialLook.EnvironmentPass(materials[2])!.Value),
        ], passes);

        ObjectModelPasses.Build(materials, 3, new Fp32(Fp32.One / 2), passes);
        Assert.Equal("A2", passes[0].Look.Texture);
        Assert.Equal("B", passes[1].Look.Texture);
        Assert.Equal(5, passes.Count);
    }

    /// <summary>The old ConfigureTexturePhase: the texture <see cref="DungeonHeartVisual.TextureIndex"/> picks, the first
    /// for a material without a list, the environment texture where there is none.</summary>
    [Theory]
    [InlineData(0, "A0")]
    [InlineData(1024, "A1")]
    [InlineData(2048, "A2")]
    [InlineData(4095, "A3")]
    public void ATexturePhasePicksFromTheList(int phase, string texture)
    {
        KmfMaterial material = Material(0, 0.1f, 0, "Env", "A0", "A1", "A2", "A3");
        Assert.Equal(texture, ObjectModelPasses.TexturePhase(material, new Fp32(phase)).Texture);
        Assert.Equal(material.Texture(DungeonHeartVisual.TextureIndex(new Fp32(phase), 4)), texture);
        Assert.Equal("Env", ObjectModelPasses.TexturePhase(Material(0, 0.1f, 0, "Env"), new Fp32(phase)).Texture);
        Assert.Equal(new ClassicMaterialRecipe("", KmfBlend.Opaque, 0f), ObjectModelPasses.TexturePhase(null, new Fp32(phase)));
    }
}

internal static class KmfMaterialTestExtensions
{
    public static string Texture(this KmfMaterial material, int index)
    {
        return material.TextureNames[index];
    }
}
