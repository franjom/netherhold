// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>A model's instance look, as the world shader applies it (the shape's blend, the tint added to the light of a
/// look that takes it, the alpha multiplied into the colour's, the environment pass only while unfaded), gives each
/// surface the look <see cref="ObjectMaterialLook.Tinted"/>, <see cref="ObjectMaterialLook.InstanceTinted"/> and
/// <see cref="ObjectMaterialLook.Faded"/> give it.</summary>
public sealed class ModelInstanceLookTests
{
    private static readonly ObjectMaterialLook[] Looks =
    [
        ObjectMaterialLook.Own(new ClassicMaterialRecipe("A", KmfBlend.Opaque, 0.25f)),
        ObjectMaterialLook.Own(new ClassicMaterialRecipe("B", KmfBlend.AlphaBlend, 0.5f)),
        ObjectMaterialLook.Own(new ClassicMaterialRecipe("C", KmfBlend.Additive, 1f)),
    ];

    private static readonly ObjectMaterialLook Environment = ObjectMaterialLook.EnvironmentPass(
        new KmfMaterial("m", ["A"], KmfMaterial.EnvironmentMapFlag, 0, 0.6f, "Env"))!.Value;

    private static readonly TerrainLightColour[] Tints =
        [default, new(300, 40, 0), new(256, 512, 0), new(1000, 700, 90)];

    /// <summary>The look the shader draws a surface with under <paramref name="instance"/>.</summary>
    private static ObjectMaterialLook Drawn(ObjectMaterialLook look, ModelInstanceLook instance) =>
        instance.Shape(look) with
        {
            AddedLight = look.TakesAddedLight ? look.AddedLight + instance.Tint : look.AddedLight,
            Colour = look.Colour with { W = look.Colour.W * instance.Alpha },
        };

    [Fact]
    public void AnObjectsInstanceIsItsTintedAndFadedLook()
    {
        foreach (ObjectMaterialLook look in Looks)
            foreach (TerrainLightColour tint in Tints)
                for (int alpha = 1; alpha <= 255; alpha++)
                    Assert.Equal(look.Tinted(tint).Faded((byte)alpha), Drawn(look, ModelInstanceLook.Object(tint, (byte)alpha)));
    }

    [Fact]
    public void AStructuresInstanceIsItsInstanceTintedLookAndNothingUnderAPlan()
    {
        foreach (ObjectMaterialLook look in Looks)
            for (int tint = 0; tint < 256; tint += 5)
                Assert.Equal(look.InstanceTinted((byte)tint, (byte)tint, (byte)tint),
                    Drawn(look, ModelInstanceLook.Structure((byte)tint, plan: false)));
        Assert.Equal(ModelInstanceLook.None, ModelInstanceLook.Structure(128, plan: true));
    }

    [Fact]
    public void TheEnvironmentPassDrawsOnlyWhileUnfaded()
    {
        for (int alpha = 1; alpha <= 255; alpha++)
        {
            var instance = ModelInstanceLook.Object(default, (byte)alpha);
            Assert.Equal(Environment.Faded((byte)alpha).Colour != Vector4.Zero, instance.EnvironmentDrawn);
            Assert.Equal(alpha < 255, instance.Faded);
        }
        Assert.True(ModelInstanceLook.None.EnvironmentDrawn);
        Assert.False(ModelInstanceLook.None.Faded);
    }
}
