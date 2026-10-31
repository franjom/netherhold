// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class CreatureSoundTests
{
    [Theory]
    [InlineData(223)]
    [InlineData(214)]
    public void CreatureEventKeepsItsBankGroupPositionAndVoiceAfterCreatureRemoval(ushort group)
    {
        var events = new ListEventSink();
        var director = new CreatureSoundDirector(events);
        director.Emit(new SimEvent(default, "test", "creature_sound", "")
        {
            CreatureSound = new(new CreatureId(42), "CREATURE_GOBLIN", (CreatureSoundEvent)group,
                new(new Fp32(8192), new Fp32(12288), new Fp32(4096))),
        });
        Assert.True(director.TryTake(out var sound));
        Assert.Equal("creature_goblin", sound.Category.Name);
        Assert.Equal((uint)group, sound.Id.Value);
        Assert.Equal(new Vector3(2, 1, 3), sound.World);
        Assert.Equal(0x8000002Au, sound.Voice.Value);
        Assert.False(director.TryTake(out _));
        Assert.Single(events.Events);
    }
}
