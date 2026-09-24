// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A creature's own gold, <see cref="CreatureState.GoldHeld"/> (<c>creature+0x3FB</c>):
/// an imp's load and a fighter's purse are one field. Half of every wage lands in it
/// (state 189) and the Casino pays into it (<c>payday.md</c> §4.4, ADR-0187).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/payday.md")]
public static class PurseRules
{
    /// <summary>DK2's <c>0x0048AE70(amount)</c>: adds to the purse and caps it at the level-scaled
    /// <c>MaxGoldHeld</c>, so what does not fit is lost. Returns what landed.</summary>
    public static int Receive(
        GameDatabase database, CreatureDef creature, CreatureState state, int amount)
    {
        int before = state.GoldHeld;
        int cap = CreatureLevelRules.MaxGoldHeld(database, creature, state.Level);
        state.GoldHeld = (int)Math.Min((long)before + amount, cap);
        return state.GoldHeld - before;
    }

    /// <summary>The share of a wage that goes into the purse: half, rounded down
    /// (<c>taken / 2</c> in state 189).</summary>
    public static int PocketedFromWage(int taken) => taken / 2;
}
