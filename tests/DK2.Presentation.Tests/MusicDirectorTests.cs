// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class MusicDirectorTests
{
    [InstallFact]
    public void ABattleSnapshotIncludesItsOwnersAndExpiresOnTheExactDeadline()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3], fogOfWarEnabled: false);
        byte knight = db.Creatures.Values.Single(c => c.Name == "Knight").CreatureId;
        CreatureState hero = world.SpawnCreature(db, knight, new PlayerId(1), new TilePos(10, 10));
        CreatureState keeper = world.SpawnCreature(db, knight, new PlayerId(3), new TilePos(11, 10));
        hero.Dk2StateId = keeper.Dk2StateId = 199;
        hero.FightTargetId = keeper.Id; keeper.FightTargetId = hero.Id;
        hero.FightTargetKind = keeper.FightTargetKind = FightTargetKind.Creature;
        var observer = new MusicWorldObserver(new PlayerId(3), new MusicDirector());
        observer.Capture(world, db, new GameTick(100));
        var battle = Assert.Single(observer.Battles, b => b.MemberCount != 0);
        Assert.Equal(new BattleViewSlot(0), battle.Slot);
        Assert.Equal(new TilePos(11, 10), battle.Centre);
        Assert.True(battle.Owners.Contains(new PlayerId(1)));
        Assert.True(battle.Owners.Contains(new PlayerId(3)));
        hero.Dk2StateId = keeper.Dk2StateId = 1;
        observer.Capture(world, db, new GameTick(115));
        Assert.Equal(2, observer.Battles.Sum(b => b.MemberCount));
        observer.Capture(world, db, new GameTick(116));
        Assert.Equal(0, observer.Battles.Sum(b => b.MemberCount));
    }

    [InstallFact]
    public void AThirdPartyFightRaisesMusicAndObservingDoesNotChangeWorldHash()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        byte knight = db.Creatures.Values.Single(c => c.Name == "Knight").CreatureId;
        CreatureState hero = world.SpawnCreature(db, knight, new PlayerId(1), new TilePos(10, 10));
        CreatureState keeper = world.SpawnCreature(db, knight, new PlayerId(4), new TilePos(11, 10));
        hero.Dk2StateId = keeper.Dk2StateId = 199;
        hero.FightTargetId = keeper.Id; keeper.FightTargetId = hero.Id;
        hero.FightTargetKind = keeper.FightTargetKind = FightTargetKind.Creature;
        var music = new MusicDirector();
        var observer = new MusicWorldObserver(new PlayerId(3), music);
        var before = new Hash64(); world.HashInto(ref before);
        observer.Capture(world, db, new GameTick(100));
        observer.Capture(world, db, new GameTick(101));
        var after = new Hash64(); world.HashInto(ref after);
        Assert.Equal(before.Value, after.Value);
        Assert.Equal(MusicLevel.Battle, music.Level);
    }

    [Theory]
    [InlineData(MusicLevel.Silent, 0)]
    [InlineData(MusicLevel.Raid, 20)]
    [InlineData(MusicLevel.Battle, 40)]
    [InlineData(MusicLevel.LargeBattle, 60)]
    [InlineData(MusicLevel.Reaper, 80)]
    public void LevelsMapToTheNativeIntensities(MusicLevel level, int intensity)
    {
        var music = new MusicDirector(); music.Set(level);
        Assert.Equal(intensity, music.Intensity);
    }

    [Fact]
    public void MusicUsesTheRaisedLevelBeforeDecayAndAmbienceUsesItAfterDecay()
    {
        var music = new MusicDirector(); music.Raise(MusicLevel.Reaper);
        Assert.Equal(new MusicFrame(80, 40), music.AdvanceFrame(true, 1));
        Assert.Equal(MusicLevel.Raid, music.Level);
        music.Raise(MusicLevel.LargeBattle); music.AdvanceFrame(false, 0);
        Assert.Equal(MusicLevel.LargeBattle, music.Level);
    }

    [Fact]
    public void ThePickerFiltersInclusiveRangesAndStaysStoppedUntilIntensityIsSetAgain()
    {
        var empty = new SoundItem([], [], []);
        var first = new SoundItem([], [new SoundLink(2, 0, 20, 39), new SoundLink(3, 0, 40, 100)], []);
        var repeat = new SoundItem([], [new SoundLink(2, 0, 20, 100)], []);
        var picker = new MusicSegmentPicker(new SoundGroup(new SoundId(343), [first, repeat, empty], []), () => 0);
        Assert.Null(picker.Pick()); Assert.True(picker.Stopped);
        Assert.Null(picker.Pick());
        picker.SetIntensity(20);
        Assert.Same(repeat, picker.Pick());
        picker.SetIntensity(39); Assert.Same(repeat, picker.Pick());
        picker.SetIntensity(0); Assert.Null(picker.Pick());
        picker.SetIntensity(40); Assert.Same(empty, picker.Pick());
        Assert.Null(picker.Pick()); Assert.False(picker.Stopped);
    }

    [Fact]
    public void TheAudioQueueCrossesSegmentsDuplicatesMonoAndPadsUnderflow()
    {
        var stream = new PcmAudioQueue(22050);
        stream.Enqueue(new PcmClip(22050, 1, [100, 200]));
        stream.Enqueue(new PcmClip(22050, 2, [300, 400]));
        var samples = new float[8]; stream.Read(samples);
        Assert.Equal(new[] { 100, 100, 200, 200, 300, 400, 0, 0 }, samples.Select(s => (int)(s * 32768)));
        Assert.Equal(0, stream.RemainingFrames);
    }
}
