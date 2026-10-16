// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Art;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public sealed class InstallEffectSpriteProviderTests
{
    [Fact]
    public void BuiltInWallFlameResolvesWithoutAnEffectElementDefinition()
    {
        var provider = new InstallEffectSpriteProvider(Database(), name =>
            name.StartsWith("KTorch", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(6), out int frame) && frame < 20
                ? Image : throw new FileNotFoundException());
        var key = AssetRef.ForObject("wall_torch", AssetKind.Effect, "flame");
        Assert.True(provider.TryResolve(key, out var asset));
        Assert.Equal("KTorch", asset.InstallEntry);
        Assert.True(provider.TryGetSprite(asset, out var sprite));
        Assert.Equal(20, sprite.DeclaredFrameCount);
        Assert.Equal(20, sprite.StoredFrameCount);
        Assert.Equal(4096, sprite.WidthRaw);
        Assert.Equal(4096, sprite.HeightRaw);
        Assert.Equal(EffectSpriteBlend.Additive, sprite.Blend);
        Assert.Equal(0x20004u, sprite.ArtFlags);
        Assert.True(provider.TryLoadFrame(asset, 19, out _));
        Assert.False(provider.TryLoadFrame(asset, 20, out _));
    }

    [Fact]
    public void DeclaredDurationAndAvailableImageCountRemainSeparate()
    {
        var element = Element("Cloud", "cloud", 3, 0x24, 16);
        var provider = new InstallEffectSpriteProvider(Database(element), name =>
            name is "cloud0" or "cloud1" ? Image : throw new FileNotFoundException());
        Assert.True(provider.TryResolve(AssetRef.ForEffectElement("Cloud"), out var asset));
        Assert.True(provider.TryGetSprite(asset, out var sprite));
        Assert.Equal(16, sprite.DeclaredFrameCount);
        Assert.Equal(2, sprite.StoredFrameCount);
        Assert.Equal(EffectSpriteBlend.Additive, sprite.Blend);
        Assert.Equal(4096, sprite.WidthRaw);
        Assert.True(provider.TryLoadFrame(asset, 1, out var image));
        Assert.Same(Image, image);
        Assert.False(provider.TryLoadFrame(asset, 2, out _));
    }

    [Fact]
    public void StaticSpriteUsesUnnumberedTextureAndRejectsForeignAssets()
    {
        var provider = new InstallEffectSpriteProvider(Database(Element("Chip", "chip", 2, 1, 0)),
            name => name == "chip" ? Image : throw new FileNotFoundException());
        Assert.True(provider.TryResolve(AssetRef.ForEffectElement("Chip"), out var asset));
        Assert.Equal("chip", asset.InstallEntry);
        Assert.True(provider.TryGetSprite(asset, out var sprite));
        Assert.Equal(EffectSpriteBlend.Alpha, sprite.Blend);
        Assert.Equal(1, sprite.StoredFrameCount);
        var foreign = new ResolvedAsset(asset.Reference, AssetSource.InstallEntry, null, default, "other", "chip");
        Assert.False(provider.TryLoadFrame(foreign, 0, out _));
    }

    [Fact]
    public void MissingSpriteAndMeshArtDoNotResolve()
    {
        var provider = new InstallEffectSpriteProvider(Database(Element("Missing", "missing", 3, 4, 4),
            Element("Mesh", "mesh", 6, 1, 4)), _ => throw new FileNotFoundException());
        Assert.False(provider.TryResolve(AssetRef.ForEffectElement("Missing"), out _));
        Assert.False(provider.TryResolve(AssetRef.ForEffectElement("Mesh"), out _));
    }

    [InstallFact]
    public void ShippedConstructionSpritesDecodeTheirActualImages()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var art = InstallArtProvider.Open(Dk2InstallGate.Path, database);
        var provider = new InstallEffectSpriteProvider(database, art.LoadTexture);
        Assert.True(provider.TryResolve(AssetRef.ForEffectElement(database.EffectElements[204].Name), out var mist));
        Assert.True(provider.TryGetSprite(mist, out var sprite));
        Assert.Equal(16, sprite.DeclaredFrameCount);
        Assert.Equal(8, sprite.StoredFrameCount);
        for (int frame = 0; frame < sprite.StoredFrameCount; frame++)
        {
            Assert.True(provider.TryLoadFrame(mist, frame, out var image));
            Assert.Contains(image.Pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        }
        Assert.True(provider.TryResolve(AssetRef.ForEffectElement(database.EffectElements[144].Name), out var chip));
        Assert.True(provider.TryLoadFrame(chip, 0, out _));
        Assert.True(provider.TryResolve(AssetRef.ForObject("wall_torch", AssetKind.Effect, "flame"), out var flame));
        Assert.True(provider.TryGetSprite(flame, out var flameSprite));
        Assert.Equal(20, flameSprite.DeclaredFrameCount);
        Assert.True(flameSprite.StoredFrameCount > 0);
        Assert.True(provider.TryLoadFrame(flame, 0, out var flameImage));
        Assert.Contains(flameImage.Pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    [Fact]
    public void AmbiguousShippedNamesDoNotSelectAnArbitraryResource()
    {
        var second = Element("Same", "second", 3, 1, 0) with { EffectElementId = 2 };
        var provider = new InstallEffectSpriteProvider(Database(Element("Same", "first", 3, 1, 0), second),
            _ => Image);
        Assert.False(provider.TryResolve(AssetRef.ForEffectElement("Same"), out _));
    }

    private static readonly RgbaImage Image = new(1, 1, [255, 255, 255, 255]);
    private static GameDatabase Database(params EffectElementDef[] elements) =>
        new([], [], [], new GlobalVariables(), effectElements: elements);

    private static EffectElementDef Element(string name, string texture, byte type, uint flags, int frames)
    {
        byte[] values = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(values, 4096);
        BinaryPrimitives.WriteInt32LittleEndian(values.AsSpan(4), 4096);
        BinaryPrimitives.WriteInt32LittleEndian(values.AsSpan(8), frames);
        return new EffectElementDef([])
        {
            Name = name, EffectElementId = (ushort)(name == "Mesh" ? 2 : 1),
            ArtResource = new(texture, flags, values, type, 0, 0, 0),
        };
    }
}
