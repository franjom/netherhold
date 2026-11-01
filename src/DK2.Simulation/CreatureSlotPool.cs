// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

internal readonly record struct NativeCreatureTag(ushort Value)
{
    public byte InitialFightPhase => (byte)(Value % 10);
}

/// <summary>004B5A10 constructs tags 8..455 after the seven players;
/// 004B7900 takes the head and 004B7A20 returns a slot to the head.</summary>
internal sealed class CreatureSlotPool
{
    internal const int Capacity = 448;
    private readonly List<NativeCreatureTag> _free = Enumerable.Range(8, Capacity)
        .Select(value => new NativeCreatureTag((ushort)value)).ToList();

    internal IReadOnlyList<NativeCreatureTag> FreeTags => _free;

    internal NativeCreatureTag Take()
    {
        if (_free.Count == 0) throw new InvalidOperationException("The native creature pool is full.");
        NativeCreatureTag tag = _free[_free.Count - 1];
        _free.RemoveAt(_free.Count - 1);
        return tag;
    }

    internal void Return(NativeCreatureTag tag)
    {
        if (tag.Value < 8 || tag.Value > 455 || _free.Contains(tag))
            throw new InvalidOperationException("The native creature slot is invalid or already free.");
        _free.Add(tag);
    }

    internal void Restore(IEnumerable<NativeCreatureTag> tags)
    {
        NativeCreatureTag[] saved = tags.ToArray();
        if (saved.Length > Capacity || saved.Any(tag => tag.Value < 8 || tag.Value > 455)
            || saved.Distinct().Count() != saved.Length)
            throw new InvalidDataException("Invalid native creature free slots in snapshot.");
        _free.Clear();
        _free.AddRange(saved);
    }

    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(_free.Count);
        foreach (NativeCreatureTag tag in _free) hash.Add(tag.Value);
    }

    internal bool IsValidPartition(IEnumerable<CreatureState> creatures)
    {
        var allocated = new HashSet<NativeCreatureTag>();
        foreach (CreatureState creature in creatures)
        {
            if (creature.NativeSlotTag == 0)
            {
                if (!CreatureDeathRules.IsCorpse(creature)) return false;
                continue;
            }
            var tag = new NativeCreatureTag(creature.NativeSlotTag);
            if (tag.Value < 8 || tag.Value > 455 || _free.Contains(tag) || !allocated.Add(tag)) return false;
        }
        return allocated.Count + _free.Count == Capacity;
    }
}
