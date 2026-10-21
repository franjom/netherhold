// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>Trigger action 42's arm (<c>0x0049AC80</c> arm <c>0x2A</c>) over the six world slots.</summary>
public class HeroSlotRulesTests
{
    [Fact]
    public void ANonZeroValueTakesTheFirstFreeSlotEvenForACreatureAlreadyMarked()
    {
        var slots = new HeroSlot[6];
        HeroSlotRules.Mark(slots, 7, 1);
        slots[1] = new HeroSlot { CreatureId = 9, Value = 2 };
        HeroSlotRules.Mark(slots, 7, 3);
        Assert.Equal(new HeroSlot { CreatureId = 7, Value = 1 }, slots[0]);
        Assert.Equal(new HeroSlot { CreatureId = 9, Value = 2 }, slots[1]);
        Assert.Equal(new HeroSlot { CreatureId = 7, Value = 3 }, slots[2]);
    }

    [Fact]
    public void AFullListTakesNothing()
    {
        var slots = new HeroSlot[6];
        for (int id = 1; id <= 6; id++) HeroSlotRules.Mark(slots, id, 1);
        HeroSlotRules.Mark(slots, 99, 1);
        Assert.DoesNotContain(slots, s => s.CreatureId == 99);
    }

    [Fact]
    public void ZeroFreesOnlyTheFirstSlotHoldingTheCreature()
    {
        var slots = new HeroSlot[6];
        HeroSlotRules.Mark(slots, 7, 1);
        HeroSlotRules.Mark(slots, 8, 1);
        HeroSlotRules.Mark(slots, 7, 2);
        HeroSlotRules.Mark(slots, 7, 0);
        Assert.True(slots[0].IsFree);
        Assert.Equal(8, slots[1].CreatureId);
        Assert.Equal(new HeroSlot { CreatureId = 7, Value = 2 }, slots[2]);
        HeroSlotRules.Mark(slots, 42, 0);
        Assert.Equal(8, slots[1].CreatureId);
    }
}
