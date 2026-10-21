// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>One of the world's six marked-creature records (world <c>+0x72BD + 7i</c>,
/// <c>0x005096A0</c>/<c>0x005096C0</c>): the creature's tag and a byte value, both 0 when free.
/// Trigger action 42 fills and clears them; the renderer draws the Objective ring over a creature
/// that holds one (<c>0x00596770</c>) and, for a hero, picks its centre icon by the value
/// (<c>0x00597270</c>). The record's dword at <c>+3</c>, which Creature_Update sets under a test
/// not yet read (<c>0x0048A1FB</c>), is not kept.</summary>
public record struct HeroSlot
{
    /// <summary>The creature's id, standing in for its tag; 0 when the slot is free.</summary>
    public int CreatureId;
    public byte Value;

    public readonly bool IsFree => CreatureId == 0;

    internal readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(CreatureId);
        hash.Add(Value);
    }
}

public static class HeroSlotRules
{
    /// <summary>Trigger action 42 (<c>0x0049AC80</c> arm <c>0x2A</c>, byte 0): a non-zero value puts
    /// the creature in the first free slot with that value, without checking whether it holds one
    /// already, and does nothing when all six are taken (<c>0x0049B07A..0x0049B0C5</c>); zero frees
    /// the first slot holding it (<c>0x0049B004..0x0049B05B</c>), and, when it found one, clears
    /// <c>+0x3EA</c> bit <c>0x20000</c>, a flag with no field here yet.</summary>
    public static void Mark(HeroSlot[] slots, int creatureId, byte value)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (value != 0 ? !slots[i].IsFree : slots[i].CreatureId != creatureId) continue;
            slots[i] = value != 0 ? new HeroSlot { CreatureId = creatureId, Value = value } : default;
            return;
        }
    }
}
