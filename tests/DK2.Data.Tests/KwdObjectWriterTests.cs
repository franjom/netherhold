// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdObjectWriterTests
{
    [Fact]
    public void EveryDecodedObjectFieldRoundTripsWithoutTouchingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Objects, 894, Read, KwdObjects.WriteRecord,
            "Name:0:32 ObjectId:35A:1 Flags:344:4 Hp:348:2 ManaValue:34E:2 " +
            "RoomCapacity:35C:1 StartState:35B:1 PickUpPriority:35D:1 Width:32C:4 " +
            "Height:330:4 Mass:334:4 Speed:338:4 NameStringId:352:2 TooltipStringId:350:2");

    [InstallFact]
    public void EveryShippedObjectTypedOverlayIsByteIdentical()
    {
        string file = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Objects.kwd");
        KwdChunk chunk = KwdReader.ReadFile(file).Single();
        List<ObjectDef> records = KwdObjects.Parse([chunk]);
        List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(145, records.Count);
        for (int i = 0; i < records.Count; i++)
            Assert.Equal(images[i].ToBytes(), KwdObjects.WriteRecord(records[i], images[i]));
    }

    private static ObjectDef Read(byte[] source) => KwdObjects.Parse([
        new KwdChunk { Type = KwdRecordType.Objects, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
