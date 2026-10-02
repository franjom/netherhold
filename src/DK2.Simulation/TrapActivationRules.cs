// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum TrapReadiness : byte
{
    Recharging = 0,
    UnderConstruction = 1,
    InsufficientMana = 2,
    BeingDisarmed = 3,
    Ready = 4,
}

/// <summary>The readiness and charge arithmetic read at 0x004A7D10 and 0x004A84E0, which
/// <see cref="TrapRules"/> dispatches each update (ADR-0321). A Ready result alone does not
/// establish that a trap's firing path is supported: <see cref="TrapRules.FiringBuilt"/> does.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/traps.md")]
public static class TrapActivationRules
{
    public const uint UsesMana = 0x100;

    public static TrapReadiness Readiness(
        TrapDef definition, byte owner, bool underConstruction, long tick,
        long rechargeDeadline, int mana, bool beingDisarmed)
    {
        if (underConstruction) return TrapReadiness.UnderConstruction;
        if (tick < rechargeDeadline) return TrapReadiness.Recharging;
        if ((definition.Flags & UsesMana) != 0 && owner != PlayerIds.Good
            && definition.ManaCostToFire != 0 && definition.ManaCostToFire > mana)
            return TrapReadiness.InsufficientMana;
        return beingDisarmed ? TrapReadiness.BeingDisarmed : TrapReadiness.Ready;
    }

    public static byte AdvanceCharge(byte charge, bool hasTarget) =>
        hasTarget ? unchecked((byte)(charge + 1)) : (byte)0;

    public static bool ChargeReached(TrapDef definition, byte charge) =>
        charge >= SimulationTiming.LoaderFixedSecondsToTicks(definition.ChargeSecondsRaw);
}
