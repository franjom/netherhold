// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public class TrapActivationTests
{
    private static readonly TrapDef Definition = new()
    {
        Name = "activation fixture", TrapId = 3, Health = 1500, GoldCost = 1000,
        ManufactureRequired = 1500, ShotTypeId = 44, ManaUpkeepPerSecond = 0,
        Flags = TrapActivationRules.UsesMana, ManaCostToFire = 250,
    };

    [Theory]
    [InlineData(true, 9u, 10u, 0, true, TrapReadiness.UnderConstruction)]
    [InlineData(false, 9u, 10u, 0, true, TrapReadiness.Recharging)]
    [InlineData(false, 10u, 10u, 249, true, TrapReadiness.InsufficientMana)]
    [InlineData(false, 10u, 10u, 250, true, TrapReadiness.BeingDisarmed)]
    [InlineData(false, 10u, 10u, 250, false, TrapReadiness.Ready)]
    public void BinaryInferredReadinessPreservesGateOrderAndInclusiveRecharge(
        bool construction, uint tick, uint deadline, int mana, bool disarmer, TrapReadiness expected)
    {
        Assert.Equal(expected, TrapActivationRules.Readiness(
            Definition, PlayerIds.FirstKeeper, construction, tick, deadline, mana, disarmer));
    }

    [Fact]
    public void BinaryInferredHeroExemptionDoesNotBypassDisarmer()
    {
        Assert.Equal(TrapReadiness.Ready, TrapActivationRules.Readiness(
            Definition, PlayerIds.Good, false, 10, 0, 0, false));
        Assert.Equal(TrapReadiness.BeingDisarmed, TrapActivationRules.Readiness(
            Definition, PlayerIds.Good, false, 10, 0, 0, true));
    }

    [Fact]
    public void BinaryInferredZeroCostSucceedsEvenWithNegativeMana()
    {
        Assert.Equal(TrapReadiness.Ready, TrapActivationRules.Readiness(
            Definition with { ManaCostToFire = 0 }, PlayerIds.FirstKeeper, false, 0, 0, -1, false));
        Assert.Equal(TrapReadiness.Ready, TrapActivationRules.Readiness(
            Definition with { Flags = 0 }, PlayerIds.FirstKeeper, false, 0, 0, 0, false));
    }

    [Fact]
    public void BinaryInferredChargeWrapsAndLosingTargetResetsIt()
    {
        Assert.Equal(0, TrapActivationRules.AdvanceCharge(255, true));
        Assert.Equal(0, TrapActivationRules.AdvanceCharge(254, false));
        byte firstDetection = TrapActivationRules.AdvanceCharge(0, true);
        Assert.Equal(1, firstDetection);
        Assert.True(TrapActivationRules.ChargeReached(Definition, firstDetection));
        TrapDef delayed = Definition with { ChargeSecondsRaw = 2048 };
        Assert.False(TrapActivationRules.ChargeReached(delayed, firstDetection));
        Assert.True(TrapActivationRules.ChargeReached(delayed, TrapActivationRules.AdvanceCharge(firstDetection, true)));
        Assert.False(TrapActivationRules.ChargeReached(Definition with { ChargeSecondsRaw = 64 * 4096 }, 255));
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(2048, 2u)]
    [InlineData(24576, 24u)]
    [InlineData(1023, 0u)]
    [InlineData(-1, 1048575u)]
    [InlineData(0x40000000, 0u)]
    public void BinaryInferredTimingUsesLowDwordAndLogicalShift(int raw, uint ticks) =>
        Assert.Equal(ticks, SimulationTiming.LoaderFixedSecondsToTicks(raw));
}
