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

    [InstallFact]
    public void KnockedOutCreaturesOfEitherSideAndPrisonersShow_OwnerConfirmed()
    {
        // Owner 2026-10-20, from play: a knocked-out creature keeps its flower, own or enemy, and
        // an enemy in the viewer's prison shows one.
        (GameDatabase db, WorldState world, CreatureState own) = Setup();
        CreatureState enemy = world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, PlayerIds.Good, new(9, 8));
        CreatureState prisoner = world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, PlayerIds.Good, new(10, 8));
        own.Dk2StateId = CreatureDeathRules.DyingState;
        enemy.Dk2StateId = CreatureDeathRules.DyingState;
        prisoner.Dk2StateId = PrisonRules.InTheCellState;
        var shown = new StatusFlowers(db, PlayerIds.FirstKeeper).Capture(world, new GameTick(5), CreatureQueryTarget.None);
        Assert.Contains(shown, f => f.CreatureId == own.Id);
        Assert.Contains(shown, f => f.CreatureId == enemy.Id);
        Assert.Contains(shown, f => f.CreatureId == prisoner.Id);
    }

    [Fact]
    public void RingLosesAPetalPerQuarterOfHealthAndShowsOneWhileDying()
    {
        Assert.Equal(0, StatusFlowerRules.Ring(false, 100, 100));
        Assert.Equal(1, StatusFlowerRules.Ring(false, 99, 100));
        Assert.Equal(1, StatusFlowerRules.Ring(false, 75, 100));
        Assert.Equal(2, StatusFlowerRules.Ring(false, 74, 100));
        Assert.Equal(3, StatusFlowerRules.Ring(false, 49, 100));
        Assert.Equal(4, StatusFlowerRules.Ring(false, 24, 100));
        Assert.Equal(4, StatusFlowerRules.Ring(false, 0, 100));
        Assert.Equal(0, StatusFlowerRules.Ring(false, 150, 100));
        Assert.Equal(4, StatusFlowerRules.Ring(false, 10, 0));
        Assert.Equal(5, StatusFlowerRules.Ring(true, 100, 100));
    }

    [Fact]
    public void BlinkIsATriangleOverOneCycle()
    {
        var red = new StatusFlowerColour(230, 50, 50);
        Assert.Equal(new StatusFlowerColour(0, 0, 0), StatusFlowerRules.Blinked(red, 1000, 1000));
        Assert.Equal(red, StatusFlowerRules.Blinked(red, 1000, 1000 + 167)); // u = 50: full
        Assert.Equal(new StatusFlowerColour(225, 49, 49), StatusFlowerRules.Blinked(red, 1000, 1000 + 166)); // u = 49: 98%
        Assert.Equal(new StatusFlowerColour(0, 0, 0), StatusFlowerRules.Blinked(red, 1000, 1000 + 333));
    }

    [Fact]
    public void ExperienceShareIsTheWayToTheNextLevelInHalfTurnUnits()
    {
        Assert.Equal(0f, StatusFlowerRules.ExperienceShare(0, 3000));
        Assert.Equal(0.5f, StatusFlowerRules.ExperienceShare(1500, 3000));
        Assert.Equal(0f, StatusFlowerRules.ExperienceShare(1500, 0));
        Assert.Equal(0f, StatusFlowerRules.ExperienceShare(3000, 3000));
    }

    [Fact]
    public void CentreCyclesLevelMoodAngerAndBack_HeroStatusListUnbuilt()
    {
        var unhappy = new StatusFlowerCentreFacts(Hero: false, Imp: false, StateId: 1, MoodBand: 1,
            AngerBucket.Hunger, Status: null, Job: "SJ-Library", Level: 3);
        Assert.Equal(StatusFlowerMode.Mood, StatusFlowerCentre.Next(StatusFlowerMode.Level, unhappy));
        Assert.Equal(StatusFlowerMode.AngerReason, StatusFlowerCentre.Next(StatusFlowerMode.Mood, unhappy));
        Assert.Equal(StatusFlowerMode.Level, StatusFlowerCentre.Next(StatusFlowerMode.AngerReason, unhappy));
        Assert.Equal(("SM-Sad", StatusFlowerMode.Mood), StatusFlowerCentre.Icon(StatusFlowerMode.Mood, unhappy));
        Assert.Equal(("SJ-Hungry", StatusFlowerMode.AngerReason), StatusFlowerCentre.Icon(StatusFlowerMode.AngerReason, unhappy));
        var content = unhappy with { MoodBand = 0 };
        Assert.Equal(StatusFlowerMode.Job, StatusFlowerCentre.Next(StatusFlowerMode.Level, content));
        Assert.Equal(("SJ-Library", StatusFlowerMode.Job), StatusFlowerCentre.Icon(StatusFlowerMode.Job, content));
        Assert.Equal(("SL-03", StatusFlowerMode.Level), StatusFlowerCentre.Icon(StatusFlowerMode.Job, content with { Job = null }));
        var stunned = content with { Status = "SJ-Stunned" };
        Assert.Equal(StatusFlowerMode.Status, StatusFlowerCentre.Next(StatusFlowerMode.Level, stunned));
        Assert.Equal(("SL-03", StatusFlowerMode.Status), StatusFlowerCentre.Icon(StatusFlowerMode.Status, content));
        var hero = content with { Hero = true };
        Assert.Equal(StatusFlowerMode.Level, StatusFlowerCentre.Next(StatusFlowerMode.Level, hero));
        var idleImp = content with { Imp = true, StateId = 8 };
        Assert.Equal(StatusFlowerMode.Level, StatusFlowerCentre.Next(StatusFlowerMode.Level, idleImp));
        Assert.Equal(StatusFlowerMode.Job, StatusFlowerCentre.Next(StatusFlowerMode.Level, idleImp with { StateId = 0xD }));
    }

    [Fact]
    public void JobIconsFollowTheCategoryTable()
    {
        Assert.Equal("SJ-Dig", StatusFlowerCentre.Job(false, CreatureStateCategory.WorkerJob, 0xD));
        Assert.Equal("SJ-Claim", StatusFlowerCentre.Job(false, CreatureStateCategory.WorkerJob, 0x12));
        Assert.Null(StatusFlowerCentre.Job(false, CreatureStateCategory.WorkerJob, 0x30));
        Assert.Equal("SJ-Workshop", StatusFlowerCentre.Job(false, CreatureStateCategory.Manufacture, 0));
        Assert.Equal("SJ-Watching", StatusFlowerCentre.Job(false, CreatureStateCategory.CombatPit, 0x8F));
        Assert.Equal("SM-Sad", StatusFlowerCentre.Job(false, CreatureStateCategory.Sulking, 0));
        Assert.Null(StatusFlowerCentre.Job(false, CreatureStateCategory.Combat, 0));
        Assert.Equal("SJ-No_Go", StatusFlowerCentre.Job(true, CreatureStateCategory.Combat, 0));
    }

    [Fact]
    public void StatusPrefersUnconsciousThenStunnedThenFear()
    {
        Assert.Equal("SJ-Unconscious", StatusFlowerCentre.Status(false, true, true, false, 9, 1));
        Assert.Equal("SJ-Stunned", StatusFlowerCentre.Status(false, false, true, false, 9, 1));
        Assert.Equal("ST-Fear", StatusFlowerCentre.Status(false, false, false, false, 9, 1));
        Assert.Null(StatusFlowerCentre.Status(false, false, false, true, 9, 1));
    }

    /// <summary>A hero's status is its world slot's value (0x00597270 kinds 7-9, the icon table at
    /// 0x006BE580: SJ-Fighting, SJ-Prison, SJ-Torture), whatever else is true of it; none without a slot.</summary>
    [Fact]
    public void AHeroTakesItsStatusFromItsWorldSlot()
    {
        Assert.Null(StatusFlowerCentre.Status(true, true, false, false, 9, 1));
        Assert.Equal("SJ-Fighting", StatusFlowerCentre.Status(true, true, true, false, 9, 1, heroSlotValue: 1));
        Assert.Equal("SJ-Prison", StatusFlowerCentre.Status(true, false, false, false, 0, 1, heroSlotValue: 2));
        Assert.Equal("SJ-Torture", StatusFlowerCentre.Status(true, false, false, false, 0, 1, heroSlotValue: 3));
        Assert.Equal("SJ-Fighting", StatusFlowerCentre.Status(true, false, false, false, 0, 1, heroSlotValue: 7));
    }

    /// <summary>0x00597270's order past fear: unconscious, stunned, leaving (bit 14), fear, then the
    /// Portal Gem (0x0048B5C0).</summary>
    [Fact]
    public void LeavingComesBeforeFearAndTheGemComesLast()
    {
        Assert.Equal("SJ-Leaving", StatusFlowerCentre.Status(false, false, false, false, 9, 1, leaving: true, carriesPortalGem: true));
        Assert.Equal("SJ-Stunned", StatusFlowerCentre.Status(false, false, true, false, 9, 1, leaving: true));
        Assert.Equal("ST-Fear", StatusFlowerCentre.Status(false, false, false, false, 9, 1, carriesPortalGem: true));
        Assert.Equal("S-Gem", StatusFlowerCentre.Status(false, false, false, true, 9, 1, carriesPortalGem: true));
        Assert.Null(StatusFlowerCentre.Status(true, false, false, false, 9, 1, leaving: true, carriesPortalGem: true));
    }

    /// <summary>0x0048F110 with four turns a second: 1 at the stamp, one more a second, 5 from five
    /// seconds on; 0x0048F0F0 draws the ring on the second half of each second.</summary>
    [Fact]
    public void RemovalCountdownStepsOnceASecondAndBlinksTheRing()
    {
        Assert.Equal(0, StatusFlowerRules.RemovalStep(0, new GameTick(100)));
        Assert.Equal(1, StatusFlowerRules.RemovalStep(100, new GameTick(100)));
        Assert.Equal(1, StatusFlowerRules.RemovalStep(100, new GameTick(103)));
        Assert.Equal(2, StatusFlowerRules.RemovalStep(100, new GameTick(104)));
        Assert.Equal(5, StatusFlowerRules.RemovalStep(100, new GameTick(119)));
        Assert.Equal(5, StatusFlowerRules.RemovalStep(100, new GameTick(500)));
        Assert.False(StatusFlowerRules.CountdownRingOn(new GameTick(8)));
        Assert.False(StatusFlowerRules.CountdownRingOn(new GameTick(9)));
        Assert.True(StatusFlowerRules.CountdownRingOn(new GameTick(10)));
        Assert.True(StatusFlowerRules.CountdownRingOn(new GameTick(11)));
        Assert.True(StatusFlowerRules.Shows(OwnIdle with { MarkedForRemoval = true }));
    }

    /// <summary>0x0059C360: scale 0x400 (0x200 possessing) times the descriptor, 0x1000 or 0x2000
    /// for Imprison and the Objectives, in 12.12, over 4096.</summary>
    [Fact]
    public void AFlowerIsAQuarterTileAcross()
    {
        Assert.Equal(0.25f, StatusFlowerRules.WorldSize("H-05", possessing: false));
        Assert.Equal(0.25f, StatusFlowerRules.WorldSize("SJ-Dig", possessing: false));
        Assert.Equal(0.5f, StatusFlowerRules.WorldSize("Imprison", possessing: false));
        Assert.Equal(0.5f, StatusFlowerRules.WorldSize("Objective-2", possessing: false));
        Assert.Equal(0.125f, StatusFlowerRules.WorldSize("SL-01", possessing: true));
    }

    /// <summary>0x005955C0: torture +0x580 off a floor slot, +0x280 on one; +0x80 for a slot holder.
    /// The on-slot value assumes global 0x00759ADC is 1; it has no writer we have found, and at 0 DK2
    /// adds 0x400 more (Q635).</summary>
    [Fact]
    public void TortureAndHeroSlotsLiftTheFlower_AssumingGlobal759ADCIsOneQ635()
    {
        var body = new Fp32(4096);
        Assert.Equal(new Fp32(4096 + 460 + 0x580),
            StatusFlowerRules.Height(body, 3, StatusFlowerTortureLift.OffFloorSlot));
        Assert.Equal(new Fp32(4096 + 460 + 0x280 + 0x80),
            StatusFlowerRules.Height(body, 3, StatusFlowerTortureLift.OnFloorSlot, holdsHeroSlot: true));
        Assert.Equal(new Fp32(4096 + 0x80), StatusFlowerRules.Height(body, 0, holdsHeroSlot: true));
    }

    [InstallFact]
    public void ARemovalCountdownShowsTheDigitAndAViewerMarkShowsTheTag()
    {
        (GameDatabase db, WorldState world, CreatureState own) = Setup();
        own.ManaRemovalStamp = 40;
        RescuePriorityRules.Set(own, PlayerIds.FirstKeeper, true);
        var flowers = new StatusFlowers(db, PlayerIds.FirstKeeper);
        StatusFlower flower = Assert.Single(flowers.Capture(world, new GameTick(44), CreatureQueryTarget.None),
            f => f.CreatureId == own.Id);
        Assert.Equal("SL-04", flower.Centre);
        Assert.True(flower.Imprison);
        Assert.False(flower.DrawRing);
        Assert.True(Assert.Single(flowers.Capture(world, new GameTick(46), CreatureQueryTarget.None),
            f => f.CreatureId == own.Id).DrawRing);
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
