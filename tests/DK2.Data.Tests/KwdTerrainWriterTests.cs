// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdTerrainWriterTests
{
    [Fact]
    public void EveryDecodedTerrainFieldRoundTripsWithoutTouchingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Terrain, 552, Read, KwdTerrainRooms.WriteTerrainRecord,
            "Name:0:32 Depth:188:4 TerrainLightHeight:18C:4 Flags:190:4 Damage:194:2 GoldValue:19A:2 ManaGain:19C:2 " +
            "MaxManaGain:19E:2 TooltipStringId:1A0:2 NameStringId:1A2:2 MaxHealthEffectId:1A4:2 DestroyedEffectId:1A6:2 TerrainId:1D6:1 " +
            "StartingHealth:1D7:2 MaxHealthTypeTerrainId:1D9:1 DestroyedTypeTerrainId:1DA:1 MaxHealth:1FF:2",
            excludedProperties: ["Wibble", "Lighting"], // compound fields have dedicated overlay tests
            customValues: new Dictionary<string, object> { ["TerrainLightHeight"] = new DK2.Core.Fp32(-12345) });

    [InstallFact]
    public void EveryShippedTerrainTypedOverlayIsByteIdentical()
    {
        string file = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Terrain.kwd");
        KwdChunk chunk = KwdReader.ReadFile(file).Single();
        List<TerrainDef> records = KwdTerrainRooms.ParseTerrain([chunk]);
        List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(41, records.Count);
        Assert.Equal((ushort)5, records.Single(r => r.Name == "Dirt Path").MaxHealthEffectId);
        Assert.Equal((ushort)6, records.Single(r => r.Name == "Rock").DestroyedEffectId);
        Assert.Equal((ushort)7, records.Single(r => r.Name == "Gold").DestroyedEffectId);
        Assert.Equal((ushort)0, records.Single(r => r.Name == "Reinforced Wall").DestroyedEffectId);
        for (int i = 0; i < records.Count; i++)
            Assert.Equal(images[i].ToBytes(), KwdTerrainRooms.WriteTerrainRecord(records[i], images[i]));
    }

    private static TerrainDef Read(byte[] source) => KwdTerrainRooms.ParseTerrain([
        new KwdChunk { Type = KwdRecordType.Terrain, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
