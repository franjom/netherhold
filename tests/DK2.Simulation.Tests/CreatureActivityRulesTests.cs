// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary><c>0x00478A20</c>'s code, which the keeper's slap step asks to be 5
/// (computer-keeper-management.md §8). The worker rows come from the Level 17 capture: of
/// Harkan's imps only an idle one or a reinforcer was ever slapped.</summary>
public sealed class CreatureActivityRulesTests
{
    [Fact]
    public void AnImpWithNoTaskIsSlappable()
    {
        var imp = new CreatureState { Dk2StateId = 6, UtilityNewState = 87, WorkerIdle = true };
        Assert.Equal(CreatureActivityRules.Slappable, CreatureActivityRules.Code(Dk2TerrainFixture.Imp(), imp));
    }

    [Fact]
    public void AnImpOnItsWayToReinforceIsSlappable_TaskKindReadFromTheCapture()
    {
        var imp = new CreatureState { Dk2StateId = 6, UtilityNewState = 21, TargetTileIndex = 40, TargetWork = TileWorkKind.Reinforce };
        Assert.Equal(CreatureActivityRules.Slappable, CreatureActivityRules.Code(Dk2TerrainFixture.Imp(), imp));
    }

    [Theory]
    [InlineData(16, TileWorkKind.Claim)]
    [InlineData(14, TileWorkKind.Dig)]
    [InlineData(26, TileWorkKind.MineGold)]
    public void AnImpClaimingDiggingOrMiningIsNot_TaskKindReadFromTheCapture(int state, TileWorkKind work)
    {
        var imp = new CreatureState { Dk2StateId = state, TargetTileIndex = 40, TargetWork = work };
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
