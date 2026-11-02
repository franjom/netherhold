// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
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
        Assert.True(director.TryTake(out var creature));
        Assert.True(director.TryTake(out var gold));
        Assert.True(director.TryTake(out var door));
        Assert.Equal("object_gold", gold.Category.Name);
        Assert.Equal(3, new[] { creature.Voice, gold.Voice, door.Voice }.Distinct().Count());
        Assert.DoesNotContain(SoundVoiceId.None, new[] { creature.Voice, gold.Voice, door.Voice });
    }

    [Fact]
    public void ARequestWithoutACategoryPlaysNothing()
    {
        var director = new WorldSoundDirector();
        director.Emit(Sound(SoundSourceId.Of(new CreatureId(1)), "", ThingSoundEvent.Sacked));
        Assert.False(director.TryTake(out _));
    }

    private static SimEvent Sound(SoundSourceId source, string category, ThingSoundEvent sound) =>
        new(default, "test", "thing_sound", "")
        {
            ThingSound = new(source, category, sound, new(new Fp32(8192), new Fp32(12288), new Fp32(4096))),
        };
}
