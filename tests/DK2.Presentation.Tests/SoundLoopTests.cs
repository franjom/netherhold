// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The continuous-sound model of the remake (docs/research/sound-effects-continuous.md section 3): a stop request ends a thing's
/// voice (DK2's slot <c>+0x38</c>), a voice follows its thing while it plays (slot <c>+0x30</c>, the per-update replay of a live handle),
/// a group whose flag word has bit <c>0x08</c> loops (instance flags 6, repeat count 9999), and a looping voice whose thing is gone ends.</summary>
public sealed class SoundLoopTests
{
    [Fact]
    public void AStopFromTheSimulationBecomesAStopRequestForTheThingsVoice()
    {
        var director = new WorldSoundDirector();
        director.Emit(new SimEvent(new GameTick(1), "status", "thing_sound", "")
        {
            ThingSound = new ThingSoundRequest(SoundSourceId.Of(new CreatureId(42)), "", default, default, Stop: true),
        });

        Assert.True(director.TryTake(out SoundRequest stop));
        Assert.True(stop.Stop);
        Assert.Equal(WorldSoundDirector.CreatureVoice(42), stop.Voice);
        Assert.False(director.TryTake(out _));
    }

    [Fact]
    public void AListenerRelativeRequestPlaysNonspatiallyWithTheTilesPointAsItsVolumeAnchor()
    {
        var director = new WorldSoundDirector();
        director.Emit(new SimEvent(new GameTick(1), "casino", "thing_sound", "")
        {
            ThingSound = new ThingSoundRequest(new SoundSourceId(SoundSourceKind.Room, 9), "ROOM_CASINO", ThingSoundEvent.PartyMusic,
                new FpPosition3(new Fp32(4 * 4096), new Fp32(7 * 4096), new Fp32(0)), ListenerRelative: true),
        });

        Assert.True(director.TryTake(out SoundRequest request));
        Assert.Null(request.World);
        Assert.Equal(new System.Numerics.Vector3(4f, 0f, -7f), request.VolumeAnchor);
        Assert.Equal(new SoundId(0x316), request.Id);
    }

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(22.5f, 0f, .5f)]
    [InlineData(0f, 22.5f, .5f)]
    [InlineData(45f, 3f, 0f)]
    [InlineData(100f, 0f, 0f)]
    public void TheAnchoredVolumeFallsLinearlyToZeroAtFortyFiveTilesByTheLargerAxis(float dx, float dz, float expected)
    {
        var focus = new System.Numerics.Vector3(10f, 0f, -10f);
        var anchor = new System.Numerics.Vector3(10f + dx, 3f, -10f - dz);
        Assert.Equal(expected, AnchoredVolume.Of(anchor, focus), 4);
    }

    [Fact]
    public void WithNoListenerFocusTheAnchoredVolumeIsFull()
    {
        Assert.Equal(1f, AnchoredVolume.Of(new System.Numerics.Vector3(50f, 0f, 50f), null));
    }

    [Fact]
    public void AStopOfAnUnvoicedRequestIsNotForwarded()
    {
        var director = new WorldSoundDirector();
        director.Emit(new SimEvent(new GameTick(1), "x", "thing_sound", "")
        {
            ThingSound = new ThingSoundRequest(SoundSourceId.Of(new CreatureId(42)), "", default, default, Voiced: false, Stop: true),
        });
        Assert.False(director.TryTake(out _));
    }

    [Fact]
    public void ReleasingAVoiceReturnsItsSourceOnceAndForgetsIt()
    {
        var bindings = new SoundVoiceBindings<string>();
        var voice = new SoundVoiceId(0x80000007);
        bindings.Assign(voice, new SoundId(1), "source");
        var listed = new List<SoundVoiceId>();
        bindings.Voices(listed);
        Assert.Equal([voice], listed);

        Assert.Equal("source", bindings.Release(voice));
        Assert.Null(bindings.Release(voice));
        Assert.False(bindings.TryGet(voice, out _, out _));
        bindings.Voices(listed);
        Assert.Empty(listed);
    }

    [InstallFact]
    public void OnlyAGroupWhoseFlagWordHasBit8Loops()
    {
        string root = Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "Sfx", "Global");
        SoundResolver Of(string name) => new(BankMap.Read(File.ReadAllBytes(Path.Combine(root, name + "BANK.map"))),
            SfxMap.Read(File.ReadAllBytes(Path.Combine(root, name + "SFX.map"))));
        SoundResolver fireball = Of("spell_fireball");
        Assert.True(fireball.IsLooping(new SoundId(0xF2)));    // the shot's second handle: the trail (flags 0x0008)
        Assert.False(fireball.IsLooping(new SoundId(0xF4)));
        Assert.False(fireball.IsLooping(new SoundId(0xFFFF)));
        Assert.True(Of("spell_firebomb").IsLooping(new SoundId(0xF2)));
        Assert.False(Of("creature_imp").IsLooping(new SoundId(0x10D)));
        // 0x0006 (burning, the flies, the boulder's roll, a Tremor's 0x0016) is not 0x08: those loop by being asked again.
        Assert.False(Of("creature_goblin").IsLooping(new SoundId(0x340)));
        Assert.False(Of("keeper_spell_tremor").IsLooping(new SoundId(0xFB)));
    }

    [InstallFact]
    public void AVoiceFollowsAMovingCreatureAndShotAndEndsWhenTheyAreGone()
    {
        (GameDatabase database, WorldState world) = Dk2Fixture();
        CreatureState goblin = world.SpawnCreature(database, 7, PlayerIds.FirstKeeper, new TilePos(3, 3));
        goblin.PosX = 12345;
        goblin.PosY = 23456;
        goblin.PosZ = 4096;
        Assert.Equal(VoiceLocator.Fate.Moves, VoiceLocator.Locate(world, WorldSoundDirector.CreatureVoice(goblin.Id), out float x, out float? h, out float z));
        Assert.Equal(12345 / 4096f, x);
        Assert.Equal(-23456 / 4096f, z);
        Assert.Equal(4096 / 4096f, h);
        Assert.Equal(VoiceLocator.Fate.Gone, VoiceLocator.Locate(world, WorldSoundDirector.CreatureVoice(9999), out _, out _, out _));
    }

    [InstallFact]
    public void AVoiceFollowsAShotAtItsViewPositionAndAnObjectAtItsTileWithoutAHeight()
    {
        (GameDatabase database, WorldState world) = Dk2Fixture();
        ShotState shot = world.SpawnShot(1, PlayerIds.FirstKeeper, 8192, 12288);
        foreach (SoundSourceKind kind in new[] { SoundSourceKind.Shot, SoundSourceKind.ShotSecond })
        {
            var voice = new SoundVoiceId(0x80000000u | ((uint)kind << 24) | (uint)shot.Id);
            Assert.Equal(VoiceLocator.Fate.Moves, VoiceLocator.Locate(world, voice, out float x, out float? h, out float z));
            Assert.Equal((8192 / 4096f, shot.ViewPosition.Z.Raw / 4096f, -12288 / 4096f), (x, h, z));
        }

        ObjectState chicken = world.SpawnObject(database, 9, PlayerIds.FirstKeeper, (2 * world.Width) + 4);
        var objectVoice = new SoundVoiceId(0x80000000u | ((uint)SoundSourceKind.Object << 24) | (uint)chicken.Id);
        Assert.Equal(VoiceLocator.Fate.Moves, VoiceLocator.Locate(world, objectVoice, out float ox, out float? oh, out float oz));
        Assert.Equal((CreatureState.CentreOf(4) / 4096f, (float?)null, -CreatureState.CentreOf(2) / 4096f), (ox, oh, oz));
        world.RemoveObject(chicken.Id);
        Assert.Equal(VoiceLocator.Fate.Gone, VoiceLocator.Locate(world, objectVoice, out _, out _, out _));
    }

    [InstallFact]
    public void AStaticThingOrAnUnownedVoiceNeitherMovesNorEnds()
    {
        (GameDatabase database, WorldState world) = Dk2Fixture();
        foreach (SoundSourceKind kind in new[] { SoundSourceKind.Door, SoundSourceKind.Trap, SoundSourceKind.Room, SoundSourceKind.Terrain,
            SoundSourceKind.Player, SoundSourceKind.Tremor, SoundSourceKind.Corpse })
            Assert.Equal(VoiceLocator.Fate.Static,
                VoiceLocator.Locate(world, new SoundVoiceId(0x80000000u | ((uint)kind << 24) | 5), out _, out _, out _));
        Assert.Equal(VoiceLocator.Fate.Static, VoiceLocator.Locate(world, SoundVoiceId.None, out _, out _, out _));
        Assert.Equal(VoiceLocator.Fate.Static, VoiceLocator.Locate(world, new SoundVoiceId(5), out _, out _, out _));
    }

    private static (GameDatabase, WorldState) Dk2Fixture()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        return (database, WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!, [0, 1]));
    }
}
