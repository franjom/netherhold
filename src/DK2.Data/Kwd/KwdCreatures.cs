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
public static class KwdCreatures
{
    private const int RecordSize = 5449;
    private const int RecordSizeEmbedded = 5537;

    public static List<CreatureDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<CreatureDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;

            int recordSize = chunk.ItemCount == 0 ? RecordSize : chunk.Content.Length / chunk.ItemCount;
            if (recordSize != RecordSize && recordSize != RecordSizeEmbedded)
                throw new InvalidDataException(
                    $"CREATURES record size {recordSize} is neither {RecordSize} nor {RecordSizeEmbedded}.");
            if (chunk.Content.Length != recordSize * chunk.ItemCount)
                throw new InvalidDataException("CREATURES content size does not divide into records.");

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRecord(chunk.Content, i * recordSize));
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

        // Spells[3] at 0xDC4 (24 bytes each): Vector3f(12), 4 bytes, shotDelay fp32, 2 bytes, spellId, level.
        var spells = new List<CreatureSpellSlot>(3);
        for (int i = 0; i < 3; i++)
        {
            int off = 0xDC4 + i * 24;
            byte spellId = U8(off + 22);
            if (spellId != 0)
                spells.Add(new CreatureSpellSlot(spellId, U8(off + 23), Fp(off + 16), U8(off + 13)));
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
                    list.Add(new CreatureJobPreference(job, U16(off + 4), U16(off + 6), U8(off + 8)));
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
                altJobs.Add(new CreatureJobAlternative(job, U16(off + 4), U16(off + 6)));
        }

        return new CreatureDef
        {
            Name = Str(0x00, 32),
            CreatureId = U8(0xF4B),
            Flags = (CreatureFlags)U32(0xEFC),

            // The record's last four bytes, and part of the 72-byte tail Q70
            // says nobody had accounted for. See CreatureFlags2 for how it was
            // located: by the roster its bit 3 has to select, not by layout.
            Flags2 = (CreatureFlags2)U32(0x1545),

            Speed = Fp(0xEB4),
            RunSpeed = Fp(0xEB8),
            ShuffleSpeed = Fp(0xD37),
            Height = Fp(0xEA4),
            EyeHeight = Fp(0xEB0),
            DistanceCanSee = Fp(0xEC8),
            DistanceCanHear = Fp(0xECC),
            PerceptionRange = Fp(0xD01),

            Hp = U16(0xF0C),
            HpFromChicken = U16(0xF0E),
            HungerRate = Fp(0xEBC),
            HungerFill = U8(0xF4E),
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
            CloneCreatureId = U8(0xD38),

            NameStringId = U16(0xF24),
            TooltipStringId = U16(0xF26),
            IntroductionStringId = U16(0xCFF),
        };
    }
}
