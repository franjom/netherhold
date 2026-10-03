// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public sealed class TremorOnRealDataTests
{
    [InstallFact]
    public void ShippedTremorHasTheResearchedDescriptorAndCarrierValues()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var spell = db.KeeperSpellById(8)!;
        Assert.Equal("Tremor", spell.Name);
        Assert.Equal((30000, 0, 0), (spell.ManaCost, spell.ManaUpkeepPerSecond, spell.CooldownRaw));
        Assert.Equal(((byte)5, (byte)4), (spell.LocationSelector, spell.TargetSelector));
        Assert.Equal(0x14u, (uint)spell.Flags);
        Assert.Equal(((byte)24, 5, 3), spell.Shot(false));
        Assert.Equal(((byte)24, 7, 3), spell.Shot(true));
        var shot = db.Shots[spell.ShotTypeId];
        Assert.Equal((byte)27, shot.ProcessType);
        Assert.Equal(0x40u, shot.ShotProcessFlags);
        Assert.Equal(4u, shot.Flags);
        Assert.Equal(10, shot.Health);
        Assert.Equal(((byte)0, (byte)2), (shot.DamageType, shot.CollideType));
        Assert.Equal("Tremor Rocks", db.Effects[43].Name);
        Assert.Equal((ushort)43, db.Effects[43].EffectId);
        foreach (var (id, knockdownTicks, getUpTicks) in new[]
        {
            (5, 5, 8), (7, 7, 11), (10, 5, 5), (12, 3, 13), (14, 3, 4), (29, 5, 4),
        })
        {
            var creature = db.CreatureById((byte)id);
            Assert.Equal(knockdownTicks, AnimationTimingRules.KnockdownHoldTicks(creature));
            Assert.Equal(getUpTicks, AnimationTimingRules.GetUpHoldTicks(creature));
        }
        var horny = db.CreatureById(27);
        Assert.Equal(1, AnimationTimingRules.KnockdownHoldTicks(horny));
        Assert.Equal(9, AnimationTimingRules.GetUpHoldTicks(horny));
    }
}
