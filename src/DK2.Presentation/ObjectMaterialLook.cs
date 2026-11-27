// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>How one draw of an object's, structure's or torch's model looks, for any engine's material: the texture
/// times <see cref="Colour"/>, times the vertex light plus <see cref="AddedLight"/>, or at full light
/// (<see cref="FullLight"/>), as <see cref="TerrainMaterialLook"/> draws a terrain key. An environment pass samples its
/// texture at the environment-map coordinates (<see cref="EnvironmentMapped"/>); a draw <see cref="QueueOffset"/> later
/// goes after the others at the same depth. Colours are the stored bytes over 255.</summary>
public readonly record struct ObjectMaterialLook(string Texture, KmfBlend Blend, bool WritesDepth, bool FullLight,
    Vector4 Colour, Vector3 AddedLight, bool EnvironmentMapped = false, int QueueOffset = 0)
{
    /// <summary>How much later a hovered gold object's texture twin draws: one queue, so it lands on top at equal depth.</summary>
    public const int HoverTwinQueueOffset = 1;

    /// <summary>The recipe's own look: an additive recipe at full white, any other with its self-illumination added.</summary>
    public static ObjectMaterialLook Own(in ClassicMaterialRecipe recipe)
    {
        return new(recipe.Texture ?? "", recipe.Blend, recipe.WritesDepth, recipe.Blend == KmfBlend.Additive, Vector4.One,
            new Vector3(recipe.AddedLight));
    }

    /// <summary>The additive pass DK2 draws a second time over an environment-mapped opaque material's triangles
    /// (<see cref="KmfMaterial.HasEnvironmentPass"/>, <c>0x00581797</c>, <c>0x005851E0</c>): the environment texture,
    /// ONE/ONE, no depth write, lit by the thing's light times <see cref="KmfMaterial.EnvironmentPassLight"/>. Null when
    /// the material has none or names no environment texture.</summary>
    public static ObjectMaterialLook? EnvironmentPass(KmfMaterial? kmf)
    {
        if (kmf is null || !kmf.HasEnvironmentPass || string.IsNullOrEmpty(kmf.EnvironmentTextureName))
        {
            return null;
        }

        float light = kmf.EnvironmentPassLight;
        return new ObjectMaterialLook(kmf.EnvironmentTextureName, KmfBlend.Additive, false, false,
            new Vector4(light, light, light, 1f), Vector3.Zero, EnvironmentMapped: true);
    }

    /// <summary>The plan a door or trap under construction draws in every slot: DK2 swaps each material for
    /// <c>AA_BlueTexture</c> or <c>AA_RedTexture</c> (draw flag 0x100, <c>0x00580EC0</c>), the dig tag's overlay.</summary>
    public static ObjectMaterialLook Blueprint(bool red)
    {
        var overlay = TerrainMaterialLook.For(new TerrainMaterialKey(default, BlueprintOverlay(red)));
        return new ObjectMaterialLook(overlay.Texture, overlay.Blend, overlay.WritesDepth, overlay.FullLight, overlay.Colour,
            overlay.AddedLight);
    }

    /// <summary>The red pass of the Hand's hover and of a burning creature (<see cref="HoverOverlayPlan"/>): RedTexture's
    /// <see cref="TerrainMaterialLook.HoverOverlayColour"/>, additive, unlit, no depth write, as the tutorial's red plan.</summary>
    public static ObjectMaterialLook HoverOverlay()
    {
        return Blueprint(red: true);
    }

    /// <summary>The terrain overlay a plan draws as: the dig tag's, or the tutorial's red.</summary>
    public static TerrainMaterialVariant BlueprintOverlay(bool red)
    {
        return red ? TerrainMaterialVariant.RedTagOverlay : TerrainMaterialVariant.TagOverlay;
    }

    /// <summary>The material's texture at <see cref="ObjectHoverHighlight.HoverTextureIndex"/>, drawn
    /// <see cref="HoverTwinQueueOffset"/> later over a hovered gold object; null where that is the first texture.</summary>
    public static ObjectMaterialLook? HoverTwin(KmfMaterial kmf)
    {
        int index = ObjectHoverHighlight.HoverTextureIndex(kmf.TextureNames.Count);
        return index == 0 ? null : Own(ClassicMaterialRecipe.FromKmf(kmf, index)) with { QueueOffset = HoverTwinQueueOffset };
    }

    /// <summary>Whether light added on top changes it: not an environment pass, lit through its own colour, nor a
    /// full-light draw (an additive material or a plan).</summary>
    public bool TakesAddedLight
    {
        get
        {
            return !EnvironmentMapped && !FullLight;
        }
    }

    /// <summary>Lit by an object's terrain light more (<see cref="TerrainMaterialLook.TintedLight"/>); a tint cut to zero
    /// adds nothing.</summary>
    public ObjectMaterialLook Tinted(TerrainLightColour tint)
    {
        return !TakesAddedLight || ClassicTerrainTint.PieceColour(tint) == default ? this
            : this with { AddedLight = TerrainMaterialLook.TintedLight(AddedLight, tint) };
    }

    /// <summary>The light <see cref="Tinted"/> adds to a look that takes added light: the tint cut to a piece's bytes,
    /// over 255; zero for a tint cut to zero. An engine adding it per drawn thing shares one material across tints.</summary>
    public static Vector3 TintLight(TerrainLightColour tint)
    {
        TerrainLightColour piece = ClassicTerrainTint.PieceColour(tint);
        return new Vector3(piece.Red / 255f, piece.Green / 255f, piece.Blue / 255f);
    }

    /// <summary>The light <see cref="InstanceTinted"/> adds to a look that takes added light: the RGB over 255.</summary>
    public static Vector3 InstanceTintLight(byte red, byte green, byte blue)
    {
        return new(red / 255f, green / 255f, blue / 255f);
    }

    /// <summary>Lit by a RenderInfo tint more, the element's RGB over 255 (<c>0x00580480</c>, <c>0x00581BE0</c>): an
    /// effect element's tint, or a built door's grey <see cref="WorldView.BuiltDoorTint"/>.</summary>
    public ObjectMaterialLook InstanceTinted(byte red, byte green, byte blue)
    {
        return !TakesAddedLight || (red | green | blue) == 0 ? this
            : this with { AddedLight = AddedLight + InstanceTintLight(red, green, blue) };
    }

    /// <summary>Drawn at <paramref name="alpha"/> (object <c>+0x8F</c>; 255 as it is, 0 not drawn, <see cref="IsDrawn"/>):
    /// alpha-blended and still writing depth, since the Heart's Arches and Big Steps fade in as several objects that DK2
    /// sorts by triangle; an environment pass draws black, adding nothing, as a blended draw gets none (state bits 0x21,
    /// <c>0x00581797</c>).</summary>
    public ObjectMaterialLook Faded(byte alpha)
    {
        if (alpha == byte.MaxValue)
        {
            return this;
        }

        if (EnvironmentMapped)
        {
            return this with { Colour = Vector4.Zero };
        }

        return FadeShape() with { Colour = Colour with { W = FadeAlpha(alpha) } };
    }

    /// <summary>The look <see cref="Faded"/> draws with at any alpha below 255, its colour's alpha kept: alpha-blended,
    /// writing depth. An engine multiplying the fade's alpha (<see cref="FadeAlpha"/>) per drawn thing shares one
    /// material across alphas. An environment pass keeps its look; faded, it draws black.</summary>
    public ObjectMaterialLook FadeShape()
    {
        return EnvironmentMapped ? this : this with { Blend = KmfBlend.AlphaBlend, WritesDepth = true };
    }

    /// <summary>A fade's alpha as <see cref="Faded"/> writes it into the colour.</summary>
    public static float FadeAlpha(byte alpha)
    {
        return alpha / 255f;
    }

    /// <summary>Whether a model at <paramref name="alpha"/> draws at all.</summary>
    public static bool IsDrawn(byte alpha)
    {
        return alpha > 0;
    }
}

/// <summary>One draw of a model: the submesh it draws and how it looks.</summary>
public readonly record struct ObjectPass(int Submesh, ObjectMaterialLook Look);

/// <summary>The draws of a model, for any engine to build its materials from.</summary>
public static class ObjectModelPasses
{
    /// <summary>One draw per submesh with its own material (<see cref="ClassicMaterialRecipe.FromKmf"/>, at
    /// <paramref name="texturePhase"/>'s texture when given, <see cref="TexturePhase"/>), then the environment passes
    /// (<see cref="ObjectMaterialLook.EnvironmentPass"/>) in submesh order, so a submesh index keeps its meaning.</summary>
    public static void Build(IReadOnlyList<KmfMaterial> materials, int submeshCount, Fp32? texturePhase, List<ObjectPass> into)
    {
        if (materials is null)
        {
            throw new ArgumentNullException(nameof(materials));
        }

        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        into.Clear();
        for (int i = 0; i < submeshCount; i++)
        {
            KmfMaterial? kmf = i < materials.Count ? materials[i] : null;
            into.Add(new ObjectPass(i, ObjectMaterialLook.Own(texturePhase is Fp32 phase ? TexturePhase(kmf, phase)
                : ClassicMaterialRecipe.FromKmf(kmf))));
        }
        for (int i = 0; i < submeshCount; i++)
        {
            if (i < materials.Count && ObjectMaterialLook.EnvironmentPass(materials[i]) is ObjectMaterialLook environment)
            {
                into.Add(new ObjectPass(i, environment));
            }
        }
    }

    /// <summary>A material at a texture phase (a Heart's decay, an object's <see cref="ObjectView.TexturePhase"/>): the
    /// texture <see cref="DungeonHeartVisual.TextureIndex"/> picks from its list.</summary>
    public static ClassicMaterialRecipe TexturePhase(KmfMaterial? kmf, Fp32 phase)
    {
        return ClassicMaterialRecipe.FromKmf(kmf, kmf is null ? 0 : DungeonHeartVisual.TextureIndex(phase, kmf.TextureNames.Count));
    }
}
