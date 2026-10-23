// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Parser for CREATURES(170) records.
///
/// Record size is 5449 bytes in standalone Creatures.kwd; a 5537-byte variant
/// (88 extra trailing bytes) occurs when creature data is embedded in globals
/// files. Field offsets CONFIRMED via the dk2-tools template's self-consistent
/// offset anchors (docs/research/kwd-creatures.md). Gameplay fields only; the
/// ~45 ArtResource blocks are skipped except where a name is useful later.
/// </summary>
public static partial class KwdCreatures
{
    private const int RecordSize = 5449;
    private const int RecordSizeEmbedded = 5537;
    private const int OffsetShuffleSpeed = 0xD37;
    private const int OffsetBaseCreatureId = 0xD3B;

    /// <summary>Animation block 30, Entrance, which action 49 plays (<c>0x006E4739</c> = 30).</summary>
    private const int EntranceBlock = 0x20 + (30 * 84);

    /// <summary>Animation block 9, Angry, which action 41 plays (<c>0x006E4651</c> = 9).</summary>
    private const int AngryBlock = 0x20 + (9 * 84);

    /// <summary>Animation block 27, Getup, which action 53 plays (<c>0x006E47AD</c> = 27).</summary>
    private const int GetUpBlock = 0x20 + (27 * 84);

    public static List<CreatureDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<CreatureDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;

            // Any record size (Q14): the chunk reader 0x0056A250 zero-fills a 5449-byte record and
            // reads the file's record into it, and nothing reads past 0x1548; the 5537-byte variant
            // is one shipped case.
            int recordSize = chunk.ItemCount == 0 ? RecordSize : chunk.Content.Length / chunk.ItemCount;
            if (chunk.Content.Length != recordSize * chunk.ItemCount)
                throw new InvalidDataException("CREATURES content size does not divide into records.");

            var staged = new byte[RecordSize];
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                Array.Clear(staged, 0, staged.Length);
                Array.Copy(chunk.Content, i * recordSize, staged, 0, Math.Min(recordSize, RecordSize));
                result.Add(ParseRecord(staged, 0));
            }
        }
        return result;
    }

    private static CreatureDef ParseRecord(byte[] rec, int b)
    {
        // Primitive readers, offset-addressed relative to the record base.
        ushort U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + off));
        short S16(int off) => BinaryPrimitives.ReadInt16LittleEndian(rec.AsSpan(b + off));
        uint U32(int off) => BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + off));
        int S32(int off) => BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + off));
        byte U8(int off) => rec[b + off];
        Fp32 Fp(int off) => new(BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + off)));
        string Str(int off, int len)
        {
            ReadOnlySpan<byte> s = rec.AsSpan(b + off, len);
            int end = s.IndexOf((byte)0);
            return Encoding.ASCII.GetString(end < 0 ? s : s[..end]);
        }

        // Attractions[2] at 0xDAC (8 bytes each).
        var attractions = new List<CreatureAttraction>(2);
        for (int i = 0; i < 2; i++)
        {
            int off = 0xDAC + i * 8;
            attractions.Add(new CreatureAttraction(U32(off), U16(off + 4), U16(off + 6)));
        }

        // Spells[3] at 0xDC4 (24 bytes each): the origin vector (3 × int32), 4 bytes, shotDelay fp32, 2 bytes, spellId, level.
        var spells = new List<CreatureSpellSlot>(3);
        for (int i = 0; i < 3; i++)
        {
            int off = 0xDC4 + i * 24;
            byte spellId = U8(off + 22);
            if (spellId != 0)
                spells.Add(new CreatureSpellSlot(spellId, U8(off + 23), Fp(off + 16), U8(off + 13),
                    Fp(off).Raw, Fp(off + 4).Raw, Fp(off + 8).Raw));
        }

        // Resistances[4] at 0xE0C (2 bytes each).
        var resistances = new List<CreatureResistance>(4);
        for (int i = 0; i < 4; i++)
        {
            int off = 0xE0C + i * 2;
            if (U8(off) != 0)
                resistances.Add(new CreatureResistance((AttackType)U8(off), U8(off + 1)));
        }

        // Job preference tables. JobPreference = 12 bytes: u32 job, u16 mood, u16 mana, u8 chance, 3 unk.
        List<CreatureJobPreference> Prefs(int baseOff, int count)
        {
            var list = new List<CreatureJobPreference>(count);
            for (int i = 0; i < count; i++)
            {
                int off = baseOff + i * 12;
                var job = (JobType)U32(off);
                if (job != JobType.None)
                    list.Add(new CreatureJobPreference(job, S16(off + 4), S16(off + 6), U8(off + 8)));
            }
            return list;
        }

        var happyJobs = Prefs(0xE14, 3);
        var unhappyJobs = Prefs(0xE38, 2);
        var angryJobs = Prefs(0xE50, 3);

        var hateJobs = new List<JobType>(2);
        for (int i = 0; i < 2; i++)
        {
            var job = (JobType)U32(0xE74 + i * 4);
            if (job != JobType.None) hateJobs.Add(job);
        }

        // JobAlternative = 8 bytes: u32 job, u16 mood, u16 mana.
        var altJobs = new List<CreatureJobAlternative>(3);
        for (int i = 0; i < 3; i++)
        {
            int off = 0xE7C + i * 8;
            var job = (JobType)U32(off);
            if (job != JobType.None)
                altJobs.Add(new CreatureJobAlternative(job, S16(off + 4), S16(off + 6)));
        }

        CreatureAnimationTiming AnimationTiming(int slot) => AnimationTimingAt(0x20 + (slot * 84));

        CreatureAnimationTiming AnimationTimingAt(int offset)
        {
            byte type = U8(offset + 0x50);
            return new CreatureAnimationTiming
            {
                ResourceType = type,
                Flags = (CreatureAnimationFlags)U32(offset + 0x40),
                FrameCount = type == 6 ? U16(offset + 0x44) : (ushort)0,
                FramesPerSecond = type == 6 ? U16(offset + 0x48) : (ushort)0,
                StartSegmentFrames = type == 6 ? U8(offset + 0x51) : (byte)0,
                EndSegmentFrames = type == 6 ? U8(offset + 0x52) : (byte)0,
            };
        }

        var artSlots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        for (int slot = 0; slot < artSlots.Length; slot++) artSlots[slot] = AnimationTiming(slot);

        return new CreatureDef
        {
            Name = Str(0x00, 32),
            CreatureId = U8(0xF4B),
            UniqueBaseCreatureId = U8(OffsetBaseCreatureId),
            Flags = (CreatureFlags)U32(0xEFC),

            // The record's last four bytes, and part of the 72-byte tail Q70
            // says nobody had accounted for. See CreatureFlags2 for how it was
            // located: by the roster its bit 3 has to select, not by layout.
            Flags2 = (CreatureFlags2)U32(0x1545),

            // The portal step (loader source +0x1284, less the 0x3F0 shift) and the Entrance
            // animation: art block 30 of 84 bytes from 0x20, frames at +0x44 and rate at +0x48.
            EntranceOffsetX = S32(0xE94),
            EntranceOffsetY = S32(0xE98),
            // The definition's vector 1 (temple-states.md §5): the reader's +0x166B, file 0x127B.
            PrayerStepX = S32(0x127B),
            PrayerStepY = S32(0x127F),
            EntranceFrames = S32(EntranceBlock + 0x44),
            EntranceFramesPerSecond = S32(EntranceBlock + 0x48),
            // The Getup animation (block 27), which action 53 plays while state 257 waits.
            GetUpFrames = S32(GetUpBlock + 0x44),
            GetUpFramesPerSecond = S32(GetUpBlock + 0x48),
            // The Angry block (9): flags at +0x40, type byte at +0x50; the loader leaves the runtime
            // record empty when the type is 0 (no resource), so the flags are taken only with a type.
            AngryArtType = U8(AngryBlock + 0x50),
            AngryArtFlags = U8(AngryBlock + 0x50) != 0 ? U32(AngryBlock + 0x40) : 0,
            AngryFrames = U8(AngryBlock + 0x50) != 0 ? S32(AngryBlock + 0x44) : 0,
            AngryFramesPerSecond = U8(AngryBlock + 0x50) != 0 ? S32(AngryBlock + 0x48) : 0,
            KnockdownAnimation = AnimationTiming(24),
            GetUpAnimation = AnimationTiming(27),
            KnockdownFallbackAnimation = AnimationTiming(4),
            GetUpFallbackAnimation = AnimationTiming(1),
            ReaperSummonAnimation = AnimationTiming(35),
            ReaperSummonFallbackAnimation = AnimationTiming(8),
            ArtSlots = artSlots,
            AlternateArt = AnimationTimingAt(0x1083),

            Speed = Fp(0xEB4),
            RunSpeed = Fp(0xEB8),
            ShuffleSpeed = Fp(OffsetShuffleSpeed),
            Height = Fp(0xEA4),
            EyeHeight = Fp(0xEB0),
            DistanceCanSee = Fp(0xEC8),
            DistanceCanHear = Fp(0xECC),
            PerceptionRange = Fp(0xD01),
            FootprintRadius = Fp(0xEA0),
            SizeClass = (int)(U32(0xEA8) >> 12),
            TurnRate = U16(0xF20),

            Hp = U16(0xF0C),
            HpFromChicken = U16(0xF0E),
            HungerRate = Fp(0xEBC),
            HungerFill = U8(0xF4E),
            Unknown_0F4C = U8(0xF4C),
            TimeAwake = S32(0xEC0),
            TimeSleep = S32(0xEC4),
            OwnLandHealthIncreasePerSecond = S16(0xEEA),
            DecomposeValue = U16(0xF22),

            Fear = U16(0xF10),
            Threat = U16(0xF12),
            MeleeDamage = U16(0xF14),
            SlapDamage = U16(0xF16),
            MeleeRange = Fp(0xEEC),
            MeleeRecharge = Fp(0xEF8),
            MeleeHitDelay = Fp(0xCF9),
            MeleeAttackType = (AttackType)U8(0xF50),
            StunDuration = Fp(0xED0),
            FightStyle = (FightStyle)U8(0xF03),
            Resistances = resistances,
            Spells = spells,

            Pay = U16(0xF1C),
            MaxGoldHeld = U16(0xF1E),
            InitialGoldHeld = U16(0xF34),
            ExpForNextLevel = U16(0xF00),
            ExpPerSecond = U16(0xF04),
            ExpPerSecondTraining = U16(0xF06),
            ResearchPerSecond = U16(0xF08),
            ManufacturePerSecond = U16(0xF0A),
            ManaGeneratedByPrayerPerSecond = U16(0xF18),
            JobClass = (JobClass)U8(0xF02),

            UnhappyThreshold = U8(0xF4F),
            // All six signed, and the last two are why: read as u16 they come back as 60536 and
            // 65286, which are -5000 and -250 in a 16-bit field — being slapped or picked up
            // *calms* most creatures rather than angering them. The first four are small positives
            // on every shipped creature (0..150), so signed reads them identically and is strictly
            // the safer choice. docs/research/kwd-creatures.md had the whole run as u16, which is
            // what checking one field of a run rather than the run costs.
            AngerNoLair = S16(0xF28),
            AngerNoFood = S16(0xF2A),
            AngerNoPay = S16(0xF2C),
            AngerNoWork = S16(0xF2E),
            AngerSlap = S16(0xF30),
            AngerInHand = S16(0xF32),
            DropDeliveryCalm = S16(0xFCB),
            TortureHpChange = S16(0x107F),
            TortureMoodChange = S16(0x1081),
            TortureTimeToConvert = Fp(0xEF4),

            HappyJobs = happyJobs,
            UnhappyJobs = unhappyJobs,
            AngryJobs = angryJobs,
            HateJobs = hateJobs,
            AlternativeJobs = altJobs,
            Attractions = attractions,

            GuardDuration = Fp(0xED4),
            IdleDuration = Fp(0xED8),
            SlapFearlessDuration = Fp(0xEDC),
            PossessionManaCost = S16(0xEE8),
            LairObjectId = U8(0xF52),
            CloneCreatureId = U8(OffsetBaseCreatureId),

            NameStringId = U16(0xF24),
            TooltipStringId = U16(0xF26),
            DescriptionStringId = U16(0xF38),
            IntroductionStringId = U16(0xCFF),
            UnhappySpeechId = U16(0xD05),
            AngrySpeechId = U16(0xD07),
            LeftSpeechId = U16(0xD09),
            EntranceEffectId = U16(0xF36),
        };
    }
}
