// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using Xunit;

namespace DK2.Simulation.Tests;

/// <summary>Action 16 makes its shot at the slot's origin turned by the caster's heading
/// (<c>0x00498989</c>, combat-spells.md §7, ADR-0489): the Dark Elf's (0, −2048) is half a tile ahead.</summary>
public class ShotOriginTests
{
    private static readonly CreatureSpellSlot DarkElf = new(12, 1, new Fp32(0), 1, 0, -2048, 2662);

    [Theory]
    [InlineData(0, 0, -2048)]      // facing -Y
    [InlineData(512, 2048, 0)]     // a quarter turn: facing +X
    [InlineData(1024, 0, 2048)]
    [InlineData(1536, -2048, 0)]
    public void TheShotLeavesHalfATileAheadOfADarkElf(int heading, int dx, int dy)
    {
        var me = new CreatureState { PosX = 100_000, PosY = 200_000, Heading = heading };

        (int x, int y) = FightRules.ShotOrigin(me, DarkElf);

        Assert.Equal(100_000 + dx, x);
        Assert.Equal(200_000 + dy, y);
    }

    /// <summary>No shipped slot has a sideways component; this one exercises the vx terms. Expected
    /// from 0x00498989's formula by hand: sin 100 = 19792, cos 100 = 62475 in the 16.16 table;
    /// dx = (1000·62475 &gt;&gt; 16) − (−2048·19792 &gt;&gt; 16) = 953 + 619, dy = (1000·19792 &gt;&gt; 16) + (−2048·62475 &gt;&gt; 16) = 302 − 1953.</summary>
    [Fact]
    public void ASidewaysOriginTurnsWithTheHeading()
    {
        var me = new CreatureState { PosX = 100_000, PosY = 200_000, Heading = 100 };

        Assert.Equal((100_000 + 1572, 200_000 - 1651), FightRules.ShotOrigin(me, DarkElf with { OriginX = 1000 }));
    }

    [Fact]
    public void APossessedCasterFiresFromItsOwnPosition()
    {
        var me = new CreatureState { PosX = 100_000, PosY = 200_000, Heading = 512, Possessed = true };

        Assert.Equal((100_000, 200_000), FightRules.ShotOrigin(me, DarkElf));
    }
}
