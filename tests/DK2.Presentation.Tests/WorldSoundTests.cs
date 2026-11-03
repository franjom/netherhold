// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class WorldSoundTests
{
    [Theory]
    [InlineData(223)]
    [InlineData(214)]
    public void CreatureEventKeepsItsBankGroupPositionAndVoiceAfterCreatureRemoval(ushort group)
    {
        var events = new ListEventSink();
        var director = new WorldSoundDirector(events);
        director.Emit(Sound(SoundSourceId.Of(new CreatureId(42)), "CREATURE_GOBLIN", (ThingSoundEvent)group));
        Assert.True(director.TryTake(out var sound));
        Assert.Equal("creature_goblin", sound.Category.Name);
        Assert.Equal((uint)group, sound.Id.Value);
        Assert.Equal(new Vector3(2, 1, -3), sound.World);   // where DungeonScene.Ground draws (2, 3) at height 1
        Assert.Equal(0x8000002Au, sound.Voice.Value);
        Assert.False(director.TryTake(out _));
        Assert.Single(events.Events);
    }

    [Fact]
    public void ThingsOfDifferentKindsWithTheSameIdKeepSeparateVoices()
    {
        var director = new WorldSoundDirector();
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Creature, 7), "CREATURE_IMP", ThingSoundEvent.BattleCall));
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Object, 7), "OBJECT_GOLD", (ThingSoundEvent)0xC4));
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Door, 7), "DOOR_WOOD", (ThingSoundEvent)0xC4));
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Room, 7), "ROOM_DUNGEON_HEART", (ThingSoundEvent)0x329));
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Player, 7), "ROOM_DUNGEON_HEART", (ThingSoundEvent)0xF4));
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Shot, 7), "SPELL_FIREBALL", ThingSoundEvent.SpellCast));
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.ShotSecond, 7), "SPELL_FIREBALL", ThingSoundEvent.ShotMadeSecond));   // a shot's second handle
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Tremor, 7), "KEEPER_SPELL_TREMOR", ThingSoundEvent.TremorStarted));
        director.Emit(Sound(new SoundSourceId(SoundSourceKind.Corpse, 7), "OBJECT_FLIES", ThingSoundEvent.CorpseDestroyed));
        Assert.True(director.TryTake(out var creature));
        Assert.True(director.TryTake(out var gold));
        Assert.True(director.TryTake(out var door));
        Assert.True(director.TryTake(out var room));
        Assert.True(director.TryTake(out var player));
        Assert.True(director.TryTake(out var shot));
        Assert.True(director.TryTake(out var shotSecond));
        Assert.True(director.TryTake(out var tremor));
        Assert.True(director.TryTake(out var corpse));
        Assert.Equal("object_gold", gold.Category.Name);
        var voices = new[] { creature.Voice, gold.Voice, door.Voice, room.Voice, player.Voice, shot.Voice, shotSecond.Voice, tremor.Voice, corpse.Voice };
        Assert.Equal(9, voices.Distinct().Count());   // the room's handle and the player's, a shot's two, a tremor slot's and a body's are different voices
        Assert.DoesNotContain(SoundVoiceId.None, voices);
    }

    [Fact]
    public void ARequestDK2MakesWithHandleZeroCarriesNoVoice()
    {
        var director = new WorldSoundDirector();
        var request = new ThingSoundRequest(SoundSourceId.Of(new CreatureId(5)), "CREATURE_IMP", (ThingSoundEvent)0xC3,
            new(new Fp32(4096), new Fp32(8192), new Fp32(4096)), Voiced: false);
        director.Emit(new SimEvent(default, "test", "thing_sound", "") { ThingSound = request });
        Assert.True(director.TryTake(out var sound));
        Assert.Equal(SoundVoiceId.None, sound.Voice);
        Assert.Equal(new Vector3(1, 1, -2), sound.World);
    }

    [Fact]
    public void ATileSoundIsPlayedAtItsCentreOnTheGroundWithNoVoice()
    {
        var director = new WorldSoundDirector();
        director.Emit(new SimEvent(default, "test", "thing_sound", "")
        {
            ThingSound = new(new SoundSourceId(SoundSourceKind.Terrain, 91), "TERRAIN_ROCK", ThingSoundEvent.TileBroken,
                new(new Fp32(0x1800), new Fp32(0x2800), new Fp32(0)), Voiced: false),
        });
        Assert.True(director.TryTake(out var sound));
        Assert.Equal("terrain_rock", sound.Category.Name);
        Assert.Equal(0xF4u, sound.Id.Value);
        Assert.Equal(new Vector3(1.5f, 0, -2.5f), sound.World);
        Assert.Equal(SoundVoiceId.None, sound.Voice);
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, false)]
    [InlineData(3, null, false)]
    public void ARequestMarkedForTheLocalPlayersOwnThingPlaysOnlyForThatViewer(byte owner, int? viewer, bool plays)
    {
        var director = new WorldSoundDirector(null, viewer is int v ? new PlayerId((byte)v) : null);
        director.Emit(new SimEvent(default, "test", "thing_sound", "")
        {
            ThingSound = new(new SoundSourceId(SoundSourceKind.Room, 7), "ROOM_DUNGEON_HEART", ThingSoundEvent.HeartOpened,
                new(new Fp32(0x1800), new Fp32(0x1800), new Fp32(0x1000)), Voiced: false, ViewerOnlyFor: new PlayerId(owner)),
        });
        Assert.Equal(plays, director.TryTake(out _));
    }

    [Fact]
    public void ARequestWithoutACategoryPlaysNothing()
    {
        var director = new WorldSoundDirector();
        director.Emit(Sound(SoundSourceId.Of(new CreatureId(1)), "", ThingSoundEvent.Sacked));
        Assert.False(director.TryTake(out _));
    }

    /// <summary>DK2 tests bit 0 of a global counter, then adds one, for every chicken step it offers (<c>0x0049FA70</c>,
    /// <c>0x0049FA9B</c>): the first is silent, the second plays, and so on; other requests do not count.</summary>
    [Fact]
    public void AnAlternatingRequestPlaysOnEverySecondOfferStartingWithTheSecondAndOthersDoNotCount()
    {
        var director = new WorldSoundDirector();
        var chicken = new SoundSourceId(SoundSourceKind.Object, 5);
        SimEvent Step() => new(default, "test", "thing_sound", "")
        {
            ThingSound = new(chicken, "OBJECT_CHICKEN", ThingSoundEvent.ChickenStep, new(new Fp32(0), new Fp32(0), new Fp32(0)), Alternating: true),
        };
        var played = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            director.Emit(Step());
            played.Add(director.TryTake(out _));
            director.Emit(Sound(new SoundSourceId(SoundSourceKind.Creature, 1), "CREATURE_IMP", ThingSoundEvent.BattleCall));
            Assert.True(director.TryTake(out _));   // an ordinary request always plays and never moves the counter
        }
        Assert.Equal([false, true, false, true], played);
    }

    private static SimEvent Sound(SoundSourceId source, string category, ThingSoundEvent sound) =>
        new(default, "test", "thing_sound", "")
        {
            ThingSound = new(source, category, sound, new(new Fp32(8192), new Fp32(12288), new Fp32(4096))),
        };
}
