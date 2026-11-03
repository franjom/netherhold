// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Tests;

/// <summary>The sound word (<c>+25</c>) of each of the 81 records of the creature action table at <c>0x006E4198</c>, which
/// <c>CreatureAction_Begin</c> (<c>0x00496700</c>) asks of the creature's category when an action begins. The words are
/// the filler <c>0x004950C0</c>'s stores at <c>0x006E41B1 + 29 x n</c>, read by tracking the registers it stores
/// (docs/research/sound-effects-continuous.md section 2).</summary>
public sealed class CreatureActionTableTests
{
    [Fact]
    public void FortyFourOfTheEightyOneRecordsCarryASoundWord()
    {
        Assert.Equal(81, CreatureActionTable.Count);
        Assert.Equal(44, Enumerable.Range(0, CreatureActionTable.Count).Count(a => CreatureActionTable.SoundWord(a) != 0));
    }

    [Theory]
    [InlineData(CreatureActionTable.Dig, 0x334)]
    [InlineData(CreatureActionTable.Mine, 0x109)]
    [InlineData(CreatureActionTable.RepairWall, 0x10C)]
    [InlineData(CreatureActionTable.FreeAimCast, 0xD8)]
    [InlineData(CreatureActionTable.SpellCast, 0xD8)]
    [InlineData(CreatureActionTable.MeleeSwing, 0xD7)]
    [InlineData(CreatureActionTable.TrainMelee, 0xD3)]
    [InlineData(CreatureActionTable.TrainSpell, 0xD3)]
    [InlineData(24, 0xCE)]
    [InlineData(26, 0xCD)]
    [InlineData(58, 0x12D)]
    [InlineData(80, 0xD7)]
    public void TheWordsTheCombatWorkAndTrainingActionsAskAreThoseOfTheirRecords(int action, int word) =>
        Assert.Equal((ushort)word, CreatureActionTable.SoundWord(action));

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(7)] [InlineData(22)]
    public void ARecordWithoutAWordAsksNothing(int action) => Assert.Equal((ushort)0, CreatureActionTable.SoundWord(action));

    [Theory]
    [InlineData(-1)] [InlineData(81)] [InlineData(1000)]
    public void AnIndexOutsideTheTableAsksNothing(int action) => Assert.Equal((ushort)0, CreatureActionTable.SoundWord(action));
}
