// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdRoomWriterTests
{
    [Fact]
    public void EveryDecodedRoomFieldIncludingObjectArrayRoundTripsWithoutTouchingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Rooms, 1055, Read, KwdTerrainRooms.WriteRoomRecord,
            "Name:0:32 CeilingHeight:368:4 Flags:370:4 TooltipStringId:374:2 NameStringId:376:2 " +
            "Cost:378:2 CreatedObjectEffectId:384:2 RoomId:392:1 ReturnPercentage:393:1 TerrainId:394:1 Construction:395:1 " +
            "CreatedCreatureId:396:1 ObjectIds:39A:8 OrderInEditor:3C2:1 " +
            "RecommendedSizeX:41B:1 RecommendedSizeY:41C:1 HealthGain:41D:2",
            // The torch is read for the renderer and not written back: an edit to it is not a mod.
            excludedProperties: ["Torch"]);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void ObjectArrayEditsCannotChangeTheFixedRecordShape(int count)
    {
        byte[] source = new byte[1055];
        RoomDef changed = Read(source) with { ObjectIds = new byte[count] };
        Assert.Throws<InvalidDataException>(() => KwdTerrainRooms.WriteRoomRecord(changed,
            new KwdRecordImage(KwdRecordType.Rooms, source)));
    }

    [InstallFact]
    public void EveryShippedRoomTypedOverlayIsByteIdentical()
    {
        string file = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Rooms.kwd");
        KwdChunk chunk = KwdReader.ReadFile(file).Single();
        List<RoomDef> records = KwdTerrainRooms.ParseRooms([chunk]);
        List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(26, records.Count);
        Assert.Equal((ushort)17, records.Single(r => r.Name == "Lair").CreatedObjectEffectId);
        for (int i = 0; i < records.Count; i++)
            Assert.Equal(images[i].ToBytes(), KwdTerrainRooms.WriteRoomRecord(records[i], images[i]));
    }

    private static RoomDef Read(byte[] source) => KwdTerrainRooms.ParseRooms([
        new KwdChunk { Type = KwdRecordType.Rooms, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
