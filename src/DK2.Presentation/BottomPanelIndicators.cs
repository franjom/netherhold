// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A slot's progress sectors (panel-indicators.md section 7), each null when the slot is not
/// its subject. A spell slot can carry a recharge and a research sector at once (an upgrade being
/// researched while the spell recharges), so these are three fields, not one kind.</summary>
/// <param name="RechargePercent">Set while recharging (p != 100), 0 included: DK2's branch is
/// <c>angle != 3π || p == 0</c>, so the base icon is tinted <c>0x666666</c> with no wedge area.</param>
/// <param name="ManufacturePercent">Set on the head order's door or trap with p != 0.</param>
/// <param name="ResearchPercent">Set on the research target's slot with p != 0.</param>
/// <param name="ResearchOnUnknown">Slot state 2 (availability 2 or 4): the sector is the slot's own
/// icon ("tba" only at availability 2) in (1, 0.3, 0.3); otherwise catalogue 261
/// <c>gui\icons\Gold_Frame</c>.</param>
public readonly record struct BottomPanelSlotIndicators(byte? RechargePercent, byte? ManufacturePercent,
    byte? ResearchPercent, bool ResearchOnUnknown)
{
    public static readonly BottomPanelSlotIndicators None = default;
}

/// <summary>The three progress percents the slot draw <c>0x00412FC0</c> reads, in DK2's integer
/// arithmetic. The caller keeps each result's low byte (<c>AND EAX,0xFF</c> at <c>0x00413C4B</c>).</summary>
public static class BottomPanelProgress
{
    /// <summary><c>PlayerSpells_RechargePercent</c> 0x004BE430: 100 when never cast or
    /// <c>now − lastCast ≥ cooldown</c> (signed), else floor(floor(0x64000 / cooldown) × elapsed / 4096).
    /// The cooldown is the expression <see cref="KeeperSpellRules"/> CanCast uses, so 100 here is
    /// exactly "CanCast passes the cooldown" at the same tick.</summary>
    public static byte RechargePercent(long lastCastTick, long now, int cooldownRaw)
    {
        if (lastCastTick == 0) return 100;
        long cooldown = (uint)unchecked(cooldownRaw * SimulationTiming.OriginalTicksPerGameSecond) >> 12;
        long elapsed = now - lastCastTick;
        if (elapsed >= cooldown) return 100;
        // DK2 divides by zero here only for a zero cooldown with a negative elapsed; ours cannot rewind.
        if (cooldown == 0) return 100;
        return unchecked((byte)((0x64000 / cooldown * elapsed) >> 12));
    }

    /// <summary><c>ManufactureQueue_ProgressPercent</c> 0x00506B70: 0 for an empty queue, else
    /// <c>(points &amp; 0xFFFF) × 100 / cost word</c>, signed. DK2 divides by the cost at ring slot 0
    /// (Q618); a zero cost, which would fault DK2, returns 0.</summary>
    public static byte ManufacturePercent(int points, int slotZeroCost)
    {
        int cost = (ushort)slotZeroCost;
        return cost == 0 ? (byte)0 : unchecked((byte)((points & 0xFFFF) * 100 / cost));
    }

    /// <summary><c>ResearchList_ProgressPercent</c> 0x00507060: 100 when points ≥ cost (unsigned
    /// compare, so a zero cost is 100), else points × 100 / cost, unsigned and wrapping.</summary>
    public static byte ResearchPercent(int points, int cost)
    {
        uint have = (uint)points, need = (uint)cost;
        return have >= need ? (byte)100 : unchecked((byte)(have * 100u / need));
    }
}
