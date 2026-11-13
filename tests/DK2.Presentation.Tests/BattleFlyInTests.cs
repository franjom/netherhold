// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The Battle tab's fly-in (battle-tab.md §5): the second reward tablet, started by
/// <c>0x0042C0D0</c>, stepped by <c>0x0042EE80</c>, finished in <c>0x0042C390</c>.</summary>
public sealed class BattleFlyInTests
{
    private static readonly Vector3[] Ring = [new(0.05f, 0, 0), new(-0.05f, 0, 0)];

    [Fact]
    public void TheStartPlaysItsSoundAndLatches_ASecondStartIsRefused()
    {
        var flyIn = new BattleFlyIn();
        Assert.True(flyIn.Start(0, 1000, 100, 900, 600));
        Assert.True(flyIn.Running);
        Assert.False(flyIn.Start(10, 1000, 100, 900, 600));
        SoundRequest start = Assert.Single(flyIn.Sounds.Pending);
        Assert.Equal(BattleFlyIn.StartSound, start.Id.Value);
        Assert.Equal("gui_button_tab_new_combat", start.Category.Name);
        Assert.Equal(BattleFlyIn.StartScale, flyIn.Tablet.Scale);
        Assert.Equal((1000f, 100f), (flyIn.Tablet.X, flyIn.Tablet.Y));
    }

    [Fact]
    public void ForTwoSecondsOnTheRealClockItStandsStillWithoutSparkles()
    {
        // 0x0042EEE8..0x0042EEF1: before the gate only the sparkles age; the spawn waits too.
        var flyIn = new BattleFlyIn();
        flyIn.Start(0, 1000, 100, 900, 600);
        var random = new Random(3);
        for (int ms = 0; ms < 2000; ms += 33)
            Assert.True(Frame(flyIn, ms, random));
        Assert.Equal((1000f, 100f, BattleFlyIn.StartScale, 0), (flyIn.Tablet.X, flyIn.Tablet.Y, flyIn.Tablet.Scale, flyIn.Tablet.Frame));
        Assert.Equal(0, flyIn.Tablet.Sparkles.Length);
    }

    [Fact]
    public void ThenItGlidesInAStraightLineAndShrinksToNothingIn32Frames_TurningOnce()
    {
        var flyIn = new BattleFlyIn();
        flyIn.Start(0, 1000, 100, 900, 600);
        var random = new Random(3);
        Frame(flyIn, 2000, random);
        Assert.Equal(1000 + (900 - 1000) / 32f, flyIn.Tablet.X, 3);    // 1/32 of the gap
        Assert.Equal(100 + (600 - 100) / 32f, flyIn.Tablet.Y, 3);
        Assert.Equal(BattleFlyIn.StartScale * 31 / 32, flyIn.Tablet.Scale, 5);
        for (int frame = 1; frame < 32; frame++)
        {
            Frame(flyIn, 2000 + frame * 33, random);
            if (frame == 16) Assert.Equal(16 * 64, flyIn.Tablet.Angle);
        }
        Assert.Equal((900f, 600f), (flyIn.Tablet.X, flyIn.Tablet.Y));
        Assert.Equal(0, flyIn.Tablet.Scale, 6);
        Assert.NotEqual(0, flyIn.Tablet.Sparkles.Length);               // the spray outlives the tablet
    }

    [Fact]
    public void WhenTheSparklesAreGoneItEndsWithItsSoundAndReleasesTheLatch()
    {
        // Hold 0: past frame 34 with no sparkles the hold arms, and the next call is done.
        var flyIn = new BattleFlyIn();
        flyIn.Start(0, 1000, 100, 900, 600);
        flyIn.Sounds.Clear();
        var random = new Random(3);
        int frames = 0;
        double now = 2000;
        while (flyIn.Running) { Assert.True(Frame(flyIn, now, random)); now += 33; frames++; }
        Assert.InRange(frames, 40, 43);   // done on stepped call 39..42, counting from 0
        Assert.Equal(0, flyIn.Tablet.Sparkles.Length);
        Assert.False(flyIn.Running);
        Assert.Equal(BattleFlyIn.EndSound, Assert.Single(flyIn.Sounds.Pending).Id.Value);
        Assert.True(flyIn.Start(now, 1000, 100, 900, 600));             // free for the next battle
    }

    [Fact]
    public void WithTheGuiHiddenTheCameraLetterboxedOrTheGamePausedItNeitherStepsNorDraws()
    {
        var flyIn = new BattleFlyIn();
        flyIn.Start(0, 1000, 100, 900, 600);
        var random = new Random(3);
        Assert.False(flyIn.BeginFrame(5000, guiShown: false, letterboxed: false, paused: false, Ring, random));
        Assert.False(flyIn.BeginFrame(5000, guiShown: true, letterboxed: true, paused: false, Ring, random));
        Assert.False(flyIn.BeginFrame(5000, guiShown: true, letterboxed: false, paused: true, Ring, random));
        Assert.Equal(0, flyIn.Tablet.Frame);
        Assert.True(flyIn.Running);                                     // the latch waits for the GUI
        Assert.True(Frame(flyIn, 5000, random));                        // gate long passed: it glides at once
        Assert.Equal(1, flyIn.Tablet.Frame);
    }

    [Fact]
    public void TheDrawSeesTheSparklesBornThisFrameBeforeTheStepAgesThem()
    {
        // 0x0042F040: spawn, draw every sparkle, then the step (0x0042F43B).
        var flyIn = new BattleFlyIn();
        flyIn.Start(0, 1000, 100, 900, 600);
        var random = new Random(3);
        Assert.True(flyIn.BeginFrame(2000, true, false, false, Ring, random));
        Assert.Equal(4, flyIn.Tablet.Sparkles.Length);
        Assert.All(flyIn.Tablet.Sparkles.ToArray(), s => Assert.InRange(s.Life, 8, 11));
        Assert.Equal((1000f, 100f), (flyIn.Tablet.X, flyIn.Tablet.Y));   // drawn where it stood
        flyIn.EndFrame(2000);
        Assert.All(flyIn.Tablet.Sparkles.ToArray(), s => Assert.InRange(s.Life, 7, 10));
    }

    private static bool Frame(BattleFlyIn flyIn, double now, Random random)
    {
        if (!flyIn.BeginFrame(now, true, false, false, Ring, random)) return false;
        flyIn.EndFrame(now);
        return true;
    }

    [Fact]
    public void TheWinScreensTabletIsUnchanged_StillGrowingInPlace()
    {
        var tablet = new RewardTablet();
        tablet.Restart();
        var random = new Random(1);
        for (int i = 0; i < 32; i++) tablet.DrawFrame(0, Ring, random);
        Assert.Equal((0f, 0f), (tablet.X, tablet.Y));
        Assert.Equal(RewardTablet.FinalScale, tablet.Scale, 5);
    }

    /// <summary><c>0x00504FB0</c> calls the fly-in only for a creature of the viewer joining a record
    /// that held none of the viewer's: a second one joining, or another keeper's fight, does not.</summary>
    [InstallFact]
    public void TheObserverCountsOnlyTheViewersFirstEntryIntoEachBattle()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        byte knight = db.Creatures.Values.Single(c => c.Name == "Knight").CreatureId;
        CreatureState hero = world.SpawnCreature(db, knight, new PlayerId(1), new TilePos(10, 10));
        CreatureState mine = world.SpawnCreature(db, knight, new PlayerId(3), new TilePos(11, 10));
        CreatureState mate = world.SpawnCreature(db, knight, new PlayerId(3), new TilePos(11, 11));
        Fight(hero, mine);
        Fight(mate, hero);
        var observer = new MusicWorldObserver(new PlayerId(3), new MusicDirector());
        observer.Capture(world, db, new GameTick(100));
        Assert.Equal(1, observer.ViewerBattleEntries);

        CreatureState rival = world.SpawnCreature(db, knight, new PlayerId(4), new TilePos(22, 10));
        CreatureState rivalHero = world.SpawnCreature(db, knight, new PlayerId(1), new TilePos(23, 10));
        Fight(rival, rivalHero);
        Fight(rivalHero, rival);
        observer.Capture(world, db, new GameTick(101));
        Assert.Equal(1, observer.ViewerBattleEntries);
    }

    private static void Fight(CreatureState fighter, CreatureState target)
    {
        fighter.Dk2StateId = 199;
        fighter.FightTargetId = target.Id;
        fighter.FightTargetKind = FightTargetKind.Creature;
    }
}
