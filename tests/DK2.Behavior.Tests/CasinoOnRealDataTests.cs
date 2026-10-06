// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The Casino's states against the shipped v1.70 data (casino.md, ADR-0396): the room's
/// furniture slots the finders read, the jackpot's variables, and the art its actions play.
/// Gated with [InstallFact].</summary>
public class CasinoOnRealDataTests
{
    private static GameDatabase Db() => GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level3");

    [InstallFact]
    public void TheShippedCasinoListsItsTablesInSlotsZeroToTwoAndItsAlcoveInSlotThree()
    {
        IReadOnlyList<byte> ids = Db().Room(RoomTypeId.Casino).ObjectIds;
        Assert.Equal(new byte[] { 87, 59, 67, 68, 0, 0 }, ids.Take(6));
        Assert.Equal(CasinoStep.RouletteTableObjectId, ids[0]);
        Assert.Equal(CasinoStep.AlcoveObjectId, ids[3]);
    }

    [InstallFact]
    public void TheJackpotsVariablesAreTheOnesTheFixtureCopies()
    {
        GameDatabase db = Db();
        Assert.Equal(5000, db[VariableTypeId.GamblingCreatureJackpotChanceSmilesPerSecond]);
        Assert.Equal(20000, db[VariableTypeId.GamblingCreatureJackpotChanceMoneyPerSecond]);
        Assert.Equal(240, db[VariableTypeId.GamblingJackpotPayTimeLimit]);
        Assert.Equal(100, db[VariableTypeId.GamblingJackpotPayTimeLimitMissedAngerPercentageModifier]);
        Assert.Equal(1000, db[VariableTypeId.GamblingJackpotBasicAmount]);
    }

    /// <summary>Two drinkers, King Reginald and the Stone Knight, have neither 146's drink art (19, falling
    /// back to 9) nor 153's dance (28, falling back to 8): DK2 turns them away at the alcove three times in
    /// four and from a party at once. Every drinker has 37, 8, 20 and 34; most lack art 14, so action 65
    /// is rare at a table.</summary>
    [InstallFact]
    public void TwoDrinkersHaveNoDrinkOrDanceArtAndMostHaveNoArtFourteen()
    {
        GameDatabase db = Db();
        var drinkers = db.Creatures.Values.Where(c =>
            c.HappyJobs.Concat(c.UnhappyJobs).Concat(c.AngryJobs).Any(j => j.Job == JobType.Drink)).ToList();
        string[] Missing(Func<CreatureDef, bool> has) => drinkers.Where(c => !has(c)).Select(c => c.Name).ToArray();
        string[] heroes = ["King Reginald", "Stone Knight"];
        Assert.Equal(heroes, Missing(c => AnimationTimingRules.ResolvesPresent(c, 19)));
        Assert.Equal(heroes, Missing(c => AnimationTimingRules.ResolvesPresent(c, 28)));
        Assert.Empty(Missing(c => AnimationTimingRules.ResolvesPresent(c, 37)));
        Assert.Empty(Missing(c => AnimationTimingRules.ResolvesPresent(c, 8)));
        Assert.Empty(Missing(c => AnimationTimingRules.ResolvesPresent(c, 34)));
        Assert.Empty(Missing(c => AnimationTimingRules.HasArt(c, 20)));
        Assert.Equal(28, Missing(c => AnimationTimingRules.HasArt(c, 14)).Length);
        Assert.Equal(39, drinkers.Count);
    }

    /// <summary>The Goblin's drink, action 39 three times over (four plays of art 19), and its turn at a
    /// table, action 68 once (art 20), from its own files. LIKELY: the cycle count is the renderer's.</summary>
    [InstallFact]
    public void TheGoblinsDrinkAndItsPlayAreTimedFromItsOwnArt_LikelyCycleCount()
    {
        CreatureDef goblin = Db().CreatureById(7);
        CreatureAnimationTiming drink = goblin.ArtSlot(AnimationTimingRules.ResolveArtSlot(goblin, 19));
        Assert.Equal(6, drink.ResourceType);
        Assert.Equal((4 * drink.FrameCount * 4 + drink.FramesPerSecond - 1) / drink.FramesPerSecond,
            AnimationTimingRules.CountedHoldTicks(goblin, 19, 3));
        Assert.True(AnimationTimingRules.CountedHoldTicks(goblin, 20, 1) > 0);
    }
}
