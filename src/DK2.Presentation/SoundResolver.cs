// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>The sound system's own LCG; never uses the simulation's RNG.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-playback.md#category-and-sample-selection")]
public sealed class SoundRand(uint seed)
{
    private uint _state = seed;
    public uint Next()
    {
        _state = unchecked(_state * 1664525u + 1013904223u);
        return _state >> 13 | _state << 19;
    }
}

/// <summary>A voice's QMixer distance mapping in mixer units (two per tile, <c>0x0060E4B0</c>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/qmixer-distance.md")]
public readonly record struct SoundDistanceMapping(float Minimum, float Maximum, float Scale)
{
    public const float UnitsPerTile = 2f;

    /// <summary><c>0x0060CDB0</c>'s mapping for an item: 2.0, the item's float <c>+0x22</c> (file bytes 26..29 of
    /// <see cref="SoundItem.Unknown0x08"/>) floored to 2.1 when 2.0 or less, and 0.5.</summary>
    public static SoundDistanceMapping Of(SoundItem item)
    {
        float maximum = BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(item.Unknown0x08.AsSpan(0x22 - 8)));
        // FCOMP then TEST AH,0x41 also floors an unordered (NaN) value.
        return new SoundDistanceMapping(2f, maximum > 2f ? maximum : 2.1f, 0.5f);
    }

    /// <summary>QMixer's distance factor, a linear amplitude (QMIXER.DLL <c>0x18009150</c>, <c>0x18009460</c>,
    /// qmixer-distance.md): 0 beyond <see cref="Maximum"/>, 1 within <see cref="Minimum"/>, otherwise
    /// <c>min / (min + scale × (d − min))</c>; DK2's mapping gives 4 / (d + 2), 2 / (tiles + 1).</summary>
    public float Factor(float distanceUnits)
    {
        if (distanceUnits > Maximum) return 0f;
        if (distanceUnits <= Minimum || Scale == 0f) return 1f;
        return Minimum / (Minimum + (Scale * (distanceUnits - Minimum)));
    }

    /// <summary>The range a rolloff curve spans, in mixer units: a hair past <see cref="Maximum"/>, so the curve can
    /// fall to silence there (an engine curve holds its last value beyond its range).</summary>
    public float CurveRange => Maximum * 1.01f;

    /// <summary><paramref name="into"/>'s points of the rolloff curve over <see cref="CurveRange"/>: x the fraction
    /// of the range, y <see cref="Factor"/>, at 0, at <see cref="Minimum"/> and geometrically spaced out to
    /// <see cref="Maximum"/> (dense where the curve bends); the last point steps to 0.</summary>
    public void RolloffPoints(List<(float X, float Y)> into, int samples = 32)
    {
        into.Clear();
        float range = CurveRange;
        into.Add((0f, Factor(0f)));
        for (int i = 0; i < samples; i++)
        {
            float d = i == samples - 1 ? Maximum : Minimum * (float)Math.Pow(Maximum / Minimum, (double)i / (samples - 1));
            into.Add((d / range, Factor(d)));
        }
        into.Add((1f, 0f));
    }
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-playback.md#category-and-sample-selection")]
public sealed class SoundResolver(BankMap banks, SfxMap map)
{
    /// <summary>Whether the group repeats on its own: bit <c>0x08</c> of its flag word (group <c>+0x10</c>) makes the instance flags 6, and flag 4 gives the
    /// voice a repeat count of 9999 (<c>0x0060C960</c>, <c>0x0060CDB0</c>). Only the fireball's and firebomb's <c>0xF2</c> trail has it in the shipped maps: the other
    /// long sounds (flags <c>0x04</c> and <c>0x02</c>) end with their sample and are asked for again by their thing.</summary>
    public bool IsLooping(SoundId id) =>
        map.Groups.FirstOrDefault(g => g.Id == id) is { Items.Count: > 0 } group
        && (System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(group.Unknown0x08.AsSpan(8)) & 0x08) != 0;
    /// <summary>Whether the group has exactly one item with samples, the only kind <see cref="Resolve"/> plays; a linked or delayed group has its own director.</summary>
    public bool IsSingleItem(SoundId id) =>
        map.Groups.FirstOrDefault(g => g.Id == id) is { Items.Count: 1 } group && group.Items[0].Sounds.Count > 0;

    /// <summary>The voice's distance mapping in mixer units, two to a tile (<c>0x0060E4B0</c>), as <c>SoundInstance_BindVoice</c>
    /// (<c>0x0060CDB0</c>) builds it for QMixer: minimum 2.0 (one tile), maximum the item's float at <c>+0x22</c> raised to 2.1
    /// when it is 2.0 or less, scale 0.5 (<c>0x0060CFE0</c>). Null for a group whose flag word has <c>0x200</c>: that voice is
    /// head-relative and gets no mapping. Uses the group's first item, the one <see cref="Resolve"/> plays. Not mirrored:
    /// an instance started with a range (instance flag <c>0x200</c>, <c>0x0060CF12..0x0060CF27</c>) takes the mixer ratio
    /// times that range instead of the item's float; which requests pass one is unread.</summary>
    public SoundDistanceMapping? DistanceMappingOf(SoundId id)
    {
        SoundGroup? group = map.Groups.FirstOrDefault(g => g.Id == id);
        if (group is not { Items.Count: > 0 }) return null;
        if ((System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(group.Unknown0x08.AsSpan(8)) & 0x200) != 0) return null;
        return SoundDistanceMapping.Of(group.Items[0]);
    }

    public SoundPriority? PriorityOf(SoundId id)
    {
        SoundGroup? group = map.Groups.FirstOrDefault(g => g.Id == id);
        return group is { Items.Count: > 0 } ? new SoundPriority(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(group.Unknown0x08.AsSpan(4))) : null;
    }
    public ResolvedSound? Resolve(SoundId id, SoundRand random)
    {
        SoundGroup? group = map.Groups.FirstOrDefault(g => g.Id == id);
        // Linked/delayed multi-item players are selected by their own directors.
        return group is { Items.Count: 1 } ? ResolveItem(group.Items[0], random) : null;
    }

    /// <summary>Every sample the group's items can pick, without drawing from the sound LCG: what a decoder warms
    /// before the group is played. Entries that point outside the bank map are skipped.</summary>
    public void SamplesOf(SoundId id, List<ResolvedSound> into)
    {
        into.Clear();
        foreach (SoundGroup group in map.Groups)
        {
            if (group.Id != id) continue;
            foreach (SoundItem item in group.Items)
                foreach (SoundSample sample in item.Sounds)
                    if (Locate(banks, sample) is ResolvedSound found) into.Add(found);
            return;
        }
    }

    /// <summary>A sample's bank entry, or null when it points outside the bank map.</summary>
    public static ResolvedSound? Locate(BankMap banks, SoundSample sample)
    {
        int bank = sample.Archive.Value - 1;
        if (bank < 0 || bank >= banks.Archives.Count || sample.Index == 0 || sample.Index > int.MaxValue) return null;
        return new ResolvedSound(banks.Archives[bank].Name, new SoundEntryId((int)sample.Index - 1));
    }

    public ResolvedSound? ResolveItem(SoundItem item, SoundRand random)
    {
        if (item.Sounds.Count == 0) return null;
        SoundSample sample = item.Sounds[0];
        if (item.Sounds.Count > 1)
        {
            uint draw = random.Next() >> 16;
            foreach (SoundSample candidate in item.Sounds)
                if (draw <= candidate.CumulativeWeight) { sample = candidate; break; }
        }
        int bank = sample.Archive.Value - 1;
        if (bank < 0 || bank >= banks.Archives.Count || sample.Index == 0 || sample.Index > int.MaxValue)
            throw new InvalidDataException("Sound map sample references an invalid archive or entry.");
        return new ResolvedSound(banks.Archives[bank].Name, new SoundEntryId((int)sample.Index - 1));
    }
}
