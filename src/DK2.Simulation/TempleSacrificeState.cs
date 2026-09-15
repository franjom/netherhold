// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>One offering remembered by a Temple: kind, definition id, and the
/// level carried only by creature offerings.</summary>
public readonly record struct SacrificeIngredient(byte Type, byte Id, int CreatureLevel)
{
    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(Type);
        hash.Add(Id);
        hash.Add(CreatureLevel);
    }
}

/// <summary>The persistent three-offering ring belonging to one derived Temple room.</summary>
public sealed class TempleSacrificeState
{
    public const int Capacity = 3;

    private readonly SacrificeIngredient[] _entries = new SacrificeIngredient[Capacity];

    internal TempleSacrificeState(int anchorTileIndex, byte ownerPlayerId)
    {
        AnchorTileIndex = anchorTileIndex;
        OwnerPlayerId = ownerPlayerId;
    }

    /// <summary>The room's deterministic identity; see ADR-0149.</summary>
    public int AnchorTileIndex { get; }

    public byte OwnerPlayerId { get; }

    public int Count { get; private set; }

    internal int NextInsertionIndex { get; private set; }

    internal SacrificeIngredient Slot(int index) => _entries[index];

    internal SacrificeIngredient Newest(int offset)
    {
        if ((uint)offset >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(offset));
        int index = (NextInsertionIndex - 1 - offset + Capacity * 2) % Capacity;
        return _entries[index];
    }

    internal void Append(SacrificeIngredient ingredient)
    {
        _entries[NextInsertionIndex] = ingredient;
        NextInsertionIndex = (NextInsertionIndex + 1) % Capacity;
        if (Count < Capacity) Count++;
    }

    internal void Clear()
    {
        Array.Clear(_entries, 0, _entries.Length);
        Count = 0;
        NextInsertionIndex = 0;
    }

    internal void Restore(int count, int nextInsertionIndex, SacrificeIngredient[] entries)
    {
        if ((uint)count > Capacity || (uint)nextInsertionIndex >= Capacity || entries.Length != Capacity)
            throw new InvalidDataException("Invalid Temple sacrifice history in snapshot.");

        Count = count;
        NextInsertionIndex = nextInsertionIndex;
        entries.CopyTo(_entries, 0);
    }

    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(AnchorTileIndex);
        hash.Add(OwnerPlayerId);
        hash.Add(Count);
        hash.Add(NextInsertionIndex);
        foreach (SacrificeIngredient entry in _entries) entry.HashInto(ref hash);
    }
}
