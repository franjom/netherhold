// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The persistent decomposition tally belonging to one derived Graveyard room.</summary>
public sealed class GraveyardState
{
    internal GraveyardState(int anchorTileIndex, byte ownerPlayerId)
    {
        AnchorTileIndex = anchorTileIndex;
        OwnerPlayerId = ownerPlayerId;
    }

    /// <summary>The room's deterministic identity; see ADR-0151.</summary>
    public int AnchorTileIndex { get; }

    public byte OwnerPlayerId { get; }

    public int DecomposeValue { get; private set; }

    internal void Add(int value) => DecomposeValue += value;

    internal void Zero() => DecomposeValue = 0;

    internal void Restore(int decomposeValue)
    {
        if (decomposeValue < 0)
            throw new InvalidDataException("Invalid Graveyard decomposition tally in snapshot.");
        DecomposeValue = decomposeValue;
    }

    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(AnchorTileIndex);
        hash.Add(OwnerPlayerId);
        hash.Add(DecomposeValue);
    }
}
