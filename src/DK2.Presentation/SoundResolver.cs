// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>The sound system's own LCG; never uses the simulation's RNG.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/sound-playback.md#category-and-sample-selection")]
public sealed class SoundRand(uint seed)
{
    private uint _state = seed;
    public uint Next()
    {
        _state = unchecked(_state * 1664525u + 1013904223u);
        return _state >> 13 | _state << 19;
    }
}

[Dk2Behavior(Confidence.Confirmed, "docs/research/sound-playback.md#category-and-sample-selection")]
public sealed class SoundResolver(BankMap banks, SfxMap map)
{
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
