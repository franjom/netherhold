// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdDoorWriterTests
{
    [Fact]
    public void EveryDecodedDoorFieldEditsOnlyItsOwnBytesAndReadsBack()
    {
        byte[] source = Enumerable.Repeat((byte)0x5A, 616).ToArray();
        source[0] = (byte)'a'; source[1] = 0;
        DoorDef original = Read(source);
        var edits = new (Func<DoorDef, DoorDef> Change, int Offset, int Width)[]
        {
            (d => d with { Name = "edited" }, 0, 32),
            (d => d with { DoorId = 7 }, 0x244, 1),
            (d => d with { CrateObjectId = 9 }, 0x246, 1),
            (d => d with { Health = 1234 }, 0x22C, 2),
            (d => d with { HealthGain = -17 }, 0x21C, 2),
            (d => d with { GoldCost = 789 }, 0x22E, 2),
            (d => d with { ManufactureRequired = 0xF1234567 }, 0x234, 4),
            (d => d with { Flags = (DoorFlags)0x81 }, 0x228, 1),
        };
        foreach (var edit in edits)
        {
            DoorDef changed = edit.Change(original);
            byte[] written = KwdDoors.WriteRecord(changed, new KwdRecordImage(KwdRecordType.Doors, source));
            Assert.Equal(changed, Read(written));
            Assert.False(source.SequenceEqual(written));
            for (int i = 0; i < source.Length; i++)
                if (i < edit.Offset || i >= edit.Offset + edit.Width) Assert.Equal(source[i], written[i]);
        }
    }

    [Fact]
    public void UndecodedArtByteAndNoncanonicalNamePaddingSurvive()
    {
        byte[] source = Enumerable.Repeat((byte)0xA7, 616).ToArray();
        byte[] name = [(byte)' ', (byte)'a', (byte)' ', 0];
        name.CopyTo(source, 0);
        DoorDef original = Read(source);
        Assert.Equal("a", original.Name);
        Assert.Equal(source, KwdDoors.WriteRecord(original, new KwdRecordImage(KwdRecordType.Doors, source)));
        byte[] edited = KwdDoors.WriteRecord(original with { Name = "b" }, new KwdRecordImage(KwdRecordType.Doors, source));
        Assert.Equal(source.AsSpan(2).ToArray(), edited.AsSpan(2).ToArray());
        Assert.Equal("b", Read(edited).Name);
    }

    [Fact]
    public void WrongTemplateAndNamesThatCannotRoundTripAreRefused()
    {
        DoorDef original = Read(new byte[616]);
        Assert.Throws<InvalidDataException>(() => KwdDoors.WriteRecord(original,
            new KwdRecordImage(KwdRecordType.Traps, new byte[616])));
        Assert.Throws<InvalidDataException>(() => KwdDoors.WriteRecord(original,
            new KwdRecordImage(KwdRecordType.Doors, new byte[615])));
        Assert.Throws<InvalidDataException>(() => KwdDoors.WriteRecord(original with { Name = " leading" },
            new KwdRecordImage(KwdRecordType.Doors, new byte[616])));
        Assert.Throws<InvalidDataException>(() => KwdDoors.WriteRecord(original with { Name = "\u00E9" },
            new KwdRecordImage(KwdRecordType.Doors, new byte[616])));
        Assert.Throws<InvalidDataException>(() => KwdDoors.WriteRecord(original with { Name = new string('a', 33) },
            new KwdRecordImage(KwdRecordType.Doors, new byte[616])));
    }

    [InstallFact]
    public void EveryShippedDoorTypedOverlayIsByteIdentical()
    {
        string file = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Doors.kwd");
        KwdChunk chunk = KwdReader.ReadFile(file).Single();
        List<DoorDef> records = KwdDoors.Parse([chunk]);
        List<KwdRecordImage> images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(8, records.Count);
        for (int i = 0; i < records.Count; i++)
            Assert.Equal(images[i].ToBytes(), KwdDoors.WriteRecord(records[i], images[i]));
    }

    private static DoorDef Read(byte[] source) => KwdDoors.Parse([
        new KwdChunk { Type = KwdRecordType.Doors, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
