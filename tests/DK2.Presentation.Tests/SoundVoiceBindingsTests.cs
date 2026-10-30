// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation.Tests;

public class SoundVoiceBindingsTests
{
    [Fact]
    public void APlayingVoiceRetainsEqualOrLowerPriorityRequestsEvenWhenTheirEventIdDiffers()
    {
        var bindings = new SoundVoiceBindings<object>();
        bindings.Assign(new SoundVoiceId(1), new SoundId(0x320), new object(), new SoundPriority(3999));
        Assert.True(bindings.CanRetain(new SoundVoiceId(1), new SoundPriority(3999), _ => true));
        Assert.True(bindings.CanRetain(new SoundVoiceId(1), new SoundPriority(3998), _ => true));
        Assert.False(bindings.CanRetain(new SoundVoiceId(1), new SoundPriority(4000), _ => true));
        Assert.False(bindings.CanRetain(new SoundVoiceId(1), new SoundPriority(3999), _ => false));
    }

    [Fact]
    public void ReusingAVoiceSourceForAnUnvoicedSoundRevokesTheOldOwner()
    {
        var bindings = new SoundVoiceBindings<object>();
        var source = new object();
        bindings.Assign(new SoundVoiceId(1), new SoundId(0x321), source);
        bindings.Assign(SoundVoiceId.None, new SoundId(0x30f), source);
        Assert.False(bindings.TryGet(new SoundVoiceId(1), out _, out _));
    }

    [Fact]
    public void ReusingASourceForAnotherVoiceLeavesOnlyTheNewOwnerAbleToStopIt()
    {
        var bindings = new SoundVoiceBindings<object>();
        var source = new object();
        bindings.Assign(new SoundVoiceId(1), new SoundId(0x321), source);
        bindings.Assign(new SoundVoiceId(2), new SoundId(0x320), source);
        Assert.False(bindings.TryGet(new SoundVoiceId(1), out _, out _));
        Assert.True(bindings.TryGet(new SoundVoiceId(2), out SoundId sound, out object? retained));
        Assert.Same(source, retained);
        Assert.Equal(new SoundId(0x320), sound);
    }
}
