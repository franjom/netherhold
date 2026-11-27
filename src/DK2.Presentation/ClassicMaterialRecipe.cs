// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;

namespace DK2.Presentation;

/// <summary>How a KMF material draws, for any engine to build its material from: one engine texture by name, the
/// blend DK2's material constructor gives the flags (<see cref="KmfMaterial.Blend"/>, <c>0x0057C420</c>) and the light
/// the material adds to its vertices (<see cref="KmfMaterial.SelfIllumination"/>). Every face is one-sided, nothing is
/// clipped and nothing shines: DK2 never enables specular light (SPECULARENABLE 0, <c>0x005899F0</c>).</summary>
/// <param name="Texture">The engine texture's name; empty for an untextured material.</param>
public readonly record struct ClassicMaterialRecipe(string Texture, KmfBlend Blend, float SelfIllumination)
{
    /// <summary>Only an opaque draw writes depth: DK2's blended states clear z-write (<c>0x0057C420</c>).</summary>
    public bool WritesDepth
    {
        get
        {
            return Blend == KmfBlend.Opaque;
        }
    }

    /// <summary>Alpha-blended or additive: drawn after the opaque terrain, back to front.</summary>
    public bool IsBlended
    {
        get
        {
            return Blend != KmfBlend.Opaque;
        }
    }

    /// <summary>Whether light added on top of the material changes it (the dig tag, the red highlight, a terrain
    /// tint). An additive material is already drawn at full white, so added light leaves it as it is.</summary>
    public bool TakesAddedLight
    {
        get
        {
            return Blend != KmfBlend.Additive;
        }
    }

    /// <summary>The self-illumination drawn as added light, 0 to 1 a channel; an additive material, already
    /// white, adds none.</summary>
    public float AddedLight
    {
        get
        {
            return Blend == KmfBlend.Additive ? 0f : SelfIllumination;
        }
    }

    /// <summary>A model material's recipe: the first named texture (alternative <paramref name="textureIndex"/>,
    /// clamped to the list), else the environment texture, as the converter orders them; an opaque untextured one
    /// where the model has no material.</summary>
    public static ClassicMaterialRecipe FromKmf(KmfMaterial? kmf, int textureIndex = 0)
    {
        if (kmf is null)
        {
            return new ClassicMaterialRecipe("", KmfBlend.Opaque, 0f);
        }

        string texture = kmf.TextureNames.Count > 0
            ? kmf.TextureNames[Math.Clamp(textureIndex, 0, kmf.TextureNames.Count - 1)]
            : kmf.EnvironmentTextureName ?? "";
        return new ClassicMaterialRecipe(texture, kmf.Blend, kmf.SelfIllumination);
    }
}
