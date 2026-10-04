// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>Where DK2's mixed-class layer-0 tile chain holds a thing: the tile it was last linked on
/// (<see cref="TileIndex"/>, -1 off the map) and the order of that link. Creatures, objects, traps,
/// doors and shots share one chain per tile; <c>0x004B4E70</c> places a thing at the head and
/// <c>0x004B5020</c> moves it to the head of its new tile's chain only when it changes tile, so the
/// chain runs from the highest <see cref="Order"/> down. Kept by <see cref="WorldState"/> on every
/// position, hand and carry change (keeper-spell-possession.md, Possessed actions).</summary>
public readonly record struct ThingTileLink(int TileIndex, int Order)
{
    public static readonly ThingTileLink None = new(-1, 0);

    public bool IsLinked => TileIndex >= 0;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(TileIndex);
        hash.Add(Order);
    }
}
