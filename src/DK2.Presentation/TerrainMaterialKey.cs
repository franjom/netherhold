// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Which twin of a piece material a triangle group draws with (tag-highlight.md, classic-lighting.md).</summary>
public enum TerrainMaterialVariant : byte
{
    /// <summary>The material as the recipe makes it.</summary>
    Plain,
    /// <summary>Lit by <see cref="TerrainMaterialKey.Tint"/> more.</summary>
    Tinted,
    /// <summary>Lit by the dig tag's colour more.</summary>
    Tagged,
    /// <summary>Lit by full red more: the tutorial's red highlight.</summary>
    Red,
    /// <summary>Alpha-blended at <see cref="TerrainMaterialKey.FogOpacity"/>, no depth write: the sight fog.</summary>
    Fog,
    /// <summary>The additive pass over a tagged piece, flat BlueTexture; the recipe is unused.</summary>
    TagOverlay,
    /// <summary>The additive pass over a red-highlighted piece, flat RedTexture; the recipe is unused.</summary>
    RedTagOverlay,
}

/// <summary>What one terrain surface draws with: the piece material's recipe and the twin a tile's state picks. Equal
/// keys draw with the same material in every engine, so a chunk gathers its triangles per key.</summary>
/// <param name="Tint">The added light of a <see cref="TerrainMaterialVariant.Tinted"/> key, already cut to a piece's
/// bytes (<see cref="ClassicTerrainTint.PieceColour"/>); default otherwise.</param>
/// <param name="FogOpacity">The packed alpha of a <see cref="TerrainMaterialVariant.Fog"/> key; 0 otherwise.</param>
public readonly record struct TerrainMaterialKey(
    ClassicMaterialRecipe Recipe, TerrainMaterialVariant Variant, TerrainLightColour Tint = default, byte FogOpacity = 0)
{
    /// <summary>The additive pass drawn a second time over a tagged or red-highlighted piece.</summary>
    public bool IsOverlay
    {
        get
        {
            return Variant is TerrainMaterialVariant.TagOverlay or TerrainMaterialVariant.RedTagOverlay;
        }
    }

    /// <summary>The key a piece material takes on a tile: the sight fog's faded twin first, then the red highlight's
    /// and the tag's lit twins, then the terrain light's tinted one. Added light leaves an additive recipe as it is
    /// (<see cref="ClassicMaterialRecipe.TakesAddedLight"/>), and a tint cut to zero adds nothing.</summary>
    public static TerrainMaterialKey For(in ClassicMaterialRecipe recipe, bool red, bool tagged, TerrainLightColour tint,
        byte? fogOpacity)
    {
        if (fogOpacity is byte opacity)
        {
            return new TerrainMaterialKey(recipe, TerrainMaterialVariant.Fog, default, opacity);
        }

        if (!recipe.TakesAddedLight)
        {
            return new TerrainMaterialKey(recipe, TerrainMaterialVariant.Plain);
        }

        if (red)
        {
            return new TerrainMaterialKey(recipe, TerrainMaterialVariant.Red);
        }

        if (tagged)
        {
            return new TerrainMaterialKey(recipe, TerrainMaterialVariant.Tagged);
        }

        TerrainLightColour piece = ClassicTerrainTint.PieceColour(tint);
        return piece == default ? new TerrainMaterialKey(recipe, TerrainMaterialVariant.Plain)
            : new TerrainMaterialKey(recipe, TerrainMaterialVariant.Tinted, piece);
    }

    /// <summary>The additive overlay drawn again over a tagged (<paramref name="red"/> false) or red-highlighted piece.</summary>
    public static TerrainMaterialKey Overlay(bool red)
    {
        return new(default, red ? TerrainMaterialVariant.RedTagOverlay : TerrainMaterialVariant.TagOverlay);
    }
}
