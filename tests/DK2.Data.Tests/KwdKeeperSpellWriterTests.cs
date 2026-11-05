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
            "BonusShotData1:114:4 BonusShotData2:118:4 SoundCategory:F1:32 GuiButtonSoundCategory:174:32");

    /// <summary>The category <c>0x004F7CE0</c> resolves into the runtime spell's <c>+0x6D</c> from the file record's 32 bytes at
    /// <c>0xF1</c> (its intermediate <c>+0x169</c>; the sibling at <c>0x174</c> is the GUI button's, runtime <c>+0x71</c>), and the
    /// shot record's <c>0xC9</c> is the shot definition's <c>+0x72</c> (<c>0x004ACDF0</c>, intermediate <c>+0x151</c>).</summary>
    [InstallFact]
    public void KeeperSpellAndShotSoundCategoriesAreTheShippedNames()
    {
        string editor = Path.Combine(Dk2InstallGate.Path, "Data", "editor");
        List<KeeperSpellDef> spells = KwdKeeperSpells.Parse(KwdReader.ReadFile(Path.Combine(editor, "KeeperSpells.kwd")));
        Assert.Equal(
            ["KEEPER_SPELL_SIGHT_OF_EVIL", "KEEPER_SPELL_POSSESSION", "KEEPER_SPELL_CALL_TO_ARMS", "KEEPER_SPELL_CREATE_IMP",
                "KEEPER_SPELL_SUMMON_REAPER", "KEEPER_SPELL_THUNDERBOLT", "KEEPER_SPELL_TURNCOAT", "KEEPER_SPELL_TREMOR",
                "KEEPER_SPELL_INFERNO", "KEEPER_SPELL_HEAL", "KEEPER_SPELL_CHICKEN", "KEEPER_SPELL_CREATE_GOLD", "KEEPER_SPELL_SUMMON_REAPER"],
            spells.OrderBy(s => s.KeeperSpellId).Select(s => s.SoundCategory));

        Dictionary<byte, string> shots = KwdAdditionalRecords.ParseShots(KwdReader.ReadFile(Path.Combine(editor, "Shots.kwd")))
            .ToDictionary(s => s.ShotId, s => s.SoundCategory);
        Assert.Equal("SPELL_FIREBALL", shots[1]);
        Assert.Equal("", shots[2]);
        Assert.Equal("SPELL_GRENADE", shots[18]);
        Assert.Equal("", shots[21]);
        Assert.Equal("KEEPER_SPELL_TREMOR", shots[24]);
        Assert.Equal("KEEPER_SPELL_SIGHT_OF_EVIL", shots[33]);
        Assert.Equal("SPELL_RAISE_DEAD", shots[40]);
        Assert.Equal("TRAP_SENTRY", shots[44]);
        Assert.Equal("SPELL_SKELETON_ARMY", shots[54]);
    }

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
