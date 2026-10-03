// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdKeeperSpellWriterTests
{
    [Fact]
    public void EveryDecodedKeeperSpellFieldRoundTripsWithoutTouchingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.KeeperSpells, 406, Read, KwdKeeperSpells.WriteRecord,
            "Name:0:32 ManaUpkeepPerSecond:E2:2 ManaCost:11C:4 KeeperSpellId:EE:1 " +
            "CooldownRaw:CC:4 LocationSelector:EF:1 TargetSelector:DA:1 Flags:DC:4 " +
            "ShotTypeId:F0:1 ShotData1:D0:4 ShotData2:D4:4 BonusShotTypeId:113:1 " +
            "BonusShotData1:114:4 BonusShotData2:118:4");

    [InstallFact]
    public void EveryShippedKeeperSpellTypedOverlayIsByteIdentical()
    {
        string file = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "KeeperSpells.kwd");
        KwdChunk chunk = KwdReader.ReadFile(file).Single();
        List<KeeperSpellDef> records = KwdKeeperSpells.Parse([chunk]);
        List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(13, records.Count);
        for (int i = 0; i < records.Count; i++)
            Assert.Equal(images[i].ToBytes(), KwdKeeperSpells.WriteRecord(records[i], images[i]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void UpkeepEditsOutsideTheFileWidthAreRefused(int value)
    {
        byte[] source = new byte[406];
        KeeperSpellDef changed = Read(source) with { ManaUpkeepPerSecond = value };
        Assert.Throws<InvalidDataException>(() => KwdKeeperSpells.WriteRecord(changed,
            new KwdRecordImage(KwdRecordType.KeeperSpells, source)));
    }

    private static KeeperSpellDef Read(byte[] source) => KwdKeeperSpells.Parse([
        new KwdChunk { Type = KwdRecordType.KeeperSpells, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
