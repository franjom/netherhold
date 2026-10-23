// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdTrapWriterTests
{
    [Fact]
    public void EveryDecodedTrapFieldRoundTripsWithoutTouchingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Traps, 579, Read, KwdTraps.WriteRecord,
            "Name:0:32 TrapId:20C:1 Threat:1EA:2 Health:1F0:2 GoldCost:1F2:2 " +
            "ManufactureRequired:1FA:2 ShotTypeId:20D:1 CrateObjectId:20E:1 ManaUpkeepPerSecond:202:2 " +
            "Flags:1EC:4 TriggerType:20B:1 RangeTiles:1DC:4 RechargeSecondsRaw:1C8:4 " +
            "ChargeSecondsRaw:1CC:4 ThreatSecondsRaw:1D0:4 ManaCostToFire:1D4:4 " +
            "ShotData1:1E0:4 ShotData2:1E4:4 ShotOffsetX:231:4 ShotOffsetY:235:4 ShotOffsetZ:239:4 HealthGain:241:2 NameStringId:208:2");

    [InstallFact]
    public void EveryShippedTrapTypedOverlayIsByteIdentical()
    {
        string file = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Traps.kwd");
        KwdChunk chunk = KwdReader.ReadFile(file).Single();
        List<TrapDef> records = KwdTraps.Parse([chunk]);
        List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(16, records.Count);
        for (int i = 0; i < records.Count; i++)
            Assert.Equal(images[i].ToBytes(), KwdTraps.WriteRecord(records[i], images[i]));
    }

    private static TrapDef Read(byte[] source) => KwdTraps.Parse([
        new KwdChunk { Type = KwdRecordType.Traps, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
