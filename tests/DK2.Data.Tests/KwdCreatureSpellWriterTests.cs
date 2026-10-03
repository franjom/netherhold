// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdCreatureSpellWriterTests
{
    [Fact]
    public void EveryDecodedFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.CreatureSpells, 266, Read, KwdAdditionalRecords.WriteCreatureSpellRecord,
            "Name:0:32 EditorIcon:20:84 GuiIcon:74:84 ShotData1:C8:4 ShotData2:CC:4 Range:D0:4 Flags:D4:4 " +
            "CombatPoints:D8:2 SoundEvent:DA:2 NameStringId:DC:2 TooltipStringId:DE:2 GeneralDescriptionStringId:E0:2 " +
            "StrengthStringId:E2:2 WeaknessStringId:E4:2 CreatureSpellId:E6:1 ShotTypeId:E7:1 AlternativeShotId:E8:1 " +
            "AlternativeRoomId:E9:1 RechargeTime:EA:4 AlternativeShot:EE:1 Unknown_00EF:EF:27");

    [Theory]
    [InlineData(32)] [InlineData(116)]
    public void EveryNestedArtFieldRoundTripsWithoutChangingOtherBytes(int offset) =>
        KwdNestedOverlayChecks.Art(KwdRecordType.CreatureSpells, 266, offset, Read, KwdAdditionalRecords.WriteCreatureSpellRecord,
            c => offset == 32 ? c.EditorIcon : c.GuiIcon,
            (c, art) => offset == 32 ? c with { EditorIcon = art } : c with { GuiIcon = art });

    [Fact]
    public void RawFieldsRequireTheirExactSourceWidths()
    {
        var image = new KwdRecordImage(KwdRecordType.CreatureSpells, new byte[266]);
        CreatureSpellDef row = Read(image.ToBytes());
        Assert.Throws<InvalidDataException>(() => KwdAdditionalRecords.WriteCreatureSpellRecord(row with { Unknown_00EF = new byte[26] }, image));
        Assert.Throws<InvalidDataException>(() => KwdAdditionalRecords.WriteCreatureSpellRecord(row with {
            EditorIcon = row.EditorIcon with { Unknown_0044 = new byte[11] } }, image));
    }

    [InstallFact]
    public void EveryShippedCreatureSpellTypedOverlayIsByteIdentical()
    {
        KwdChunk chunk = KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "CreatureSpells.kwd")).Single();
        var rows = KwdAdditionalRecords.ParseCreatureSpells([chunk]); var images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(41, rows.Count);
        for (int i = 0; i < rows.Count; i++) Assert.Equal(images[i].ToBytes(), KwdAdditionalRecords.WriteCreatureSpellRecord(rows[i], images[i]));
    }

    private static CreatureSpellDef Read(byte[] source) => KwdAdditionalRecords.ParseCreatureSpells([
        new KwdChunk { Type = KwdRecordType.CreatureSpells, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
