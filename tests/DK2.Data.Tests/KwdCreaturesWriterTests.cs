// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Core;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdCreaturesWriterTests
{
    [Theory]
    [InlineData(5449)] [InlineData(5537)]
    public void EveryDecodedScalarRoundTripsAtBothStridesWithoutTouchingOtherBytes(int stride) =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Creatures, stride, Read, KwdCreatures.WriteRecord,
            "Name:0:32 CreatureId:F4B:1 UniqueBaseCreatureId:D3B:1 Flags:EFC:4 Flags2:1545:4 " +
            "EntranceOffsetX:E94:4 EntranceOffsetY:E98:4 PrayerStepX:127B:4 PrayerStepY:127F:4 EntranceFrames:A3C:4 EntranceFramesPerSecond:A40:4 GetUpFrames:940:4 GetUpFramesPerSecond:944:4 " +
            "AngryArtType:364:1 AngryArtFlags:354:4 AngryFrames:358:4 AngryFramesPerSecond:35C:4 " +
            "Speed:EB4:4 RunSpeed:EB8:4 ShuffleSpeed:D37:4 Height:EA4:4 EyeHeight:EB0:4 " +
            "DistanceCanSee:EC8:4 DistanceCanHear:ECC:4 PerceptionRange:D01:4 FootprintRadius:EA0:4 SizeClass:EA8:4 TurnRate:F20:2 " +
            "Hp:F0C:2 HpFromChicken:F0E:2 HungerRate:EBC:4 HungerFill:F4E:1 Unknown_0F4C:F4C:1 " +
            "TimeAwake:EC0:4 TimeSleep:EC4:4 OwnLandHealthIncreasePerSecond:EEA:2 DecomposeValue:F22:2 " +
            "Fear:F10:2 Threat:F12:2 MeleeDamage:F14:2 SlapDamage:F16:2 MeleeRange:EEC:4 MeleeRecharge:EF8:4 " +
            "MeleeHitDelay:CF9:4 MeleeAttackType:F50:1 StunDuration:ED0:4 FightStyle:F03:1 " +
            "Pay:F1C:2 MaxGoldHeld:F1E:2 InitialGoldHeld:F34:2 ExpForNextLevel:F00:2 ExpPerSecond:F04:2 " +
            "ExpPerSecondTraining:F06:2 ResearchPerSecond:F08:2 ManufacturePerSecond:F0A:2 ManaGeneratedByPrayerPerSecond:F18:2 JobClass:F02:1 " +
            "UnhappyThreshold:F4F:1 AngerNoLair:F28:2 AngerNoFood:F2A:2 AngerNoPay:F2C:2 AngerNoWork:F2E:2 " +
            "AngerSlap:F30:2 AngerInHand:F32:2 DropDeliveryCalm:FCB:2 TortureHpChange:107F:2 TortureMoodChange:1081:2 TortureTimeToConvert:EF4:4 " +
            "GuardDuration:ED4:4 IdleDuration:ED8:4 SlapFearlessDuration:EDC:4 PossessionManaCost:EE8:2 LairObjectId:F52:1 " +
            "CloneCreatureId:D3B:1 NameStringId:F24:2 TooltipStringId:F26:2 DescriptionStringId:F38:2 IntroductionStringId:CFF:2 UnhappySpeechId:D05:2 AngrySpeechId:D07:2 LeftSpeechId:D09:2 EntranceEffectId:F36:2",
            excludedProperties: ["Attractions", "Spells", "Resistances", "HappyJobs", "UnhappyJobs", "AngryJobs", "HateJobs", "AlternativeJobs",
                "KnockdownAnimation", "GetUpAnimation", "KnockdownFallbackAnimation", "GetUpFallbackAnimation",
                "ReaperSummonAnimation", "ReaperSummonFallbackAnimation", "AlternateArt", "ArtSlots"],
            customValues: new Dictionary<string, object> { ["SizeClass"] = 13 });

    [Fact]
    public void FilteredListsEditTheirActiveSourceSlotsAndPreserveEmptySlots()
    {
        byte[] source = Source(5537);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0xEA8), 0x12345ABC);
        source[0xDC4 + 22] = 0; source[0xE0C] = 0;
        foreach (int offset in new[] { 0xE14, 0xE38, 0xE50, 0xE74, 0xE7C })
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(offset), 0);
        CreatureDef original = Read(source);
        var image = new KwdRecordImage(KwdRecordType.Creatures, source);
        Assert.Equal(source, KwdCreatures.WriteRecord(original, image));
        Assert.Equal(0xABCu, BinaryPrimitives.ReadUInt32LittleEndian(
            KwdCreatures.WriteRecord(original with { SizeClass = 13 }, image).AsSpan(0xEA8)) & 0xFFF);
        CheckList(original.Spells, (c, a) => c with { Spells = a }, c => c.Spells,
            [(0xDC4 + 24 + 22, 1), (0xDC4 + 24 + 23, 1), (0xDC4 + 24 + 16, 4), (0xDC4 + 24 + 13, 1),
             (0xDC4 + 24, 4), (0xDC4 + 24 + 4, 4), (0xDC4 + 24 + 8, 4)]);
        CheckList(original.Resistances, (c, a) => c with { Resistances = a }, c => c.Resistances, [(0xE0E, 1), (0xE0F, 1)]);
        CheckList(original.Attractions, (c, a) => c with { Attractions = a }, c => c.Attractions, [(0xDAC, 4), (0xDB0, 2), (0xDB2, 2)]);
        CheckList(original.HappyJobs, (c, a) => c with { HappyJobs = a }, c => c.HappyJobs, PrefFields(0xE20));
        CheckList(original.UnhappyJobs, (c, a) => c with { UnhappyJobs = a }, c => c.UnhappyJobs, PrefFields(0xE44));
        CheckList(original.AngryJobs, (c, a) => c with { AngryJobs = a }, c => c.AngryJobs, PrefFields(0xE5C));
        CheckList(original.AlternativeJobs, (c, a) => c with { AlternativeJobs = a }, c => c.AlternativeJobs, [(0xE84, 4), (0xE88, 2), (0xE8A, 2)]);
        JobType[] hates = original.HateJobs.ToArray(); hates[0] = (JobType)13;
        AssertEdit(original with { HateJobs = hates }, 0xE78, 4, c => Assert.Equal(hates, c.HateJobs));

        void CheckList<T>(IReadOnlyList<T> list, Func<CreatureDef, T[], CreatureDef> replace,
            Func<CreatureDef, IReadOnlyList<T>> select, (int Offset, int Width)[] fields) where T : class
        {
            var properties = typeof(T).GetProperties();
            Assert.Equal(properties.Length, fields.Length);
            for (int i = 0; i < properties.Length; i++)
            {
                T[] changed = list.ToArray();
                changed[0] = (T)typeof(T).GetMethod("<Clone>$")!.Invoke(list[0], null)!;
                var property = properties[i]; Type type = property.PropertyType;
                object value = type == typeof(uint) ? 13u : type == typeof(int) ? -13 : type == typeof(ushort) ? (ushort)13 : type == typeof(short) ? (short)-13
                    : type == typeof(byte) ? (byte)13 : type == typeof(Fp32) ? new Fp32(-12345)
                    : type.IsEnum ? Enum.ToObject(type, 13) : throw new InvalidOperationException(property.Name);
                property.SetValue(changed[0], value);
                var field = fields[i];
                AssertEdit(replace(original, changed), field.Offset, field.Width, c => Assert.Equal(value, property.GetValue(select(c)[0])));
            }
        }
        void AssertEdit(CreatureDef changed, int offset, int width, Action<CreatureDef> assert)
        {
            byte[] written = KwdCreatures.WriteRecord(changed, image); assert(Read(written));
            Assert.False(source.SequenceEqual(written));
            for (int i = 0; i < source.Length; i++)
                if (i < offset || i >= offset + width) Assert.Equal(source[i], written[i]);
        }
        static (int, int)[] PrefFields(int offset) => [(offset, 4), (offset + 4, 2), (offset + 6, 2), (offset + 8, 1)];
    }

    [Fact]
    public void MissingAngryResourceRetainsHiddenBytesAndRejectsInvisibleFieldEdits()
    {
        byte[] source = Source(5537); source[0x364] = 0;
        CreatureDef original = Read(source); var image = new KwdRecordImage(KwdRecordType.Creatures, source);
        Assert.Equal(source, KwdCreatures.WriteRecord(original, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { AngryFrames = 1 }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { SizeClass = -1 }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { SizeClass = 0x100000 }, image));
        byte[] added = KwdCreatures.WriteRecord(original with { AngryArtType = 1, AngryFrames = 42 }, image);
        Assert.Equal(42, Read(added).AngryFrames);
        Assert.Equal(source[5449..], added[5449..]);
    }

    [Fact]
    public void RemovingAngryResourceRetainsItsHiddenPayloadBytes()
    {
        byte[] source = Source(5449); CreatureDef original = Read(source);
        byte[] written = KwdCreatures.WriteRecord(original with { AngryArtType = 0,
            AngryArtFlags = 0, AngryFrames = 0, AngryFramesPerSecond = 0 },
            new KwdRecordImage(KwdRecordType.Creatures, source));
        CreatureDef reread = Read(written);
        Assert.Equal(0, reread.AngryArtType); Assert.Equal(0u, reread.AngryArtFlags);
        Assert.Equal(0, reread.AngryFrames); Assert.Equal(0, reread.AngryFramesPerSecond);
        for (int i = 0; i < source.Length; i++) if (i != 0x364) Assert.Equal(source[i], written[i]);
    }

    [Fact]
    public void RefusesFilteredListShapeChangesAndAbsentDiscriminators()
    {
        byte[] source = Source(5449); CreatureDef original = Read(source);
        var image = new KwdRecordImage(KwdRecordType.Creatures, source);
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { Spells = [] }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { Resistances = [] }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { HappyJobs = [] }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { UnhappyJobs = [] }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { AngryJobs = [] }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { HateJobs = [] }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { AlternativeJobs = [] }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { Attractions = [] }, image));
        var spells = original.Spells.ToArray(); spells[0] = spells[0] with { CreatureSpellId = 0 };
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { Spells = spells }, image));
        var resists = original.Resistances.ToArray(); resists[0] = resists[0] with { AttackType = 0 };
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { Resistances = resists }, image));
        var jobs = original.HappyJobs.ToArray(); jobs[0] = jobs[0] with { Job = JobType.None };
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { HappyJobs = jobs }, image));
        var hates = original.HateJobs.ToArray(); hates[0] = JobType.None;
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { HateJobs = hates }, image));
        var alternatives = original.AlternativeJobs.ToArray(); alternatives[0] = alternatives[0] with { Job = JobType.None };
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original with { AlternativeJobs = alternatives }, image));
        Assert.Throws<InvalidDataException>(() => KwdCreatures.WriteRecord(original,
            new KwdRecordImage(KwdRecordType.Creatures, new byte[5448])));
    }

    [Theory]
    [InlineData(5449, 24)] [InlineData(5449, 27)] [InlineData(5449, 4)] [InlineData(5449, 1)]
    [InlineData(5537, 24)] [InlineData(5537, 27)] [InlineData(5537, 4)] [InlineData(5537, 1)]
    [InlineData(5449, 35)] [InlineData(5449, 8)] [InlineData(5537, 35)] [InlineData(5537, 8)]
    public void AnimationMetadataPreservesUnknownUpperWordsAndOtherArtBytes(int stride, int slot)
    {
        byte[] source = Source(stride);
        int offset = 0x20 + (slot * 84);
        source[offset + 0x50] = 6;
        CreatureDef original = Read(source);
        var timing = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = CreatureAnimationFlags.StartSegment | CreatureAnimationFlags.EndSegment,
            FrameCount = 51, FramesPerSecond = 30, StartSegmentFrames = 6, EndSegmentFrames = 14,
        };
        CreatureDef changed = slot switch
        {
            24 => original with { KnockdownAnimation = timing },
            27 => original with { GetUpAnimation = timing },
            4 => original with { KnockdownFallbackAnimation = timing },
            35 => original with { ReaperSummonAnimation = timing },
            8 => original with { ReaperSummonFallbackAnimation = timing },
            _ => original with { GetUpFallbackAnimation = timing },
        };
        byte[] written = KwdCreatures.WriteRecord(changed, new KwdRecordImage(KwdRecordType.Creatures, source));
        CreatureDef reread = Read(written);
        Assert.Equal(timing, slot switch
        {
            24 => reread.KnockdownAnimation, 27 => reread.GetUpAnimation,
            4 => reread.KnockdownFallbackAnimation,
            35 => reread.ReaperSummonAnimation, 8 => reread.ReaperSummonFallbackAnimation,
            _ => reread.GetUpFallbackAnimation,
        });
        Assert.Equal(source, KwdCreatures.WriteRecord(original, new KwdRecordImage(KwdRecordType.Creatures, source)));
        for (int i = 0; i < stride; i++)
        {
            int relative = i - offset;
            bool known = relative is >= 0x40 and <= 0x45 or >= 0x48 and <= 0x49 or >= 0x50 and <= 0x52;
            if (!known) Assert.Equal(source[i], written[i]);
        }
    }

    [Theory]
    [InlineData(5449, 19)] [InlineData(5537, 37)] [InlineData(5537, 28)]
    public void AnArtSlotEditedThroughTheSlotListIsWrittenToItsBlockAlone(int stride, int slot)
    {
        byte[] source = Source(stride);
        int offset = 0x20 + (slot * 84);
        source[offset + 0x50] = 6;
        CreatureDef original = Read(source);
        var timing = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 40, FramesPerSecond = 20 };
        CreatureAnimationTiming[] slots = original.ArtSlots.ToArray();
        slots[slot] = timing;
        byte[] written = KwdCreatures.WriteRecord(original with { ArtSlots = slots }, new KwdRecordImage(KwdRecordType.Creatures, source));
        Assert.Equal(timing, Read(written).ArtSlot(slot));
        for (int i = 0; i < stride; i++)
        {
            int relative = i - offset;
            bool known = relative is >= 0x40 and <= 0x45 or >= 0x48 and <= 0x49 or >= 0x50 and <= 0x52;
            if (!known) Assert.Equal(source[i], written[i]);
        }
    }

    [InstallFact]
    public void EveryShippedCreatureTypedOverlayIsByteIdentical()
    {
        KwdChunk chunk = KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Creatures.kwd")).Single();
        var creatures = KwdCreatures.Parse([chunk]); var images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(46, creatures.Count);
        for (int i = 0; i < creatures.Count; i++) Assert.Equal(images[i].ToBytes(), KwdCreatures.WriteRecord(creatures[i], images[i]));
    }

    /// <summary>Q14: DK2's chunk reader takes a CREATURES record of any size, zero-filled to 5449
    /// (<c>0x0056A250</c>). Ours threw on anything but 5449 or 5537.</summary>
    [Theory]
    [InlineData(5000)] [InlineData(5600)]
    public void ARecordOfAnySizeIsRead(int size) => Assert.Equal("a", Read(Source(size)).Name);

    /// <summary>Q11: a job table's mood and mana words are signed (<c>0x00492320</c>).</summary>
    [Fact]
    public void AJobPreferencesMoodAndManaAreSigned()
    {
        byte[] source = Source(5449);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0xE14), 13);
        BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(0xE18), -2);
        BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(0xE1A), -3);
        CreatureJobPreference first = Read(source).HappyJobs[0];
        Assert.Equal((-2, -3), ((int)first.MoodChange, (int)first.ManaChange));
    }

    private static byte[] Source(int size)
    {
        byte[] source = Enumerable.Repeat((byte)0x5A, size).ToArray(); source[0] = (byte)'a'; source[1] = 0; return source;
    }
    private static CreatureDef Read(byte[] source) => KwdCreatures.Parse([
        new KwdChunk { Type = KwdRecordType.Creatures, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
