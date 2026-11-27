// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class TerrainTileStateTests
{
    [Fact]
    public void PackedStateMatchesMaterialReferenceForEveryChannelAndFlag()
    {
        foreach (KmfBlend blend in Enum.GetValues<KmfBlend>())
        {
            foreach (bool red in new[] { false, true })
            {
                foreach (bool tag in new[] { false, true })
                {
                    for (int channel = 0; channel < 768; channel++)
        {
            var tint = new TerrainLightColour(channel, 767 - channel, channel * 3);
            var tile = new TileView(new TilePos(0, 0), default, default, default, tag, false, 0, 1000,
                ambientTint: tint, quarterAmbientTint: tint, redHighlighted: red, sightFogOpacity: 127);
            Vector4 packed = TerrainTileState.Encode(tile);
            Assert.Equal(ClassicTerrainTint.PieceColour(tint), TerrainTileState.DecodeColour(packed.X));
            Assert.Equal(ClassicTerrainTint.PieceColour(tint), TerrainTileState.DecodeColour(packed.Y));
            Assert.Equal(127, packed.W);
            var recipe = new ClassicMaterialRecipe("test", blend, .37f);
            var expected = TerrainMaterialLook.For(TerrainMaterialKey.For(recipe, red, tag, tint, null));
            Vector3 added = TerrainTileState.AddedLight(recipe, packed, packed, packed, false);
            Assert.Equal(expected.AddedLight, added);
            foreach (float light in new[] { 0f, .4f, .95f, 1f })
            {
                var vertexLight = new Vector3(light);
                var texture = new Vector3(.23f, .57f, .91f);
                Vector3 oldColour = texture * (expected.FullLight ? Vector3.One : Packed(vertexLight + expected.AddedLight));
                Vector3 newColour = texture * (blend == KmfBlend.Additive ? Vector3.One : Packed(vertexLight + added));
                Vector3 expectedOverlay = red ? TerrainMaterialLook.HoverOverlayColour : tag ? TerrainMaterialLook.TagOverlayColour : Vector3.Zero;
                Assert.Equal(oldColour + expectedOverlay, newColour + TerrainTileState.Overlay(packed, packed));
            }
            Vector3 overlay = TerrainTileState.Overlay(packed, packed);
            Assert.Equal(red ? TerrainMaterialLook.HoverOverlayColour : tag ? TerrainMaterialLook.TagOverlayColour : Vector3.Zero, overlay);
            if (blend == KmfBlend.Additive && (red || tag))
                        {
                            Assert.NotEqual(Vector3.Zero, overlay);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void AddedTintMustReachPackingBeforeClamp()
    {
        var tile = new TileView(new TilePos(0, 0), default, default, default, false, false, 0, 1000,
            ambientTint: new TerrainLightColour(128, 128, 128));
        Vector4 state = TerrainTileState.Encode(tile);
        Vector3 addition = TerrainTileState.AddedLight(new ClassicMaterialRecipe("test", KmfBlend.Opaque, .3f), state, state, state, false);
        Assert.Equal(Vector3.One, Packed(new Vector3(.4f) + addition));
        Assert.NotEqual(Packed(new Vector3(.4f) + addition), Packed(new Vector3(.7f)) + new Vector3(128f / 255f));
    }

    private static Vector3 Packed(Vector3 value)
    {
        return new Vector3(Pack(value.X), Pack(value.Y), Pack(value.Z));
    }

    private static float Pack(float value)
    {
        return Math.Min(MathF.Floor(Math.Max(value, 0) * 255f + .00001f), 255f) / 255f;
    }
}
