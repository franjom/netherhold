// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

/// <summary>What the stand-ins for DK2's current-action field say (<c>Creature_IsActionRunning</c>, <c>0x00496D00</c>): ours has no single
/// field, so the index is told only for the actions it models, and an action it runs but cannot name is running with index 0.</summary>
public sealed class CreatureCurrentActionTests
{
    private static readonly CreatureDef Def = Dk2TerrainFixture.Goblin();
    private static readonly GameTick Now = new(500);

    private static CreatureState Creature() => new() { Id = 1, CreatureTypeId = Def.CreatureId };

    [Fact]
    public void ACreatureWithNoActionStandInIsNotRunningAnythingAndHasNoIndex()
    {
        CreatureState c = Creature();
        Assert.False(CreatureCurrentAction.IsRunning(c, Now));
        Assert.Equal(0, CreatureCurrentAction.IndexOf(c, Def, Now));
    }

    [Theory]
    [InlineData(TrainingActionKind.Melee, CreatureActionTable.TrainMelee)]
    [InlineData(TrainingActionKind.Spell, CreatureActionTable.TrainSpell)]
    public void ATraineesActionIsThirtyOrThirtyOne(TrainingActionKind kind, int index)
    {
        CreatureState c = Creature();
        c.TrainingActionId = (byte)kind;
        Assert.True(CreatureCurrentAction.IsRunning(c, Now));
        Assert.Equal(index, CreatureCurrentAction.IndexOf(c, Def, Now));
    }

    [Theory]
    [InlineData(TileWorkKind.Dig, CreatureActionTable.Dig)]
    [InlineData(TileWorkKind.DigOwnWall, CreatureActionTable.Dig)]
    [InlineData(TileWorkKind.DigEnemyWall, CreatureActionTable.Dig)]
    [InlineData(TileWorkKind.MineGold, CreatureActionTable.Mine)]
    public void AnImpsWorkLoopIsDigOrMine(TileWorkKind work, int index)
    {
        CreatureState c = Creature();
        c.ActionArtSlot = 5;
        c.TargetTileIndex = 7;
        c.TargetWork = work;
        Assert.True(CreatureCurrentAction.IsRunning(c, Now));
        Assert.Equal(index, CreatureCurrentAction.IndexOf(c, Def, Now));
    }

    [Fact]
    public void AClipOnAnotherSlotOrWithNoTaskIsNotTheWorkLoop()
    {
        CreatureState c = Creature();
        c.ActionArtSlot = 12;
        c.TargetTileIndex = 7;
        c.TargetWork = TileWorkKind.Dig;
        Assert.False(CreatureCurrentAction.IsRunning(c, Now));
        c.ActionArtSlot = 5;
        c.TargetTileIndex = -1;
        Assert.False(CreatureCurrentAction.IsRunning(c, Now));
    }

    [Fact]
    public void ACastInItsHoldIsSixteenAndAHoldOfAnythingElseRunsWithNoIndex()
    {
        CreatureState cast = Creature();
        cast.SwingKind = SpellRules.SpellCast;
        cast.ActionUntilTick = Now.Value + 5;
        Assert.True(CreatureCurrentAction.IsRunning(cast, Now));
        Assert.Equal(CreatureActionTable.SpellCast, CreatureCurrentAction.IndexOf(cast, Def, Now));
        Assert.False(CreatureCurrentAction.IsRunning(cast, new GameTick(Now.Value + 5)));

        CreatureState flourish = Creature();
        flourish.SwingKind = SpellRules.MeleeSwing;
        flourish.ActionUntilTick = Now.Value + 5;
        Assert.True(CreatureCurrentAction.IsRunning(flourish, Now));
        Assert.Equal(0, CreatureCurrentAction.IndexOf(flourish, Def, Now));
    }

    [Fact]
    public void ASwingInProgressRunsAndIsSeventeenOnceItsHoldDefinesIt()
    {
        CreatureState c = Creature();
        c.SwingInProgress = true;
        Assert.True(CreatureCurrentAction.IsRunning(c, Now));
        Assert.Equal(CreatureActionTable.MeleeSwing, CreatureCurrentAction.IndexOf(c, Def, Now));
    }

    [Theory]
    [InlineData(SpellRules.MeleeSwing, CreatureActionTable.MeleeSwing)]
    [InlineData(SpellRules.SpellCast, CreatureActionTable.SpellCast)]
    [InlineData(SpellRules.FreeAimCast, CreatureActionTable.FreeAimCast)]
    public void ASwingCastOrFreeAimInProgressIsItsOwnIndex(byte kind, int index)
    {
        CreatureState c = Creature();
        c.SwingInProgress = true;
        c.SwingKind = kind;
        Assert.Equal(index, CreatureCurrentAction.IndexOf(c, Def, Now));
    }
}
