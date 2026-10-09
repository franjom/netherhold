// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.IO.Compression;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Convert.Textures;
using DK2.Data;
using DK2.Data.Wad;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class TextureTests
{
    [Fact]
    public void PngRoundTripPreservesRgbaPixelsWithoutAnInstall()
    {
        byte[] pixels =
        [
            255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 255, 64, 255, 255, 255, 0,
        ];
        byte[] png = new RgbaImage(2, 2, pixels).ToPng();

        (int width, int height, byte[] decoded) = DecodePng(png);

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(pixels, decoded);
    }

    [InstallTheory]
    [InlineData("Imp-PoseFrame.kmf")]
    [InlineData("Gob-PoseFrame.kmf")]
    [InlineData("ROCK WALL.kmf")]
    public void CreatureAndTerrainMaterialTexturesDecodeToPng(string modelName)
    {
        string install = Dk2InstallGate.Path;
        using var meshes = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        KmfModel model = KmfReader.Read(meshes.Extract(modelName));
        string textureName = model.Materials.SelectMany(material => material.TextureNames).First();
        using EngineTextureArchive textures = EngineTextureArchive.OpenInstall(install);

        RgbaImage image = textures.Decode(textureName);
        (int width, int height, byte[] decoded) = DecodePng(image.ToPng());

        Assert.Equal(image.Width, width);
        Assert.Equal(image.Height, height);
        Assert.Equal(image.Pixels, decoded);
        Assert.True(image.Pixels.Distinct().Count() > 16, $"{textureName} decoded as nearly constant data");
    }

    [InstallFact]
    public void EveryModelTextureNameResolvesToTheCache()
    {
        string install = Dk2InstallGate.Path;
        using var meshes = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        using EngineTextureArchive textures = EngineTextureArchive.OpenInstall(install);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WadEntry entry in meshes.Entries.Where(entry =>
            entry.Name.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)))
        {
            KmfModel model = KmfReader.Read(meshes.Extract(entry));
            foreach (string name in model.Materials.SelectMany(material =>
                material.TextureNames.Append(material.EnvironmentTextureName)))
                if (!string.IsNullOrEmpty(name))
                    try { _ = textures.FindBest(name); }
                    catch (FileNotFoundException) { missing.Add(name); }
        }
        Assert.Empty(missing);
    }

    [InstallFact]
    public void EngineTexturesWadContainsReadyPngImages()
    {
        string install = Dk2InstallGate.Path;
        using var wad = WadArchive.Open(Path.Combine(install, "Data", "EngineTextures.WAD"));
        Assert.All(wad.Entries, entry => Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            wad.Extract(entry).AsSpan(0, 8).ToArray()));
    }

    [InstallFact]
    public void EveryEngineTextureCacheEntryDecodes()
    {
        string install = Dk2InstallGate.Path;
        using EngineTextureArchive textures = EngineTextureArchive.OpenInstall(install);
        Assert.True(textures.Entries.Count > 5_000);
        foreach (EngineTextureEntry entry in textures.Entries)
        {
            RgbaImage image = textures.Decode(entry);
            Assert.Equal(checked(entry.Width * entry.Height * 4), image.Pixels.Length);
        }
    }

    [InstallFact]
    public void AlphaTexturePreservesNonOpaquePixels()
    {
        string install = Dk2InstallGate.Path;
        using EngineTextureArchive textures = EngineTextureArchive.OpenInstall(install);
        EngineTextureEntry entry = textures.Entries.First(entry => entry.HasAlpha);
        RgbaImage image = textures.Decode(entry);
        IEnumerable<byte> alpha = image.Pixels.Where((_, index) => (index & 3) == 3);
        Assert.Contains(alpha, value => value < 255);
    }

    private static (int Width, int Height, byte[] Pixels) DecodePng(byte[] png)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        int position = 8;
        int width = 0;
        int height = 0;
        using var idat = new MemoryStream();
        while (position < png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position)));
            string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(position + 8, length);
            if (type == "IHDR")
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                Assert.Equal(6, data[9]);
            }
            else if (type == "IDAT") idat.Write(data);
            position += 12 + length;
        }
        idat.Position = 0;
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress)) zlib.CopyTo(raw);
        byte[] scanlines = raw.ToArray();
        var pixels = new byte[checked(width * height * 4)];
        int rowBytes = width * 4;
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, scanlines[y * (rowBytes + 1)]);
            scanlines.AsSpan(y * (rowBytes + 1) + 1, rowBytes).CopyTo(pixels.AsSpan(y * rowBytes));
        }
        return (width, height, pixels);
    }
}
