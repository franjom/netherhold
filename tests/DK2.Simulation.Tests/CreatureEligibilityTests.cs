// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Tests;

public class CreatureEligibilityTests
{
    private static CreatureState Free() => new() { Id = 1, Health = 100 };

    [Fact]
    public void AFreeCreatureIsExcludedByNoGuard()
    {
        CreatureGuards all = CreatureGuards.Possessed | CreatureGuards.Held | CreatureGuards.Imprisoned
            | CreatureGuards.Down | CreatureGuards.Preoccupied;

        Assert.False(CreatureEligibility.IsExcluded(Free(), all));
    }

    [Fact]
    public void EachGuardAsksOnlyItsOwnQuestion()
    {
        CreatureState held = Free();
        held.InHand = true;
        CreatureState possessed = Free();
        possessed.Possessed = true;
        CreatureState prisoner = Free();
        prisoner.PrisonOwnerPlayerId = 4;

        Assert.True(CreatureEligibility.IsExcluded(held, CreatureGuards.Held));
        Assert.False(CreatureEligibility.IsExcluded(held, CreatureGuards.Possessed | CreatureGuards.Imprisoned));
        Assert.True(CreatureEligibility.IsExcluded(possessed, CreatureGuards.Possessed));
        Assert.True(CreatureEligibility.IsExcluded(possessed, CreatureGuards.Preoccupied));
        Assert.False(CreatureEligibility.IsExcluded(possessed, CreatureGuards.Held | CreatureGuards.Down));
        Assert.True(CreatureEligibility.IsExcluded(prisoner, CreatureGuards.Imprisoned));
        Assert.False(CreatureEligibility.IsExcluded(prisoner, CreatureGuards.Sidelined));
    }

    [Fact]
    public void DownIsCollapsedOrDying_AndPreoccupiedIsAFight()
    {
        CreatureState collapsed = Free();
        collapsed.Health = 0;
        CreatureState dying = Free();
        dying.Dk2StateId = CreatureDeathRules.DyingState;
        CreatureState fighting = Free();
        fighting.Dk2StateId = 199; // CombatStates.Engaging, internal to the simulation

        Assert.True(CreatureEligibility.IsExcluded(collapsed, CreatureGuards.Down));
        Assert.True(CreatureEligibility.IsExcluded(dying, CreatureGuards.Down));
        Assert.False(CreatureEligibility.IsExcluded(collapsed, CreatureGuards.Held | CreatureGuards.Preoccupied));
        Assert.True(CreatureEligibility.IsExcluded(fighting, CreatureGuards.Preoccupied));
        Assert.False(CreatureEligibility.IsExcluded(fighting, CreatureGuards.Sidelined | CreatureGuards.Imprisoned));
    }

    [Fact]
    public void SidelinedIsPossessedHeldOrDown()
    {
        CreatureState held = Free();
        held.InHand = true;
        CreatureState collapsed = Free();
        collapsed.Health = 0;

        Assert.Equal(CreatureGuards.Possessed | CreatureGuards.Held | CreatureGuards.Down, CreatureGuards.Sidelined);
        Assert.True(CreatureEligibility.IsExcluded(held, CreatureGuards.Sidelined));
        Assert.True(CreatureEligibility.IsExcluded(collapsed, CreatureGuards.Sidelined));
    }

    [Fact]
    public void NoGuardsExcludeNobody()
    {
        CreatureState held = Free();
        held.InHand = true;

        Assert.False(CreatureEligibility.IsExcluded(held, CreatureGuards.None));
    }
}
