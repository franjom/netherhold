// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

public static partial class KwdCreatures
{
    /// <summary>Overlays decoded fields at either stride. Filtered lists retain their
    /// active source slots; count changes and empty discriminators are refused.</summary>
    public static byte[] WriteRecord(CreatureDef row, KwdRecordImage template)
    {
        int stride = template.ConsumedLength;
        if (stride != RecordSize && stride != RecordSizeEmbedded)
            throw new InvalidDataException("CREATURES requires a 5449- or 5537-byte explicit template.");
        byte[] image = KwdOverlay.Image(template, KwdRecordType.Creatures, stride);
        if (row.SizeClass < 0 || row.SizeClass > 0xFFFFF)
            throw new InvalidDataException("CREATURES SizeClass must fit the decoded upper 20 bits.");
        if (row.AngryArtType == 0 && (row.AngryArtFlags != 0 || row.AngryFrames != 0 || row.AngryFramesPerSecond != 0))
            throw new InvalidDataException("CREATURES Angry fields must be zero when AngryArtType is absent.");
        void U8(int offset, byte value) => image[offset] = value;
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset), value);
        void S16(int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset), value);
        void S32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset), value);
        uint ReadU32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset));
        CreatureDef original = ParseRecord(image, 0);
        KwdOverlay.Ascii(image, 0, 32, row.Name, original.Name, trimmed: false);
        KwdOverlay.Ascii(image, 0xF56, 32, row.SoundCategory, original.SoundCategory, trimmed: false);
        U8(0xF4B, (byte)row.CreatureId);
        U8(OffsetBaseCreatureId, (byte)row.UniqueBaseCreatureId);
        U32(0xEFC, (uint)row.Flags);
        U32(0x1545, (uint)row.Flags2);
        S32(0xE94, row.EntranceOffsetX);
        S32(0xE98, row.EntranceOffsetY);
        S32(0x127B, row.PrayerStepX);
        S32(0x127F, row.PrayerStepY);
        S32(EntranceBlock + 0x44, row.EntranceFrames);
        S32(EntranceBlock + 0x48, row.EntranceFramesPerSecond);
        void AnimationTiming(int slot, CreatureAnimationTiming timing)
        {
            int offset = 0x20 + (slot * 84);
            U8(offset + 0x50, timing.ResourceType);
            U32(offset + 0x40, (uint)timing.Flags);
            if (timing.ResourceType != 6) return;
            U16(offset + 0x44, timing.FrameCount);
            U16(offset + 0x48, timing.FramesPerSecond);
            U8(offset + 0x51, timing.StartSegmentFrames);
            U8(offset + 0x52, timing.EndSegmentFrames);
        }
        AnimationTiming(24, row.KnockdownAnimation);
        // Slot 27 is the Getup block, read two ways (Q538): GetUpAnimation's U16 words (ADR-0350)
        // and GetUpFrames/GetUpFramesPerSecond as S32 (ADR-0348). The S32 view is written only when
        // it was changed, so an unchanged view never clobbers the upper words; changed, it wins.
        AnimationTiming(27, row.GetUpAnimation);
        if (row.GetUpFrames != original.GetUpFrames) S32(GetUpBlock + 0x44, row.GetUpFrames);
        if (row.GetUpFramesPerSecond != original.GetUpFramesPerSecond) S32(GetUpBlock + 0x48, row.GetUpFramesPerSecond);
        AnimationTiming(4, row.KnockdownFallbackAnimation);
        AnimationTiming(1, row.GetUpFallbackAnimation);
        AnimationTiming(35, row.ReaperSummonAnimation);
        AnimationTiming(8, row.ReaperSummonFallbackAnimation);
        // ArtSlots views the same blocks: only a slot it changed is written, so it wins over a view above.
        for (int slot = 0; slot < row.ArtSlots.Count && slot < original.ArtSlots.Count; slot++)
            if (row.ArtSlots[slot] != original.ArtSlots[slot]) AnimationTiming(slot, row.ArtSlots[slot]);
        S32(0xEB4, row.Speed.Raw);
        S32(0xEB8, row.RunSpeed.Raw);
        S32(OffsetShuffleSpeed, row.ShuffleSpeed.Raw);
        S32(0xEA4, row.Height.Raw);
        S32(0xEB0, row.EyeHeight.Raw);
        S32(0xEC8, row.DistanceCanSee.Raw);
        S32(0xECC, row.DistanceCanHear.Raw);
        S32(0xD01, row.PerceptionRange.Raw);
        S32(0xEA0, row.FootprintRadius.Raw);
        U16(0xF20, row.TurnRate);
        U16(0xF0C, row.Hp);
        U16(0xF0E, row.HpFromChicken);
        S32(0xEBC, row.HungerRate.Raw);
        U8(0xF4E, (byte)row.HungerFill);
        U8(0xF4C, (byte)row.Unknown_0F4C);
        S32(0xEC0, row.TimeAwake);
        S32(0xEC4, row.TimeSleep);
        S16(0xEEA, row.OwnLandHealthIncreasePerSecond);
        U16(0xF22, row.DecomposeValue);
        U16(0xF10, row.Fear);
        U16(0xF12, row.Threat);
        U16(0xF14, row.MeleeDamage);
        U16(0xF16, row.SlapDamage);
        S32(0xEEC, row.MeleeRange.Raw);
        S32(0xEF8, row.MeleeRecharge.Raw);
        S32(0xCF9, row.MeleeHitDelay.Raw);
        U8(0xF50, (byte)row.MeleeAttackType);
        S32(0xED0, row.StunDuration.Raw);
        U8(0xF03, (byte)row.FightStyle);
        U16(0xF1C, row.Pay);
        U16(0xF1E, row.MaxGoldHeld);
        U16(0xF34, row.InitialGoldHeld);
        U16(0xF00, row.ExpForNextLevel);
        U16(0xF04, row.ExpPerSecond);
        U16(0xF06, row.ExpPerSecondTraining);
        U16(0xF08, row.ResearchPerSecond);
        U16(0xF0A, row.ManufacturePerSecond);
        U16(0xF18, row.ManaGeneratedByPrayerPerSecond);
        U8(0xF02, (byte)row.JobClass);
        U8(0xF4F, (byte)row.UnhappyThreshold);
        S16(0xF28, row.AngerNoLair);
        S16(0xF2A, row.AngerNoFood);
        S16(0xF2C, row.AngerNoPay);
        S16(0xF2E, row.AngerNoWork);
        S16(0xF30, row.AngerSlap);
        S16(0xF32, row.AngerInHand);
        S16(0xFCB, row.DropDeliveryCalm);
        S16(0x107F, row.TortureHpChange);
        S16(0x1081, row.TortureMoodChange);
        S32(0xEF4, row.TortureTimeToConvert.Raw);
        S32(0xED4, row.GuardDuration.Raw);
        S32(0xED8, row.IdleDuration.Raw);
        S32(0xEDC, row.SlapFearlessDuration.Raw);
        S16(0xEE8, row.PossessionManaCost);
        U8(0xF52, (byte)row.LairObjectId);
        U16(0xF24, row.NameStringId);
        U16(0xF26, row.TooltipStringId);
        U16(0xF38, row.DescriptionStringId);
        U16(0x14DF, row.SpecialNameStringId);
        U16(0xCFF, row.IntroductionStringId);
        U16(0xD05, row.UnhappySpeechId);
        U16(0xD07, row.AngrySpeechId);
        U16(0xD09, row.LeftSpeechId);
        U16(0xF36, row.EntranceEffectId);
        U32(0xEA8, ((uint)row.SizeClass << 12) | (ReadU32(0xEA8) & 0xFFF));
        U8(AngryBlock + 0x50, row.AngryArtType);
        if (row.AngryArtType != 0)
        {
            U32(AngryBlock + 0x40, row.AngryArtFlags);
            S32(AngryBlock + 0x44, row.AngryFrames);
            S32(AngryBlock + 0x48, row.AngryFramesPerSecond);
        }
        if (row.Attractions is null || row.Attractions.Count != 2)
            throw new InvalidDataException("CREATURES requires exactly two attraction slots.");
        for (int i = 0; i < 2; i++)
        {
            int offset = 0xDAC + i * 8;
            CreatureAttraction value = row.Attractions[i];
            U32(offset, value.Present); U16(offset + 4, value.RoomId); U16(offset + 6, value.RoomSize);
        }
        WriteFiltered("Spells", row.Spells, 0xDC4, 3, 24, off => image[off + 22] != 0, (off, value) =>
        {
            if (value.CreatureSpellId == 0) throw new InvalidDataException("CREATURES active spell id cannot be zero.");
            U8(off + 22, value.CreatureSpellId); U8(off + 23, value.LevelAvailable);
            S32(off + 16, value.ShotDelay.Raw); U8(off + 13, value.PlayAnimation);
            S32(off, value.OriginX); S32(off + 4, value.OriginY); S32(off + 8, value.OriginZ);
        });
        WriteFiltered("Resistances", row.Resistances, 0xE0C, 4, 2, off => image[off] != 0, (off, value) =>
        {
            if (value.AttackType == 0) throw new InvalidDataException("CREATURES active resistance type cannot be zero.");
            U8(off, (byte)value.AttackType); U8(off + 1, value.Value);
        });
        void Preferences(string name, IReadOnlyList<CreatureJobPreference> values, int offset, int count) =>
            WriteFiltered(name, values, offset, count, 12, off => ReadU32(off) != 0, (off, value) =>
            {
                if (value.Job == JobType.None) throw new InvalidDataException("CREATURES active preference job cannot be None.");
                U32(off, (uint)value.Job); S16(off + 4, value.MoodChange);
                S16(off + 6, value.ManaChange); U8(off + 8, value.ChancePercent);
            });
        Preferences("HappyJobs", row.HappyJobs, 0xE14, 3);
        Preferences("UnhappyJobs", row.UnhappyJobs, 0xE38, 2);
        Preferences("AngryJobs", row.AngryJobs, 0xE50, 3);
        WriteFiltered("HateJobs", row.HateJobs, 0xE74, 2, 4, off => ReadU32(off) != 0, (off, value) =>
        {
            if (value == JobType.None) throw new InvalidDataException("CREATURES active hated job cannot be None.");
            U32(off, (uint)value);
        });
        WriteFiltered("AlternativeJobs", row.AlternativeJobs, 0xE7C, 3, 8, off => ReadU32(off) != 0, (off, value) =>
        {
            if (value.Job == JobType.None) throw new InvalidDataException("CREATURES active alternative job cannot be None.");
            U32(off, (uint)value.Job); S16(off + 4, value.MoodChange); S16(off + 6, value.ManaChange);
        });
        return image;
    }

    private static void WriteFiltered<T>(string name, IReadOnlyList<T> values, int offset, int count,
        int stride, Func<int, bool> active, Action<int, T> write)
    {
        var slots = new List<int>(count);
        for (int i = 0; i < count; i++) if (active(offset + i * stride)) slots.Add(offset + i * stride);
        if (values is null || values.Count != slots.Count)
            throw new InvalidDataException($"CREATURES {name} requires the template's {slots.Count} active slots; shape changes are unsupported.");
        for (int i = 0; i < slots.Count; i++) write(slots[i], values[i]);
    }
}
