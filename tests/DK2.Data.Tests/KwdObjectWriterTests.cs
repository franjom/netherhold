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
            "Height:330:4 Mass:334:4 Speed:338:4 AirFriction:33C:4 ReactionTurn:34A:2 NameStringId:352:2 TooltipStringId:350:2",
            excludedProperties: [nameof(ObjectDef.ArtSlots), nameof(ObjectDef.Light)]);

    [Fact]
    public void AnimationMetadataEditsOnlyItsDecodedBytesAndLeavesOtherResourceDataIntact()
    {
        byte[] source = Enumerable.Repeat((byte)0x5A, 894).ToArray();
        ObjectDef original = Read(source);
        var slots = original.ArtSlots.ToArray();
        slots[5] = new CreatureAnimationTiming { ResourceType = 6, Flags = CreatureAnimationFlags.Present,
            FrameCount = 123, FramesPerSecond = 30, StartSegmentFrames = 4, EndSegmentFrames = 7 };
        byte[] written = KwdObjects.WriteRecord(original with { ArtSlots = slots },
            new KwdRecordImage(KwdRecordType.Objects, source));
        Assert.Equal(slots, Read(written).ArtSlots);
        int slot = 0x20 + 5 * 84;
        for (int i = 0; i < source.Length; i++)
            if (!(i >= slot + 0x40 && i < slot + 0x46)
                && !(i >= slot + 0x48 && i < slot + 0x4A)
                && !(i >= slot + 0x50 && i <= slot + 0x52)) Assert.Equal(source[i], written[i]);
    }

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

    [Fact]
    public void AuthoredLightDescriptorIsDecodedAtItsFileOffset()
    {
        byte[] source = new byte[894];
        int[] words = [1234, -2345, 15360, 10240, 0x3800];
        for (int i = 0; i < words.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x314 + i * 4), words[i]);
        source[0x328] = 244; source[0x329] = 243; source[0x32A] = 204; source[0x32B] = 127;
        Assert.Equal(new KwdLight(1234, -2345, 15360, 10240, 0x3800, 244, 243, 204, 127),
            Read(source).Light);
    }

    [Fact]
    public void LightEditPreservesUnrelatedBytes()
    {
        byte[] source = Enumerable.Repeat((byte)0x5A, 894).ToArray();
        var light = new KwdLight(1234, -2345, 15360, 10240, 0x3800, 244, 243, 204, 127);
        byte[] written = KwdObjects.WriteRecord(Read(source) with { Light = light },
            new KwdRecordImage(KwdRecordType.Objects, source));
        Assert.Equal(light, Read(written).Light);
        for (int i = 0; i < source.Length; i++)
            if (i < 0x314 || i >= 0x32C) Assert.Equal(source[i], written[i]);
    }

    private static ObjectDef Read(byte[] source) => KwdObjects.Parse([
        new KwdChunk { Type = KwdRecordType.Objects, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
