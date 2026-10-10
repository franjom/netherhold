// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Art.Kmf;
using DK2.Data.Wad;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Convert.Tests;

/// <summary>A KMF material's flags as DK2's loader (0x0057D390) and material constructor
/// (0x0057C420) read them (docs/research/kmf-materials.md).</summary>
public class KmfMaterialTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0x00u, KmfBlend.Opaque)]
    [InlineData(0x02u, KmfBlend.Opaque)] // "double sided": the loader drops the bit
    [InlineData(0x80u, KmfBlend.Opaque)] // "Illum": dropped too; brightness does the lighting
    [InlineData(0x100u, KmfBlend.Opaque)]
    [InlineData(0x40u, KmfBlend.Opaque)] // environment map, an extra stage, not a blend
    [InlineData(0x01u, KmfBlend.AlphaBlend)] // "mask"
    [InlineData(0x81u, KmfBlend.AlphaBlend)] // the gold spill cards
    [InlineData(0x0Au, KmfBlend.AlphaBlend)] // "TRANS25"
    [InlineData(0x10u, KmfBlend.AlphaBlend)]
    [InlineData(0x20u, KmfBlend.AlphaBlend)]
    [InlineData(0x04u, KmfBlend.Additive)] // "ADD"
    [InlineData(0x06u, KmfBlend.Additive)]
    [InlineData(0x05u, KmfBlend.AlphaBlend)] // none ships; the applier issues SRCALPHA after ONE/ONE
    public void FlagsGiveTheConstructorsBlend(uint flags, KmfBlend expected) =>
        Assert.Equal(expected, Material(flags, 0f).Blend);

    [Fact]
    public void BrightnessIsTheLightAMaterialAddsAndAnAdditiveMaterialIsFullyLit()
    {
        Assert.Equal(0.6f, Material(0x81, 0.6f).SelfIllumination, 5);
        Assert.Equal(0f, Material(0x00, 0f).SelfIllumination);
        Assert.Equal(1f, Material(0x04, 0f).SelfIllumination);
        Assert.Equal(1f, Material(0x00, 3f).SelfIllumination);
    }

    [InstallFact]
    public void TreasuryAndCasinoMaterialsReadTheirShippedFlags()
    {
        string install = Dk2InstallGate.Path;
        using var wad = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        var byName = new Dictionary<string, KmfMaterial>(StringComparer.Ordinal);
        foreach (string entry in new[] { "Gold Pile.kmf", "Gold Urn1.kmf", "Gold Urn2.kmf", "Gold Chest.kmf", "Bar_Barrel2.kmf" })
        {
            KmfModel model = KmfReader.Read(wad.Extract(entry));
            foreach (KmfMaterial material in model.Materials)
            {
                output.WriteLine($"{entry} | {material.Name} | flags 0x{material.Flags:X} | brightness {material.Brightness} | {material.Blend} | [{string.Join(", ", material.TextureNames)}]");
                byName[entry + "|" + material.Name] = material;
            }
            RenderMesh mesh = RenderMesh.FromModel(model);
            for (int i = 0; i < model.Materials.Count; i++)
                Assert.Equal(model.Materials[i].Blend, mesh.Blends[i]);
        }

        // The gold spill cards: flags 0x81, the "mask" bit, so alpha-blended by the texture's alpha.
        KmfMaterial spill = byName["Gold Pile.kmf|Gold_HIGHLIGHT1Gold_Spill_Small"];
        Assert.Equal(0x81u, spill.Flags);
        Assert.Equal(KmfBlend.AlphaBlend, spill.Blend);
        Assert.Equal(0.6f, spill.SelfIllumination, 5);
        Assert.Equal(KmfBlend.AlphaBlend, byName["Gold Urn2.kmf|Gold_HIGHLIGHT2Gold_Spill"].Blend);
        Assert.Equal(KmfBlend.Opaque, byName["Gold Urn2.kmf|UrnieTreasureUrn1C"].Blend);
        // The gold inside a chest is 0x80 ("Illum"): opaque, self-lit by its brightness.
        Assert.Equal(KmfBlend.Opaque, byName["Gold Chest.kmf|Gold_In_ChestGold"].Blend);
        // The Alcove Barrels (object 68, Bar_Barrel2) are plain opaque materials.
        Assert.All(byName.Where(pair => pair.Key.StartsWith("Bar_Barrel2.kmf|", StringComparison.Ordinal)),
            pair => Assert.Equal((0u, KmfBlend.Opaque), (pair.Value.Flags, pair.Value.Blend)));
    }

    [InstallFact]
    public void ShippedFlagsUseOnlyTheSurveyedBits()
    {
        string install = Dk2InstallGate.Path;
        using var wad = WadArchive.Open(Path.Combine(install, "Data", "Meshes.WAD"));
        var counts = new SortedDictionary<uint, int>();
        foreach (WadEntry entry in wad.Entries.Where(entry =>
            entry.Name.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)))
            foreach (KmfMaterial material in KmfReader.Read(wad.Extract(entry)).Materials)
                counts[material.Flags] = counts.GetValueOrDefault(material.Flags) + 1;
        foreach ((uint flags, int count) in counts)
            output.WriteLine($"0x{flags:X}: {count}");
        // Bits 0x01..0x100 only; the loader reads 0x01, 0x04, 0x08, 0x10, 0x20, 0x40.
        Assert.All(counts.Keys, flags => Assert.Equal(0u, flags & ~0x1FFu));
        // No shipped material has both the additive and an alpha bit.
        Assert.DoesNotContain(counts.Keys, flags => (flags & 0x04) != 0 && (flags & 0x39) != 0);
    }

    private static KmfMaterial Material(uint flags, float brightness) =>
        new("m", ["t"], flags, brightness, 0f, "");
}
