// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class StatusFlowersTests
{
    private static readonly StatusFlowerFacts OwnIdle = new(Own: true, Never: false, ViewerPossessing: false,
        Hovered: false, Dying: false, InCombat: false, CreatureMood.Content, BelowSixthHealth: false, InWindow: false);

    [Fact]
    public void PredicateFollowsNativeOrder()
    {
        Assert.False(StatusFlowerRules.Shows(OwnIdle));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { Own = false }));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { InCombat = true }));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { Mood = CreatureMood.Unhappy }));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { Mood = CreatureMood.Angry }));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { BelowSixthHealth = true }));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { InWindow = true }));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { Dying = true }));
        Assert.False(StatusFlowerRules.Shows(OwnIdle with { Own = false, Never = true }));
        Assert.False(StatusFlowerRules.Shows(OwnIdle with { Own = false, ViewerPossessing = true }));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { ViewerPossessing = true, Hovered = true }));
    }

    [Fact]
    public void HealthThresholdIsMaxTimesHundredOverSixHundred()
    {
        Assert.True(StatusFlowerRules.BelowSixthHealth(99, 600));
        Assert.False(StatusFlowerRules.BelowSixthHealth(100, 600));
        Assert.False(StatusFlowerRules.BelowSixthHealth(83, 500));
        Assert.True(StatusFlowerRules.BelowSixthHealth(82, 500));
    }

    [Fact]
    public void HeightIsBodyPlusLevelTimes0x600OverTen()
    {
        Assert.Equal(StatusFlowerRules.WindowTicks, 3);
        Assert.Equal(new Fp32(4096 + 460), StatusFlowerRules.Height(new Fp32(4096), 3));
        Assert.Equal(new Fp32(2048 + 1536), StatusFlowerRules.Height(new Fp32(2048), 10));
    }

    [InstallFact]
    public void EnemyShowsAndOwnIsHiddenWithNothingToReport()
    {
        (GameDatabase db, WorldState world, CreatureState own) = Setup();
        CreatureState enemy = world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, PlayerIds.Good, new(9, 8));
        var flowers = new StatusFlowers(db, PlayerIds.FirstKeeper);
        var shown = flowers.Capture(world, new GameTick(10), CreatureQueryTarget.None);
        Assert.DoesNotContain(shown, f => f.CreatureId == own.Id);
        StatusFlower flower = Assert.Single(shown, f => f.CreatureId == enemy.Id);
        Assert.Equal(new PlayerId(PlayerIds.Good), flower.Owner);
        Assert.Equal(enemy.Level, flower.Level);
        Assert.False(flower.Blink);
    }

    [InstallFact]
    public void HoveredOwnShowsUntilThreeTurnsAfterTheLastHoveredTurn()
    {
        (GameDatabase db, WorldState world, CreatureState own) = Setup();
        var flowers = new StatusFlowers(db, PlayerIds.FirstKeeper);
        Assert.Contains(flowers.Capture(world, new GameTick(10), new CreatureQueryTarget(own.Id)), f => f.CreatureId == own.Id);
        for (long turn = 10; turn <= 12; turn++)
            Assert.Contains(flowers.Capture(world, new GameTick(turn), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
        Assert.DoesNotContain(flowers.Capture(world, new GameTick(13), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
    }

    [InstallFact]
    public void HealthChangeShowsAndBlinksForThreeTurns()
    {
        (GameDatabase db, WorldState world, CreatureState own) = Setup();
        var flowers = new StatusFlowers(db, PlayerIds.FirstKeeper);
        Assert.DoesNotContain(flowers.Capture(world, new GameTick(20), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
        own.Health -= 1;
        for (long turn = 21; turn <= 23; turn++)
            Assert.True(Assert.Single(flowers.Capture(world, new GameTick(turn), CreatureQueryTarget.None),
                f => f.CreatureId == own.Id).Blink);
        Assert.DoesNotContain(flowers.Capture(world, new GameTick(24), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
    }

    [InstallFact]
    public void OwnBelowASixthOfMaxHealthShowsAndBlinks()
    {
        (GameDatabase db, WorldState world, CreatureState own) = Setup();
        int max = CreatureHealthRules.MaxHealth(db, db.CreatureById(own.CreatureTypeId), own);
        own.Health = max * 100 / 600;
        Assert.DoesNotContain(new StatusFlowers(db, PlayerIds.FirstKeeper)
            .Capture(world, new GameTick(5), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
        own.Health -= 1;
        StatusFlower flower = Assert.Single(new StatusFlowers(db, PlayerIds.FirstKeeper)
            .Capture(world, new GameTick(5), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
        Assert.True(flower.Blink);
    }

    [InstallFact]
    public void OwnUnhappyOrInCombatShows()
    {
        (GameDatabase db, WorldState world, CreatureState own) = Setup();
        own.Mood = CreatureMood.Unhappy;
        Assert.Contains(new StatusFlowers(db, PlayerIds.FirstKeeper)
            .Capture(world, new GameTick(5), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
        own.Mood = CreatureMood.Content;
        own.Dk2StateId = CreatureStateTable.InCategory(CreatureStateCategory.Combat)
            .First(id => !CreatureStateTable.IsUtility(id));
        StatusFlower flower = Assert.Single(new StatusFlowers(db, PlayerIds.FirstKeeper)
            .Capture(world, new GameTick(5), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
        CreatureDef definition = db.CreatureById(own.CreatureTypeId);
        Assert.Equal(StatusFlowerRules.Height(definition.Height, own.Level), flower.Height);
        own.InHand = true;
        Assert.DoesNotContain(new StatusFlowers(db, PlayerIds.FirstKeeper)
            .Capture(world, new GameTick(5), CreatureQueryTarget.None), f => f.CreatureId == own.Id);
    }

    private static (GameDatabase, WorldState, CreatureState) Setup()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        CreatureState own = world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, PlayerIds.FirstKeeper, new(8, 8));
        return (db, world, own);
    }
}
