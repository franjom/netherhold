// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>How a <see cref="TerrainMaterialKey"/> draws, for any engine's material: the texture times
/// <see cref="Colour"/>, times the vertex light plus <see cref="AddedLight"/> packed and clamped as DK2 packs its
/// diffuse, or at full light (<see cref="FullLight"/>); alpha from the texture times <see cref="Colour"/>'s, used only
/// when <see cref="Blend"/> is alpha-blended. Colours are the stored bytes over 255 (tag-highlight.md).</summary>
/// <param name="Texture">The engine texture's name; empty draws <see cref="Colour"/> alone.</param>
/// <param name="FullLight">Drawn at full white whatever lights the scene: an additive material and the overlays.</param>
public readonly record struct TerrainMaterialLook(
    string Texture, KmfBlend Blend, bool WritesDepth, bool FullLight, Vector4 Colour, Vector3 AddedLight)
{
    /// <summary>The dig tag's colour, added to a tagged piece's vertex light (<c>0x0059A050</c>, <c>0x0059A200</c>,
    /// summed by <c>0x0057B6D0</c>).</summary>
    public static readonly Vector3 TagTint = new(128f / 255f, 128f / 255f, 200f / 255f);

    /// <summary>BlueTexture's one colour, drawn over a tagged piece as AA_BlueTexture: ONE, ONE, no depth write
    /// (<c>0x0057C420</c>, <c>0x00589250</c>).</summary>
    public static readonly Vector3 TagOverlayColour = new(63f / 255f, 64f / 255f, 1f);

    /// <summary>RedTexture's one colour (global <c>0x006FCD73</c>, read at <c>0x0043BF1F</c> in <c>0x0043B220</c>): the
    /// overlay over a red-highlighted piece, and the Hand's hover pass.</summary>
    public static readonly Vector3 HoverOverlayColour = new(1f, 64f / 255f, 65f / 255f);

    /// <summary>The red highlight's added light: full red.</summary>
    public static readonly Vector3 RedLight = new(1f, 0f, 0f);

    /// <summary>A tagged or red-highlighted twin's added light: the tag's (or full red) plus the material's own.</summary>
    public static Vector3 TaggedLight(Vector3 own, bool red)
    {
        return (red ? RedLight : TagTint) + own;
    }

    /// <summary>A tinted twin's added light: the material's own plus the tint cut to a piece's bytes
    /// (<see cref="ClassicTerrainTint.PieceColour"/>).</summary>
    public static Vector3 TintedLight(Vector3 own, TerrainLightColour tint)
    {
        TerrainLightColour piece = ClassicTerrainTint.PieceColour(tint);
        return own + new Vector3(piece.Red / 255f, piece.Green / 255f, piece.Blue / 255f);
    }

    /// <summary>The sight fog's packed alpha as the material colour's alpha.</summary>
    public static float FogAlpha(byte packed)
    {
        return packed / 255f;
    }

    /// <summary>The look of each variant: the recipe's own; the tagged, red and tinted twins adding their light (an
    /// additive recipe and a tint cut to zero keep the plain look); the sight fog's alpha-blended twin, no depth write,
    /// keeping its own light; the overlays' flat colour, additive.</summary>
    public static TerrainMaterialLook For(in TerrainMaterialKey key)
    {
        return For(key, new Vector3(key.Recipe.AddedLight));
    }

    /// <summary><see cref="For(in TerrainMaterialKey)"/> for an engine material whose own added light is
    /// <paramref name="own"/> rather than the recipe's grey self-illumination.</summary>
    public static TerrainMaterialLook For(in TerrainMaterialKey key, Vector3 own)
    {
        ClassicMaterialRecipe recipe = key.Recipe;
        switch (key.Variant)
        {
            case TerrainMaterialVariant.TagOverlay: return Overlay(TagOverlayColour);
            case TerrainMaterialVariant.RedTagOverlay: return Overlay(HoverOverlayColour);
        }
        var plain = new TerrainMaterialLook(recipe.Texture ?? "", recipe.Blend, recipe.WritesDepth,
            recipe.Blend == KmfBlend.Additive, Vector4.One, own);
        switch (key.Variant)
        {
            case TerrainMaterialVariant.Fog:
                return plain with { Blend = KmfBlend.AlphaBlend, WritesDepth = false, Colour = new Vector4(1f, 1f, 1f, FogAlpha(key.FogOpacity)) };
            case TerrainMaterialVariant.Tagged or TerrainMaterialVariant.Red when recipe.TakesAddedLight:
                return plain with { AddedLight = TaggedLight(own, key.Variant == TerrainMaterialVariant.Red) };
            case TerrainMaterialVariant.Tinted when recipe.TakesAddedLight && ClassicTerrainTint.PieceColour(key.Tint) != default:
                return plain with { AddedLight = TintedLight(own, key.Tint) };
            default:
                return plain;
        }
    }

    private static TerrainMaterialLook Overlay(Vector3 colour)
    {
        return new("", KmfBlend.Additive, false, true, new Vector4(colour, 1f), Vector3.Zero);
    }
}
