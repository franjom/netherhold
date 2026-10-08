// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary><c>0x00478A20</c>'s code, which the keeper's slap step asks to be 5
/// (computer-keeper-management.md §8). A worker's row reads its last task kind (<c>+0x98</c>), which
/// <c>0x0046AEA0</c> writes 6 for a reinforce and 16 for a repair.</summary>
public sealed class CreatureActivityRulesTests
{
    [Fact]
    public void AnImpWithNoTaskIsSlappable()
    {
        var imp = new CreatureState { Dk2StateId = 6, UtilityNewState = 87, WorkerIdle = true };
        Assert.Equal(CreatureActivityRules.Slappable, CreatureActivityRules.Code(Dk2TerrainFixture.Imp(), imp));
    }

    [Fact]
    public void AnImpOnItsWayToReinforceIsSlappable()
    {
        var imp = new CreatureState { Dk2StateId = 6, UtilityNewState = 21, LastTaskKind = ImpTaskKind.Reinforce };
        Assert.Equal(CreatureActivityRules.Slappable, CreatureActivityRules.Code(Dk2TerrainFixture.Imp(), imp));
    }

    /// <summary>The kind stays 6 until state 10 zeroes it, so a reinforcer drawn into a fight is
    /// still slapped: Level 11A's capture shows Morgana's imps slapped in 199 in the hole (Q574).</summary>
    [Fact]
    public void AnImpFightingAfterAReinforceIsSlappable()
    {
        var imp = new CreatureState { Dk2StateId = 199, LastTaskKind = ImpTaskKind.Reinforce };
        Assert.Equal(CreatureActivityRules.Slappable, CreatureActivityRules.Code(Dk2TerrainFixture.Imp(), imp));
    }

    [Theory]
    [InlineData(16, ImpTaskKind.Claim)]
    [InlineData(14, ImpTaskKind.Excavate)]
    [InlineData(26, ImpTaskKind.Mine)]
    [InlineData(24, ImpTaskKind.Fix)]
    public void AnImpClaimingDiggingMiningOrRepairingIsNot(int state, ImpTaskKind kind)
    {
        var imp = new CreatureState { Dk2StateId = state, LastTaskKind = kind };
        Assert.Equal(CreatureActivityRules.WorkerBusy, CreatureActivityRules.Code(Dk2TerrainFixture.Imp(), imp));
    }

    [Theory]
    [InlineData(1, CreatureActivityRules.Slappable)] // idle
    [InlineData(110, 0x15)]                           // training
    [InlineData(56, 0x14)]                            // research
    [InlineData(60, 2)]                               // category 0
    [InlineData(199, 4)]                              // combat
    public void ANonWorkerReadsItsCategorysEntry(int state, int code)
    {
        var goblin = new CreatureState { Dk2StateId = state };
        Assert.Equal(code, CreatureActivityRules.Code(Dk2TerrainFixture.Goblin(), goblin));
    }

    [Fact]
    public void ANonWorkerOnAWalkReadsTheResumedStatesCategory()
    {
        var goblin = new CreatureState { Dk2StateId = 7, UtilityNewState = 108 };
        Assert.Equal(0x15, CreatureActivityRules.Code(Dk2TerrainFixture.Goblin(), goblin));
    }

    [Fact]
    public void AnImpInCombatIsFighting()
    {
        var imp = new CreatureState { Dk2StateId = 199 };
        Assert.Equal(CreatureActivityRules.WorkerFighting, CreatureActivityRules.Code(Dk2TerrainFixture.Imp(), imp));
    }
}
